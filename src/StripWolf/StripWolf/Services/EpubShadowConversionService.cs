using System.IO.Compression;
using System.Text;
using System.Xml;
using StripWolf.Data;
using StripWolf.Models;

namespace StripWolf.Services;

public sealed class EpubShadowConversionService
{
    private const int BackgroundBatchSize = 1;
    private static readonly TimeSpan DesktopForegroundPriorityWindow = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan AndroidBackgroundLoopDelay = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan WindowsBackgroundLoopDelay = TimeSpan.FromMilliseconds(225);
    private readonly DatabaseService _databaseService;
    private readonly EpubToCbzConverterService _epubConverter;
    private readonly SettingsService _settingsService;
    private readonly string _appDataDirectory;
    private readonly string _comicsDirectory;
    private readonly string _shadowDirectory;
    private readonly Dictionary<int, SemaphoreSlim> _comicGates = new();
    private readonly Dictionary<int, Task> _backgroundTasks = new();
    private readonly Dictionary<int, EpubToCbzConverterService.EpubIncrementalConversionSession> _conversionSessions = new();
    private readonly Dictionary<int, CancellationTokenSource> _activeReaderSessions = new();
    private readonly Dictionary<int, DateTime> _lastForegroundRequests = new();
    private readonly object _lock = new();

    public event EventHandler<int>? ConversionStateChanged;
    public event EventHandler<int>? ConversionFinalized;

    public EpubShadowConversionService(
        DatabaseService databaseService,
        EpubToCbzConverterService epubConverter,
        SettingsService settingsService)
    {
        _databaseService = databaseService;
        _epubConverter = epubConverter;
        _settingsService = settingsService;
        _appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StripWolf");
        _comicsDirectory = Path.Combine(_appDataDirectory, "Comics");
        _shadowDirectory = Path.Combine(_appDataDirectory, "EpubShadow");
        Directory.CreateDirectory(_comicsDirectory);
        Directory.CreateDirectory(_shadowDirectory);
    }

    public async Task<string> StoreManagedSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (IsPathWithinDirectory(sourcePath, _comicsDirectory))
        {
            return sourcePath;
        }

        Directory.CreateDirectory(_comicsDirectory);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var candidatePath = Path.Combine(_comicsDirectory, SanitizeFileName($"{baseName}{extension}"));
        var suffix = 1;
        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(_comicsDirectory, SanitizeFileName($"{baseName}-{suffix}{extension}"));
            suffix++;
        }

        await using var sourceStream = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destinationStream = File.Create(candidatePath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        return candidatePath;
    }

    public Task<EpubConversionState?> GetConversionStateAsync(int comicId)
    {
        return _databaseService.GetEpubConversionStateAsync(comicId);
    }

    public async Task<List<EpubConversionState>> GetActiveConversionsAsync()
    {
        return (await _databaseService.GetIncompleteEpubConversionStatesAsync())
            .Where(state => state.Status == EpubConversionStatus.Converting)
            .ToList();
    }

    public void StartReadingSession(int comicId)
    {
        lock (_lock)
        {
            if (_activeReaderSessions.TryGetValue(comicId, out var existing) && !existing.IsCancellationRequested)
            {
                return;
            }

            existing?.Dispose();
            _activeReaderSessions[comicId] = new CancellationTokenSource();
        }
    }

    public async Task StopReadingSessionAsync(int comicId)
    {
        CancellationTokenSource? cancellationSource = null;
        lock (_lock)
        {
            if (_activeReaderSessions.TryGetValue(comicId, out cancellationSource))
            {
                _activeReaderSessions.Remove(comicId);
            }
        }

        cancellationSource?.Cancel();
        cancellationSource?.Dispose();
        await DisposeTrackedSessionAsync(comicId);

        var state = await _databaseService.GetEpubConversionStateAsync(comicId);
        if (state is not null && state.Status == EpubConversionStatus.Converting)
        {
            state.Status = EpubConversionStatus.Paused;
            state.UpdatedAtUtc = DateTime.UtcNow;
            await _databaseService.SaveEpubConversionStateAsync(state);
            ConversionStateChanged?.Invoke(this, comicId);
        }
    }

    public async Task<EpubConversionState> InitializePendingConversionAsync(Comic comic, ComicInfo? comicInfo, CancellationToken cancellationToken = default)
    {
        var existing = await _databaseService.GetEpubConversionStateAsync(comic.Id);
        if (existing is not null)
        {
            return existing;
        }

        var shadowPath = Path.Combine(_shadowDirectory, comic.Id.ToString());
        Directory.CreateDirectory(shadowPath);
        await WriteComicInfoAsync(shadowPath, comicInfo, cancellationToken);

        var state = new EpubConversionState
        {
            ComicId = comic.Id,
            SourceEpubPath = comic.FilePath,
            ShadowPath = shadowPath,
            Status = EpubConversionStatus.Pending,
            ProducedPageCount = CountRenderedPages(shadowPath),
            FinalPageCount = null,
            NextChapterIndex = 0,
            NextPageIndexInChapter = 0,
            PaginationSignature = CreatePaginationSignature(),
            UpdatedAtUtc = DateTime.UtcNow
        };

        await _databaseService.SaveEpubConversionStateAsync(state);
        ConversionStateChanged?.Invoke(this, comic.Id);
        return state;
    }

    public async Task<(string readPath, EpubConversionState? state)> EnsurePagesAvailableAsync(
        Comic comic,
        int requestedPage,
        int pagesAhead = 1,
        CancellationToken cancellationToken = default)
    {
        NoteForegroundRequest(comic.Id);
        var state = await _databaseService.GetEpubConversionStateAsync(comic.Id);
        if (state is null)
        {
            return (comic.FilePath, null);
        }

        var gate = GetGate(comic.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            state = await _databaseService.GetEpubConversionStateAsync(comic.Id) ?? state;
            var targetPage = Math.Max(0, requestedPage + Math.Max(0, pagesAhead));
            if (state.Status != EpubConversionStatus.Completed && state.ProducedPageCount <= targetPage)
            {
                await ProducePagesCoreAsync(comic, state, targetPage, cancellationToken);
                NoteForegroundRequest(comic.Id);
                state = await _databaseService.GetEpubConversionStateAsync(comic.Id) ?? state;
            }

            if (IsReadingSessionActive(comic.Id) && ShouldContinueInBackground())
            {
                StartBackgroundContinuation(comic.Id, comic);
            }
            else if (state.Status == EpubConversionStatus.Converting)
            {
                state.Status = EpubConversionStatus.Paused;
                state.UpdatedAtUtc = DateTime.UtcNow;
                await _databaseService.SaveEpubConversionStateAsync(state);
                ConversionStateChanged?.Invoke(this, comic.Id);
            }

            return (state.Status == EpubConversionStatus.Completed ? comic.FilePath : state.ShadowPath, state);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteConversionArtifactsAsync(int comicId)
    {
        var state = await _databaseService.GetEpubConversionStateAsync(comicId);
        if (state is null)
        {
            return;
        }

        await DisposeTrackedSessionAsync(comicId);

        if (Directory.Exists(state.ShadowPath))
        {
            try
            {
                Directory.Delete(state.ShadowPath, true);
            }
            catch
            {
            }
        }

        await _databaseService.DeleteEpubConversionStateAsync(comicId);
        CleanupTracking(comicId);
        ConversionStateChanged?.Invoke(this, comicId);
    }

    private void StartBackgroundContinuation(int comicId, Comic comic)
    {
        lock (_lock)
        {
            if (_backgroundTasks.TryGetValue(comicId, out var existing) && !existing.IsCompleted)
            {
                return;
            }

            _backgroundTasks[comicId] = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        if (!TryGetReaderCancellationToken(comicId, out var backgroundCancellationToken))
                        {
                            return;
                        }

                        var quietDelay = GetForegroundPriorityDelay(comicId);
                        if (quietDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(quietDelay, backgroundCancellationToken);
                            continue;
                        }

                        var gate = GetGate(comicId);
                        await gate.WaitAsync(backgroundCancellationToken);
                        try
                        {
                            var state = await _databaseService.GetEpubConversionStateAsync(comicId);
                            if (state is null || state.Status == EpubConversionStatus.Completed)
                            {
                                return;
                            }

                            var targetPage = state.ProducedPageCount + BackgroundBatchSize - 1;
                            await ProducePagesCoreAsync(comic, state, targetPage, backgroundCancellationToken);
                        }
                        finally
                        {
                            gate.Release();
                        }

                        await Task.Delay(GetBackgroundLoopDelay(), backgroundCancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    var state = await _databaseService.GetEpubConversionStateAsync(comicId);
                    if (state is not null && state.Status != EpubConversionStatus.Completed)
                    {
                        await DisposeTrackedSessionAsync(comicId);
                        state.Status = EpubConversionStatus.Paused;
                        state.UpdatedAtUtc = DateTime.UtcNow;
                        await _databaseService.SaveEpubConversionStateAsync(state);
                        ConversionStateChanged?.Invoke(this, comicId);
                    }
                }
                catch (Exception ex)
                {
                    var state = await _databaseService.GetEpubConversionStateAsync(comicId);
                    if (state is not null)
                    {
                        await DisposeTrackedSessionAsync(comicId);
                        state.Status = EpubConversionStatus.Failed;
                        state.LastError = ex.Message;
                        state.UpdatedAtUtc = DateTime.UtcNow;
                        await _databaseService.SaveEpubConversionStateAsync(state);
                        ConversionStateChanged?.Invoke(this, comicId);
                    }
                }
            });
        }
    }

    private async Task ProducePagesCoreAsync(
        Comic comic,
        EpubConversionState state,
        int targetPage,
        CancellationToken cancellationToken)
    {
        if (state.Status == EpubConversionStatus.Completed)
        {
            return;
        }

        await NormalizeShadowDirectoryAsync(state);

        state.Status = EpubConversionStatus.Converting;
        state.LastError = null;
        state.UpdatedAtUtc = DateTime.UtcNow;
        await _databaseService.SaveEpubConversionStateAsync(state);
        ConversionStateChanged?.Invoke(this, comic.Id);

        var session = await GetOrCreateTrackedSessionAsync(comic.Id, state, cancellationToken);

        try
        {
            while (state.ProducedPageCount <= targetPage)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pagePath = GetRenderedPagePath(state.ShadowPath, state.ProducedPageCount);
                var temporaryPagePath = $"{pagePath}.tmp";
                if (File.Exists(temporaryPagePath))
                {
                    File.Delete(temporaryPagePath);
                }

                EpubToCbzConverterService.EpubIncrementalPageResult? result;
                await using (var pageStream = new FileStream(temporaryPagePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    result = await session.RenderNextPageToStreamAsync(pageStream, cancellationToken);
                }

                if (result is null)
                {
                    if (File.Exists(temporaryPagePath))
                    {
                        File.Delete(temporaryPagePath);
                    }

                    await FinalizeCompletedConversionAsync(comic, state);
                    return;
                }

                if (File.Exists(pagePath))
                {
                    File.Delete(pagePath);
                }

                File.Move(temporaryPagePath, pagePath);
                state.ProducedPageCount++;
                state.NextChapterIndex = session.NextChapterIndex;
                state.NextPageIndexInChapter = session.NextPageIndexInChapter;
                state.UpdatedAtUtc = DateTime.UtcNow;
                await _databaseService.SaveEpubConversionStateAsync(state);
                ConversionStateChanged?.Invoke(this, comic.Id);
            }
        }
        catch
        {
            await DisposeTrackedSessionAsync(comic.Id);
            throw;
        }
    }

    private async Task FinalizeCompletedConversionAsync(Comic comic, EpubConversionState state)
    {
        await DisposeTrackedSessionAsync(comic.Id);
        var finalPageCount = CountRenderedPages(state.ShadowPath);
        var finalCbzPath = GetFinalCbzPath(state.SourceEpubPath, comic.Id);
        if (File.Exists(finalCbzPath))
        {
            File.Delete(finalCbzPath);
        }

        using (var archive = ZipFile.Open(finalCbzPath, ZipArchiveMode.Create))
        {
            var comicInfoPath = Path.Combine(state.ShadowPath, "ComicInfo.xml");
            if (File.Exists(comicInfoPath))
            {
                archive.CreateEntryFromFile(comicInfoPath, "ComicInfo.xml", CompressionLevel.Optimal);
            }

            foreach (var pagePath in Directory.EnumerateFiles(state.ShadowPath, "Page_*.*")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                archive.CreateEntryFromFile(pagePath, Path.GetFileName(pagePath), CompressionLevel.NoCompression);
            }
        }

        comic.FilePath = finalCbzPath;
        comic.Format = ComicFormat.Cbz;
        comic.PageCount = finalPageCount;
        comic.FileSize = new FileInfo(finalCbzPath).Length;
        await _databaseService.SaveComicAsync(comic);

        state.Status = EpubConversionStatus.Completed;
        state.FinalPageCount = finalPageCount;
        state.UpdatedAtUtc = DateTime.UtcNow;
        await _databaseService.DeleteEpubConversionStateAsync(comic.Id);

        if (File.Exists(state.SourceEpubPath) && IsPathWithinDirectory(state.SourceEpubPath, _comicsDirectory))
        {
            try
            {
                File.Delete(state.SourceEpubPath);
            }
            catch
            {
            }
        }

        if (Directory.Exists(state.ShadowPath))
        {
            try
            {
                Directory.Delete(state.ShadowPath, true);
            }
            catch
            {
            }
        }

        CleanupTracking(comic.Id);
        ConversionFinalized?.Invoke(this, comic.Id);
        ConversionStateChanged?.Invoke(this, comic.Id);
    }

    private async Task NormalizeShadowDirectoryAsync(EpubConversionState state)
    {
        Directory.CreateDirectory(state.ShadowPath);
        var renderedPages = Directory.EnumerateFiles(state.ShadowPath, "Page_*.*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (renderedPages.Count < state.ProducedPageCount)
        {
            state.ProducedPageCount = renderedPages.Count;
            state.UpdatedAtUtc = DateTime.UtcNow;
            await _databaseService.SaveEpubConversionStateAsync(state);
        }
        else if (renderedPages.Count > state.ProducedPageCount)
        {
            foreach (var extraFile in renderedPages.Skip(state.ProducedPageCount))
            {
                try
                {
                    File.Delete(extraFile);
                }
                catch
                {
                }
            }
        }
    }

    private static string GetRenderedPagePath(string shadowPath, int zeroBasedPageIndex)
    {
        return Path.Combine(shadowPath, $"Page_{zeroBasedPageIndex + 1:D5}.png");
    }

    private string GetFinalCbzPath(string sourceEpubPath, int comicId)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourceEpubPath);
        var candidatePath = Path.Combine(_comicsDirectory, SanitizeFileName($"{baseName}.cbz"));
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        return Path.Combine(_comicsDirectory, SanitizeFileName($"{baseName}-{comicId}.cbz"));
    }

    private static int CountRenderedPages(string shadowPath)
    {
        if (!Directory.Exists(shadowPath))
        {
            return 0;
        }

        return Directory.EnumerateFiles(shadowPath, "Page_*.*").Count();
    }

    private async Task WriteComicInfoAsync(string shadowPath, ComicInfo? comicInfo, CancellationToken cancellationToken)
    {
        if (comicInfo is null)
        {
            return;
        }

        var comicInfoPath = Path.Combine(shadowPath, "ComicInfo.xml");
        await Task.Run(() =>
        {
            using var outputStream = File.Create(comicInfoPath);
            ComicInfoXmlService.Write(outputStream, comicInfo);
        }, cancellationToken);
        
        cancellationToken.ThrowIfCancellationRequested();
    }

    private string CreatePaginationSignature()
    {
        var settings = _settingsService.LoadSettings();
        return $"{settings.EpubConversionTheme}|{settings.EpubOutputResolution}";
    }

    private SemaphoreSlim GetGate(int comicId)
    {
        lock (_lock)
        {
            if (!_comicGates.TryGetValue(comicId, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _comicGates[comicId] = gate;
            }

            return gate;
        }
    }

    private void CleanupTracking(int comicId)
    {
        lock (_lock)
        {
            _backgroundTasks.Remove(comicId);
            _comicGates.Remove(comicId);
            _conversionSessions.Remove(comicId);
            _lastForegroundRequests.Remove(comicId);
        }
    }

    private bool IsReadingSessionActive(int comicId)
    {
        lock (_lock)
        {
            return _activeReaderSessions.TryGetValue(comicId, out var cancellationSource) &&
                   !cancellationSource.IsCancellationRequested;
        }
    }

    private static bool ShouldContinueInBackground()
    {
        return OperatingSystem.IsAndroid() || OperatingSystem.IsWindows();
    }

    private void NoteForegroundRequest(int comicId)
    {
        lock (_lock)
        {
            _lastForegroundRequests[comicId] = DateTime.UtcNow;
        }
    }

    private TimeSpan GetForegroundPriorityDelay(int comicId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return TimeSpan.Zero;
        }

        lock (_lock)
        {
            if (!_lastForegroundRequests.TryGetValue(comicId, out var lastRequestUtc))
            {
                return TimeSpan.Zero;
            }

            var quietUntilUtc = lastRequestUtc + DesktopForegroundPriorityWindow;
            var remaining = quietUntilUtc - DateTime.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    private static TimeSpan GetBackgroundLoopDelay()
    {
        return OperatingSystem.IsWindows() ? WindowsBackgroundLoopDelay : AndroidBackgroundLoopDelay;
    }

    private async Task<EpubToCbzConverterService.EpubIncrementalConversionSession> GetOrCreateTrackedSessionAsync(
        int comicId,
        EpubConversionState state,
        CancellationToken cancellationToken)
    {
        EpubToCbzConverterService.EpubIncrementalConversionSession? existingSession = null;
        lock (_lock)
        {
            if (_conversionSessions.TryGetValue(comicId, out var existing))
            {
                return existing;
            }
        }

        var created = await _epubConverter.CreateIncrementalConversionSessionAsync(
            state.SourceEpubPath,
            state.NextChapterIndex,
            state.NextPageIndexInChapter,
            cancellationToken: cancellationToken);

        lock (_lock)
        {
            if (_conversionSessions.TryGetValue(comicId, out var existing))
            {
                existingSession = existing;
            }
            else
            {
                _conversionSessions[comicId] = created;
                return created;
            }
        }

        await created.DisposeAsync();
        return existingSession;
    }

    private async Task DisposeTrackedSessionAsync(int comicId)
    {
        EpubToCbzConverterService.EpubIncrementalConversionSession? session = null;
        lock (_lock)
        {
            if (_conversionSessions.TryGetValue(comicId, out session))
            {
                _conversionSessions.Remove(comicId);
            }
        }

        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    private bool TryGetReaderCancellationToken(int comicId, out CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_activeReaderSessions.TryGetValue(comicId, out var cancellationSource) &&
                !cancellationSource.IsCancellationRequested)
            {
                cancellationToken = cancellationSource.Token;
                return true;
            }
        }

        cancellationToken = CancellationToken.None;
        return false;
    }

    private static bool IsPathWithinDirectory(string filePath, string directoryPath)
    {
        var fullFilePath = Path.GetFullPath(filePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullFilePath.StartsWith(fullDirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullFilePath, fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}
