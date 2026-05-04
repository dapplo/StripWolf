using System.Xml.Serialization;
using StripWolf.Data;
using StripWolf.Models;
using StripWolf.Models.Komga;

namespace StripWolf.Services;

/// <summary>
/// Service for managing the local comic library
/// </summary>
public class LibraryService
{
    private readonly DatabaseService _databaseService;
    private readonly ComicReaderService _comicReaderService;
    private readonly KomgaApiService _komgaApiService;
    private readonly PdfToCbzConverterService _pdfConverter;
    private readonly EpubToCbzConverterService _epubConverter;
    private readonly ComicConverterService _comicConverter;
    private readonly string _appDataDirectory;
    private readonly string _comicsDirectory;
    private readonly string _coversDirectory;

    /// <summary>
    /// Event raised when the library content changes (add, delete, import)
    /// </summary>
    public event EventHandler? LibraryChanged;

    public LibraryService(
        DatabaseService databaseService,
        ComicReaderService comicReaderService,
        KomgaApiService komgaApiService,
        PdfToCbzConverterService pdfConverter,
        EpubToCbzConverterService epubConverter,
        ComicConverterService comicConverter)
    {
        _databaseService = databaseService;
        _comicReaderService = comicReaderService;
        _komgaApiService = komgaApiService;
        _pdfConverter = pdfConverter;
        _epubConverter = epubConverter;
        _comicConverter = comicConverter;
        
        _appDataDirectory = GetAppDataDirectory();
        _comicsDirectory = Path.Combine(_appDataDirectory, "Comics");
        _coversDirectory = Path.Combine(_appDataDirectory, "Covers");
        
        Directory.CreateDirectory(_comicsDirectory);
        Directory.CreateDirectory(_coversDirectory);
    }

    /// <summary>
    /// Gets the comics directory path
    /// </summary>
    public string ComicsDirectory => _comicsDirectory;

    private static string GetAppDataDirectory()
    {
        // Cross-platform app data directory
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "StripWolf");
    }

    private bool IsAppOwnedSourcePath(string filePath)
    {
        return IsPathWithinDirectory(filePath, _appDataDirectory);
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

    private static async Task DeleteManagedSourceFileAsync(string filePath)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        System.Diagnostics.Debug.WriteLine($"Warning: Failed to delete managed source file '{filePath}' after conversion.");
    }

    /// <summary>
    /// Gets all comics in the local library
    /// </summary>
    public Task<List<Comic>> GetAllComicsAsync()
    {
        return _databaseService.GetComicsAsync();
    }

    /// <summary>
    /// Checks if all comic files exist and removes missing ones from the library
    /// </summary>
    public async Task<int> CleanupMissingFilesAsync()
    {
        var comics = await _databaseService.GetComicsAsync();
        var missingComics = comics.Where(c => !File.Exists(c.FilePath)).ToList();
        
        foreach (var comic in missingComics)
        {
            await _databaseService.DeleteComicAsync(comic);
            
            if (!string.IsNullOrEmpty(comic.CoverPath) && File.Exists(comic.CoverPath))
            {
                try { File.Delete(comic.CoverPath); } catch { /* Ignore cleanup errors */ }
            }
        }
        
        return missingComics.Count;
    }

    /// <summary>
    /// Gets recently read or added comics
    /// </summary>
    public Task<List<Comic>> GetRecentComicsAsync(int count = 10)
    {
        return _databaseService.GetRecentComicsAsync(count);
    }

    /// <summary>
    /// Gets comics that are currently being read
    /// </summary>
    public Task<List<Comic>> GetInProgressComicsAsync()
    {
        return _databaseService.GetInProgressComicsAsync();
    }

    /// <summary>
    /// Gets comics that have been completely read
    /// </summary>
    public Task<List<Comic>> GetCompletedComicsAsync()
    {
        return _databaseService.GetCompletedComicsAsync();
    }

    /// <summary>
    /// Gets comics that haven't been started yet
    /// </summary>
    public Task<List<Comic>> GetNewComicsAsync()
    {
        return _databaseService.GetNewComicsAsync();
    }

    /// <summary>
    /// Gets all comics downloaded from a specific Komga server
    /// </summary>
    public Task<List<Comic>> GetComicsByKomgaServerIdAsync(int serverId)
    {
        return _databaseService.GetComicsByKomgaServerIdAsync(serverId);
    }

    /// <summary>
    /// Gets a comic by Komga ID or hash
    /// </summary>
    public Task<Comic?> GetComicByKomgaIdOrHashAsync(string komgaId, string? fileHash)
    {
        return _databaseService.GetComicByKomgaIdOrHashAsync(komgaId, fileHash);
    }

    /// <summary>
    /// Gets a comic by ID
    /// </summary>
    public Task<Comic?> GetComicAsync(int id)
    {
        return _databaseService.GetComicAsync(id);
    }

    /// <summary>
    /// Gets the next locally available comic in the same series, based on library series ordering.
    /// </summary>
    public async Task<Comic?> GetNextComicInSeriesAsync(int comicId)
    {
        var currentComic = await _databaseService.GetComicAsync(comicId);
        if (currentComic is null || string.IsNullOrWhiteSpace(currentComic.SeriesName))
        {
            return null;
        }

        var normalizedSeriesName = NormalizeSeriesName(currentComic.SeriesName);
        var orderedSeriesComics = (await _databaseService.GetComicsAsync())
            .Where(comic => !string.IsNullOrWhiteSpace(comic.SeriesName) &&
                            string.Equals(NormalizeSeriesName(comic.SeriesName!), normalizedSeriesName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(comic => comic.Number ?? float.MaxValue)
            .ThenBy(comic => comic.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(comic => comic.Id)
            .ToList();

        var currentIndex = orderedSeriesComics.FindIndex(comic => comic.Id == comicId);
        if (currentIndex < 0 || currentIndex >= orderedSeriesComics.Count - 1)
        {
            return null;
        }

        return orderedSeriesComics[currentIndex + 1];
    }

    /// <summary>
    /// Searches comics by title, series name, or authors
    /// </summary>
    public Task<List<Comic>> SearchComicsAsync(string searchText)
    {
        return _databaseService.SearchComicsAsync(searchText);
    }

    private static string NormalizeSeriesName(string seriesName)
    {
        return string.Join(' ', seriesName
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Toggle the read/unread status of a comic
    /// </summary>
    public Task ToggleReadStatusAsync(int comicId)
    {
        return _databaseService.ToggleReadStatusAsync(comicId);
    }

    /// <summary>
    /// Gets comics marked as favorites
    /// </summary>
    public Task<List<Comic>> GetFavoriteComicsAsync()
    {
        return _databaseService.GetFavoriteComicsAsync();
    }

    /// <summary>
    /// Toggle the favorite status of a comic
    /// </summary>
    public Task ToggleFavoriteAsync(int comicId)
    {
        return _databaseService.ToggleFavoriteAsync(comicId);
    }

    /// <summary>
    /// Gets the ComicInfo metadata from a comic file
    /// </summary>
    /// <param name="filePath">Path to the comic file</param>
    /// <returns>ComicInfo if found, null otherwise</returns>
    public Task<ComicInfo?> GetComicInfoAsync(string filePath)
    {
        return _comicConverter.ExtractComicInfoAsync(filePath);
    }

    /// <summary>
    /// Imports a local comic file into the library
    /// </summary>
    public async Task<Comic> ImportLocalComicAsync(string filePath, IProgress<double>? progress = null)
    {
        // Check if already imported
        var existing = await _databaseService.GetComicByFilePathAsync(filePath);
        if (existing is not null)
        {
            return existing;
        }

        var format = ComicReaderService.GetComicFormat(filePath);
        if (format == ComicFormat.Unknown)
        {
            throw new NotSupportedException("Unsupported comic format. Only CBZ, CBR, CB7, CBT, PDF, and EPUB files are supported.");
        }

        // Determine if conversion to CBZ is needed
        string actualFilePath = filePath;
        var needsConversion = format == ComicFormat.Pdf ||
                              format == ComicFormat.Epub ||
                              format == ComicFormat.Cb7 || 
                              format == ComicFormat.Cbt ||
                              (format == ComicFormat.Cbr && ComicConverterService.IsSolidRar(filePath));

        if (needsConversion)
        {
            if (format == ComicFormat.Pdf)
            {
                // PDF conversion
                actualFilePath = await _pdfConverter.ConvertPdfToCbzAsync(filePath, _comicsDirectory, progress);
            }
            else if (format == ComicFormat.Epub)
            {
                actualFilePath = await _epubConverter.ConvertEpubToCbzAsync(filePath, _comicsDirectory, progress: progress);
            }
            else
            {
                // Convert CB7, CBT, or solid CBR to CBZ
                actualFilePath = await _comicConverter.ConvertToCbzAsync(filePath, _comicsDirectory, progress);
            }

            if (actualFilePath != filePath && File.Exists(filePath) && IsAppOwnedSourcePath(filePath))
            {
                await DeleteManagedSourceFileAsync(filePath);
            }
        }

        // Extract ComicInfo.xml metadata if available
        ComicInfo? comicInfo = null;
        try
        {
            comicInfo = await _comicConverter.ExtractComicInfoAsync(actualFilePath);
        }
        catch
        {
            // ComicInfo extraction failed, continue without it
        }

        var (pageCount, fileSize) = await _comicReaderService.GetComicInfoAsync(actualFilePath);
        
        // Generate a unique ID for the cover filename
        var coverId = Guid.NewGuid().ToString();
        
        // Extract cover to the unified covers directory with unique filename
        string? coverPath = null;
        try
        {
            coverPath = await ExtractCoverToUnifiedDirectoryAsync(actualFilePath, coverId);
        }
        catch
        {
            // Cover extraction failed, continue without cover
        }

        // Build comic metadata - prefer ComicInfo.xml data over filename
        var title = comicInfo?.Title ?? Path.GetFileNameWithoutExtension(filePath);
        var seriesName = comicInfo?.Series;
        float? number = null;
        if (!string.IsNullOrEmpty(comicInfo?.Number) && float.TryParse(comicInfo.Number, out var parsedNumber))
        {
            number = parsedNumber;
        }

        var comic = new Comic
        {
            Title = title,
            SeriesName = seriesName,
            Number = number,
            Summary = comicInfo?.Summary,
            Publisher = comicInfo?.Publisher,
            Authors = comicInfo?.GetSimpleAuthors(),
            ReleaseDate = comicInfo?.GetReleaseDate(),
            FilePath = actualFilePath,
            PageCount = pageCount,
            FileSize = fileSize,
            CoverPath = coverPath,
            Format = needsConversion ? ComicFormat.Cbz : format,
            Source = ComicSource.Local,
            AddedDate = DateTime.UtcNow
        };

        await _databaseService.SaveComicAsync(comic);
        OnLibraryChanged();
        return comic;
    }

    /// <summary>
    /// Downloads a comic from Komga and adds it to the library
    /// </summary>
    public async Task<Comic> DownloadFromKomgaAsync(KomgaBook book, int? serverId = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        // Check if already downloaded by ID or Hash
        var existing = await _databaseService.GetComicByKomgaIdOrHashAsync(book.Id, book.FileHash);
        if (existing is not null)
        {
            return existing;
        }

        // Determine file extension from media type (strip parameters like "; version=4")
        var baseMediaType = book.Media?.MediaType?.Split(';')[0].Trim();
        var extension = baseMediaType switch
        {
            "application/zip" => ".cbz",
            "application/x-rar-compressed" => ".cbr",
            "application/x-7z-compressed" => ".cb7",
            "application/x-tar" => ".cbt",
            "application/pdf" => ".pdf",
            "application/epub+zip" => ".epub",
            _ => ".cbz"
        };

        var fileName = SanitizeFileName($"{book.SeriesTitle} - {book.Name}{extension}");
        var filePath = Path.Combine(_comicsDirectory, fileName);
        string actualFilePath = filePath;
        string? coverPath = null;

        try
        {
            // Download the book
            var success = await _komgaApiService.DownloadBookToFileAsync(book.Id, filePath, progress, cancellationToken);
            if (!success)
            {
                throw new Exception("Failed to download comic from Komga");
            }
            
            cancellationToken.ThrowIfCancellationRequested();

            // Check if downloaded file needs conversion (solid RAR, CB7, CBT, PDF)
            var format = ComicReaderService.GetComicFormat(filePath);
            var needsConversion = format == ComicFormat.Pdf ||
                                  format == ComicFormat.Epub ||
                                  format == ComicFormat.Cb7 || 
                                  format == ComicFormat.Cbt ||
                                  (format == ComicFormat.Cbr && ComicConverterService.IsSolidRar(filePath));
            var pageCount = book.Media?.PagesCount ?? 0;
            var fileSize = book.SizeBytes;

            if (needsConversion)
            {
                if (format == ComicFormat.Pdf)
                {
                    actualFilePath = await _pdfConverter.ConvertPdfToCbzAsync(filePath, _comicsDirectory, progress);
                }
                else if (format == ComicFormat.Epub)
                {
                    actualFilePath = await _epubConverter.ConvertEpubToCbzAsync(filePath, _comicsDirectory, progress: progress, cancellationToken: cancellationToken);
                }
                else
                {
                    actualFilePath = await _comicConverter.ConvertToCbzAsync(filePath, _comicsDirectory, null);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Delete original file after successful conversion
                if (actualFilePath != filePath && File.Exists(filePath))
                {
                    // Add a small delay and retry to allow system to release locks
                    bool deleted = false;
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            File.Delete(filePath);
                            deleted = true;
                            break;
                        }
                        catch
                        {
                            await Task.Delay(500, cancellationToken);
                        }
                    }
                    
                    if (!deleted)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Failed to delete original file '{filePath}' after conversion.");
                    }
                }

                // update the pagecount and filesize after conversion
                (pageCount, fileSize) = await _comicReaderService.GetComicInfoAsync(actualFilePath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Extract ComicInfo.xml metadata if available
            ComicInfo? comicInfo = null;
            try
            {
                comicInfo = await _comicConverter.ExtractComicInfoAsync(actualFilePath);
            }
            catch
            {
                // ComicInfo extraction failed, continue without it
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Download thumbnail to unified covers directory
            var thumbnailData = await _komgaApiService.GetBookThumbnailAsync(book.Id);

            if (thumbnailData is not null)
            {
                coverPath = Path.Combine(_coversDirectory, $"{book.Id}.jpg");
                await File.WriteAllBytesAsync(coverPath, thumbnailData, cancellationToken);
            }
            else
            {
                // Extract cover if Komga didn't have one
                try
                {
                    coverPath = await ExtractCoverToUnifiedDirectoryAsync(actualFilePath, book.Id);
                }
                catch
                {
                    // Cover extraction failed, continue without cover
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Parse release date
            DateTime? releaseDate = null;
            if (!string.IsNullOrEmpty(book.Metadata?.ReleaseDate))
            {
                if (DateTime.TryParse(book.Metadata.ReleaseDate, out var parsed))
                {
                    releaseDate = parsed;
                }
            }

            var comic = new Comic
            {
                KomgaId = book.Id,
                KomgaHash = book.FileHash,
                KomgaSeriesId = book.SeriesId,
                Title = book.Metadata?.Title ?? book.Name,
                SeriesName = book.SeriesTitle,
                Number = book.Number,
                Summary = book.Metadata?.Summary,
                Authors = book.Metadata?.Authors is not null 
                    ? string.Join(", ", book.Metadata.Authors.Select(a => a.Name))
                    : null,
                ReleaseDate = releaseDate,
                FilePath = actualFilePath,
                PageCount = pageCount,
                FileSize = fileSize,
                CoverPath = coverPath,
                Format = needsConversion ? ComicFormat.Cbz : format,
                Source = ComicSource.Komga,
                AddedDate = DateTime.UtcNow,
                CurrentPage = book.ReadProgress?.Page ?? 0,
                IsCompleted = book.ReadProgress?.Completed ?? false,
                KomgaServerId = serverId
            };

            // Create sidecar ComicInfo.xml if not already embedded
            if (comicInfo == null)
            {
                var newInfo = CreateComicInfoFromKomgaBook(book);
                await WriteComicInfoSidecarAsync(newInfo, actualFilePath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _databaseService.SaveComicAsync(comic);
            OnLibraryChanged();
            return comic;
        }
        catch (OperationCanceledException)
        {
            CleanupPartialFile(actualFilePath);
            if (!string.Equals(actualFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                CleanupPartialFile(filePath);
            }

            if (!string.IsNullOrEmpty(coverPath))
            {
                CleanupPartialFile(coverPath);
            }

            throw;
        }
    }

    /// <summary>
    /// Updates reading progress for a comic
    /// </summary>
    public async Task UpdateReadingProgressAsync(Comic comic, int currentPage)
    {
        var isCompleted = currentPage >= comic.PageCount - 1;
        
        await _databaseService.UpdateReadingProgressAsync(comic.Id, currentPage, isCompleted);
        
        // Sync with Komga if this is a Komga comic
        if (comic.Source == ComicSource.Komga && !string.IsNullOrEmpty(comic.KomgaId) && _komgaApiService.IsConfigured)
        {
            try
            {
                await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId, currentPage + 1, isCompleted);
            }
            catch
            {
                // Failed to sync with Komga, continue anyway
            }
        }
    }

    /// <summary>
    /// Removes a comic from the library database but keeps the physical file
    /// </summary>
    public async Task RemoveComicFromLibraryAsync(Comic comic)
    {
        // Delete cover (now just a single file in unified covers directory)
        if (!string.IsNullOrEmpty(comic.CoverPath) && File.Exists(comic.CoverPath))
        {
            try
            {
                File.Delete(comic.CoverPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        await _databaseService.DeleteComicAsync(comic);
        OnLibraryChanged();
    }

    /// <summary>
    /// Deletes a comic from the library
    /// </summary>
    public async Task DeleteComicAsync(Comic comic)
    {
        // If we want to protect the file, e.g. only delete if file is in our managed directory, add this: comic.FilePath.StartsWith(_comicsDirectory)

        if (File.Exists(comic.FilePath))
        {
            File.Delete(comic.FilePath);
        }

        // Delete cover (now just a single file in unified covers directory)
        if (!string.IsNullOrEmpty(comic.CoverPath) && File.Exists(comic.CoverPath))
        {
            try
            {
                File.Delete(comic.CoverPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        await _databaseService.DeleteComicAsync(comic);
        OnLibraryChanged();
    }

    /// <summary>
    /// Scans a directory for comic files and imports them
    /// </summary>
    public async Task<List<Comic>> ScanAndImportDirectoryAsync(string directoryPath)
    {
        var comics = new List<Comic>();
        
        if (!Directory.Exists(directoryPath))
        {
            return comics;
        }

        var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".cb7", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".cbt", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".epub", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            try
            {
                var comic = await ImportLocalComicAsync(file);
                comics.Add(comic);
            }
            catch
            {
                // Skip files that can't be imported
            }
        }

        return comics;
    }

    /// <summary>
    /// Extracts a cover to the unified covers directory with the specified ID as filename
    /// </summary>
    private async Task<string> ExtractCoverToUnifiedDirectoryAsync(string comicFilePath, string coverId)
    {
        var coverData = await _comicReaderService.GetPageAsync(comicFilePath, 0);
        var pageNames = await _comicReaderService.GetPageNamesAsync(comicFilePath);
        
        if (pageNames.Count == 0)
        {
            throw new InvalidOperationException("Comic has no pages");
        }

        var extension = Path.GetExtension(pageNames[0]);
        var coverPath = Path.Combine(_coversDirectory, $"{coverId}{extension}");
        
        await File.WriteAllBytesAsync(coverPath, coverData);
        
        return coverPath;
    }

    private ComicInfo CreateComicInfoFromKomgaBook(KomgaBook book)
    {
        var metadata = book.Metadata;
        var info = new ComicInfo
        {
            Title = metadata?.Title ?? book.Name,
            Series = book.SeriesTitle,
            Number = book.Number.ToString(),
            Summary = metadata?.Summary,
            Tags = metadata?.Tags != null ? string.Join(", ", metadata.Tags) : null,
            Writer = metadata?.Authors?.FirstOrDefault(a => a.Role?.Equals("writer", StringComparison.OrdinalIgnoreCase) == true)?.Name,
            Penciller = metadata?.Authors?.FirstOrDefault(a => a.Role?.Equals("penciller", StringComparison.OrdinalIgnoreCase) == true)?.Name,
            Inker = metadata?.Authors?.FirstOrDefault(a => a.Role?.Equals("inker", StringComparison.OrdinalIgnoreCase) == true)?.Name,
            Colorist = metadata?.Authors?.FirstOrDefault(a => a.Role?.Equals("colorist", StringComparison.OrdinalIgnoreCase) == true)?.Name,
            Letterer = metadata?.Authors?.FirstOrDefault(a => a.Role?.Equals("letterer", StringComparison.OrdinalIgnoreCase) == true)?.Name,
            CoverArtist = metadata?.Authors?.FirstOrDefault(a => a.Role?.Equals("cover artist", StringComparison.OrdinalIgnoreCase) == true)?.Name,
            Editor = metadata?.Authors?.FirstOrDefault(a => a.Role?.Equals("editor", StringComparison.OrdinalIgnoreCase) == true)?.Name,
        };

        if (DateTime.TryParse(metadata?.ReleaseDate, out var date))
        {
            info.Year = date.Year;
            info.Month = date.Month;
            info.Day = date.Day;
        }

        return info;
    }

    private async Task WriteComicInfoSidecarAsync(ComicInfo comicInfo, string comicFilePath)
    {
        var sidecarPath = Path.ChangeExtension(comicFilePath, ".xml");
        
        await Task.Run(() =>
        {
            try
            {
                var serializer = new XmlSerializer(typeof(ComicInfo));
                var namespaces = new XmlSerializerNamespaces();
                namespaces.Add("", "");

                var settings = new System.Xml.XmlWriterSettings
                {
                    Indent = true,
                    Encoding = System.Text.Encoding.UTF8,
                    OmitXmlDeclaration = false
                };

                using var writer = System.Xml.XmlWriter.Create(sidecarPath, settings);
                serializer.Serialize(writer, comicInfo, namespaces);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write sidecar ComicInfo for {comicFilePath}: {ex.Message}");
            }
        });
    }

    internal static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void CleanupPartialFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to clean up partial file '{path}': {ex.Message}");
        }
    }

    private void OnLibraryChanged()
    {
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }
}
