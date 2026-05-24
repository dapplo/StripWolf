// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StripWolf.Core.Data;
using StripWolf.Core.Models;
using StripWolf.Core.Models.Komga;

namespace StripWolf.Core.Services;

/// <summary>
/// Service for managing the local comic library
/// </summary>
public class LibraryService
{
    private const int CoverThumbnailMaxWidth = 300;
    private const int CoverThumbnailMaxHeight = 400;
    private const int CoverThumbnailJpegQuality = 85;

    private readonly DatabaseService _databaseService;
    private readonly ComicReaderService _comicReaderService;
    private readonly KomgaApiService _komgaApiService;
    private readonly SettingsService _settingsService;
    private readonly INetworkConnectionService _networkConnectionService;
    private readonly PdfToCbzConverterService _pdfConverter;
    private readonly EpubToCbzConverterService _epubConverter;
    private readonly EpubShadowConversionService _epubShadowConversionService;
    private readonly ComicConverterService _comicConverter;
    private readonly string _appDataDirectory;
    private readonly string _comicsDirectory;
    private readonly string _coversDirectory;
    private readonly object _libraryChangedLock = new();
    private int _deferredLibraryChangedCount;
    private bool _libraryChangedPending;

    /// <summary>
    /// Event raised when the library content changes (add, delete, import)
    /// </summary>
    public event EventHandler? LibraryChanged;

    public LibraryService(
        DatabaseService databaseService,
        ComicReaderService comicReaderService,
        KomgaApiService komgaApiService,
        SettingsService settingsService,
        INetworkConnectionService networkConnectionService,
        PdfToCbzConverterService pdfConverter,
        EpubToCbzConverterService epubConverter,
        EpubShadowConversionService epubShadowConversionService,
        ComicConverterService comicConverter)
    {
        _databaseService = databaseService;
        _comicReaderService = comicReaderService;
        _komgaApiService = komgaApiService;
        _settingsService = settingsService;
        _networkConnectionService = networkConnectionService;
        _pdfConverter = pdfConverter;
        _epubConverter = epubConverter;
        _epubShadowConversionService = epubShadowConversionService;
        _comicConverter = comicConverter;
        
        _appDataDirectory = GetAppDataDirectory();
        _comicsDirectory = Path.Combine(_appDataDirectory, "Comics");
        _coversDirectory = Path.Combine(_appDataDirectory, "Covers");
        
        Directory.CreateDirectory(_comicsDirectory);
        Directory.CreateDirectory(_coversDirectory);

        _epubShadowConversionService.ConversionFinalized += (_, _) => OnLibraryChanged();
    }

    /// <summary>
    /// Gets the comics directory path
    /// </summary>
    public string ComicsDirectory => _comicsDirectory;

    /// <summary>
    /// Defers LibraryChanged notifications until the returned scope is disposed.
    /// Nested scopes are supported and only raise a single change event when the outermost scope completes.
    /// </summary>
    public IDisposable DeferLibraryChanged()
    {
        lock (_libraryChangedLock)
        {
            _deferredLibraryChangedCount++;
        }

        return new DeferredLibraryChangedScope(this);
    }

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

    private UnsupportedFormatHandlingMode GetUnsupportedFormatHandlingMode()
    {
        return _settingsService.LoadSettings().UnsupportedFormatHandlingMode;
    }

    private bool ShouldRenderUnsupportedFormatWhileReading(ComicFormat format)
    {
        return GetUnsupportedFormatHandlingMode() == UnsupportedFormatHandlingMode.ConvertWhileReading &&
               (format == ComicFormat.Pdf || format == ComicFormat.Epub);
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

    private static string? NormalizeSeriesName(string? seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            return null;
        }

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
    public async Task<ComicInfo?> GetComicInfoAsync(string filePath)
    {
        var format = ComicReaderService.GetComicFormat(filePath);
        return format switch
        {
            ComicFormat.Pdf => await _pdfConverter.ExtractComicInfoAsync(filePath),
            ComicFormat.Epub => await _epubConverter.ExtractComicInfoAsync(filePath),
            _ => await _comicConverter.ExtractComicInfoAsync(filePath)
        };
    }

    /// <summary>
    /// Imports a local comic file into the library
    /// </summary>
    public async Task<Comic> ImportLocalComicAsync(string filePath, IProgress<double>? progress = null, string? seriesNameFallback = null)
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
            throw new NotSupportedException("Unsupported comic format.");
        }

        using var importData = await PrepareLocalImportAsync(filePath, format, progress);
        var actualFilePath = importData.FilePath;
        var isLazyEpubImport = format == ComicFormat.Epub && ShouldRenderUnsupportedFormatWhileReading(format);
        if (isLazyEpubImport)
        {
            progress?.Report(0.75);
            actualFilePath = await _epubShadowConversionService.StoreManagedSourceAsync(actualFilePath);
            progress?.Report(0.9);
        }

        if (!string.Equals(actualFilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(filePath) &&
            IsAppOwnedSourcePath(filePath))
        {
            await DeleteManagedSourceFileAsync(filePath);
        }

        var comicInfo = importData.ComicInfo;
        var pageCount = importData.PageCount;
        var fileSize = importData.FileSize;
        
        // Generate a unique ID for the cover filename
        var coverId = Guid.NewGuid().ToString();
        
        // Extract cover to the unified covers directory with unique filename
        string? coverPath = null;
        if (importData.CoverImageStream is not null)
        {
            try
            {
                coverPath = isLazyEpubImport
                    ? await CreateCoverImageAsync(importData.CoverImageStream, coverId, preferOriginal: true)
                    : await CreateCoverThumbnailAsync(importData.CoverImageStream, coverId);
            }
            catch
            {
                // Cover extraction failed, continue without cover
            }
        }

        // Build comic metadata - prefer ComicInfo.xml data over filename
        var title = !string.IsNullOrWhiteSpace(comicInfo?.Title)
            ? comicInfo.Title
            : Path.GetFileNameWithoutExtension(filePath);
        var seriesName = !string.IsNullOrWhiteSpace(comicInfo?.Series)
            ? comicInfo.Series
            : NormalizeSeriesName(seriesNameFallback);
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
            Format = importData.Format,
            Source = ComicSource.Local,
            AddedDate = DateTime.UtcNow
        };

        await _databaseService.SaveComicAsync(comic);

        if (isLazyEpubImport)
        {
            await _epubShadowConversionService.InitializePendingConversionAsync(comic, comicInfo);
            progress?.Report(1);
        }

        OnLibraryChanged();
        return comic;
    }

    /// <summary>
    /// Downloads a Komga book to the managed comics directory without importing it into the library yet.
    /// Returns null when the book is already present in the library.
    /// </summary>
    public async Task<KomgaDownloadedFile?> DownloadKomgaBookAsync(KomgaBook book, int? serverId = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_networkConnectionService.IsConnectionMetered() && !_settingsService.LoadSettings().AllowMeteredKomgaDownloads)
        {
            throw new InvalidOperationException("Downloads on metered connections are disabled. Enable the setting to allow this.");
        }

        var existing = await _databaseService.GetComicByKomgaIdOrHashAsync(book.Id, book.FileHash);
        if (existing is not null)
        {
            return null;
        }

        var filePath = GetKomgaDownloadFilePath(book);

        try
        {
            var downloadResult = await _komgaApiService.DownloadBookToFileAsync(book.Id, filePath, progress, cancellationToken);
            if (!downloadResult.Success)
            {
                throw new Exception(downloadResult.ErrorMessage ?? "Failed to download comic from Komga");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var format = ComicReaderService.GetComicFormat(filePath);
            return new KomgaDownloadedFile
            {
                Book = book,
                ServerId = serverId,
                FilePath = filePath,
                Format = format,
                RequiresConversion = RequiresKomgaConversion(filePath, format)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw;
        }
    }

    public void CleanupPendingKomgaDownload(KomgaBook book)
    {
        var filePath = GetKomgaDownloadFilePath(book);
        CleanupPartialFile(filePath);
        CleanupPartialFile(filePath + ".partial");
    }

    private string GetKomgaDownloadFilePath(KomgaBook book)
    {
        var extension = GetKomgaDownloadExtension(book);
        var fileName = SanitizeFileName($"{book.SeriesTitle} - {book.Name}{extension}");
        return Path.Combine(_comicsDirectory, fileName);
    }

    /// <summary>
    /// Imports a previously downloaded Komga file into the library, converting it when needed.
    /// </summary>
    public async Task<Comic> ImportDownloadedKomgaBookAsync(KomgaDownloadedFile downloadedFile, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var existing = await _databaseService.GetComicByKomgaIdOrHashAsync(downloadedFile.Book.Id, downloadedFile.Book.FileHash);
        if (existing is not null)
        {
            if (!string.Equals(existing.FilePath, downloadedFile.FilePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(downloadedFile.FilePath) &&
                IsAppOwnedSourcePath(downloadedFile.FilePath))
            {
                await DeleteManagedSourceFileAsync(downloadedFile.FilePath);
            }

            return existing;
        }

        var filePath = downloadedFile.FilePath;
        var actualFilePath = filePath;
        string? coverPath = null;
        ComicImportData? convertedImportData = null;
        var pageCount = downloadedFile.Book.Media?.PagesCount ?? 0;
        var fileSize = downloadedFile.Book.SizeBytes;
        var needsConversion = downloadedFile.RequiresConversion;
        var renderWhileReading = needsConversion && ShouldRenderUnsupportedFormatWhileReading(downloadedFile.Format);

        try
        {
            if (needsConversion)
            {
                if (downloadedFile.Format == ComicFormat.Pdf)
                {
                    convertedImportData = renderWhileReading
                        ? await _pdfConverter.AnalyzePdfForImportAsync(filePath)
                        : await _pdfConverter.ConvertPdfToCbzForImportAsync(filePath, _comicsDirectory, progress);
                }
                else if (downloadedFile.Format == ComicFormat.Epub)
                {
                    convertedImportData = renderWhileReading
                        ? await _epubConverter.AnalyzeEpubForImportAsync(filePath, progress: progress, cancellationToken: cancellationToken)
                        : await _epubConverter.ConvertEpubToCbzForImportAsync(filePath, _comicsDirectory, progress: progress, cancellationToken: cancellationToken);
                }
                else
                {
                    convertedImportData = await _comicConverter.ConvertToCbzForImportAsync(filePath, _comicsDirectory, progress);
                }

                actualFilePath = convertedImportData.FilePath;
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.Equals(actualFilePath, filePath, StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
                {
                    var deleted = false;
                    for (var attempt = 0; attempt < 5; attempt++)
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

                pageCount = convertedImportData.PageCount;
                fileSize = convertedImportData.FileSize;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var comicInfo = convertedImportData?.ComicInfo;
            if (comicInfo is null)
            {
                try
                {
                    comicInfo = await GetComicInfoAsync(actualFilePath);
                }
                catch
                {
                    // ComicInfo extraction failed, continue without it
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            var thumbnailData = await _komgaApiService.GetBookThumbnailAsync(downloadedFile.Book.Id);
            if (thumbnailData is not null)
            {
                coverPath = Path.Combine(_coversDirectory, $"{downloadedFile.Book.Id}.jpg");
                await File.WriteAllBytesAsync(coverPath, thumbnailData, cancellationToken);
            }
            else
            {
                try
                {
                    if (convertedImportData?.CoverImageStream is not null)
                    {
                        coverPath = renderWhileReading && downloadedFile.Format == ComicFormat.Epub
                            ? await CreateCoverImageAsync(convertedImportData.CoverImageStream, downloadedFile.Book.Id, preferOriginal: true)
                            : await CreateCoverThumbnailAsync(convertedImportData.CoverImageStream, downloadedFile.Book.Id);
                    }
                    else
                    {
                        coverPath = await ExtractCoverToUnifiedDirectoryAsync(actualFilePath, downloadedFile.Book.Id);
                    }
                }
                catch
                {
                    // Cover extraction failed, continue without cover
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            DateTime? releaseDate = null;
            if (!string.IsNullOrEmpty(downloadedFile.Book.Metadata?.ReleaseDate) &&
                DateTime.TryParse(downloadedFile.Book.Metadata.ReleaseDate, out var parsed))
            {
                releaseDate = parsed;
            }

            var comic = new Comic
            {
                KomgaId = downloadedFile.Book.Id,
                KomgaHash = downloadedFile.Book.FileHash,
                KomgaSeriesId = downloadedFile.Book.SeriesId,
                Title = downloadedFile.Book.Metadata?.Title ?? downloadedFile.Book.Name,
                SeriesName = downloadedFile.Book.SeriesTitle,
                Number = downloadedFile.Book.Number,
                Summary = downloadedFile.Book.Metadata?.Summary,
                Authors = downloadedFile.Book.Metadata?.Authors is not null
                    ? string.Join(", ", downloadedFile.Book.Metadata.Authors.Select(author => author.Name))
                    : null,
                ReleaseDate = releaseDate,
                FilePath = actualFilePath,
                PageCount = pageCount,
                FileSize = fileSize,
                CoverPath = coverPath,
                Format = convertedImportData?.Format ?? (needsConversion ? ComicFormat.Cbz : downloadedFile.Format),
                Source = ComicSource.Komga,
                AddedDate = DateTime.UtcNow,
                CurrentPage = downloadedFile.Book.ReadProgress?.Page ?? 0,
                IsCompleted = downloadedFile.Book.ReadProgress?.Completed ?? false,
                KomgaServerId = downloadedFile.ServerId
            };

            if (comicInfo == null)
            {
                var newInfo = CreateComicInfoFromKomgaBook(downloadedFile.Book);
                await WriteComicInfoSidecarAsync(newInfo, actualFilePath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _databaseService.SaveComicAsync(comic);

            if (renderWhileReading && downloadedFile.Format == ComicFormat.Epub)
            {
                progress?.Report(0.9);
                await _epubShadowConversionService.InitializePendingConversionAsync(comic, comicInfo);
                progress?.Report(1);
            }

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
        finally
        {
            convertedImportData?.Dispose();
        }
    }

    /// <summary>
    /// Downloads a comic from Komga and adds it to the library.
    /// </summary>
    public async Task<Comic> DownloadFromKomgaAsync(KomgaBook book, int? serverId = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var downloadedFile = await DownloadKomgaBookAsync(book, serverId, progress, cancellationToken);
        if (downloadedFile is null)
        {
            return await _databaseService.GetComicByKomgaIdOrHashAsync(book.Id, book.FileHash)
                ?? throw new InvalidOperationException($"Komga book '{book.Name}' is already present but could not be reloaded from the library.");
        }

        return await ImportDownloadedKomgaBookAsync(downloadedFile, progress, cancellationToken);
    }

    /// <summary>
    /// Updates reading progress for a comic
    /// </summary>
    /// <summary>
    /// Updates comic metadata in the database and persists it to the ComicInfo.xml file within the archive.
    /// </summary>
    public async Task UpdateComicMetadataAsync(Comic comic, ComicInfo comicInfo)
    {
        // 1. Update the comic object and database
        comic.Title = !string.IsNullOrWhiteSpace(comicInfo.Title) ? comicInfo.Title : comic.Title;
        comic.SeriesName = !string.IsNullOrWhiteSpace(comicInfo.Series) ? comicInfo.Series : comic.SeriesName;
        
        if (!string.IsNullOrEmpty(comicInfo.Number) && float.TryParse(comicInfo.Number, out var parsedNumber))
        {
            comic.Number = parsedNumber;
        }
        
        comic.Summary = comicInfo.Summary;
        comic.Publisher = comicInfo.Publisher;
        comic.Authors = comicInfo.GetSimpleAuthors();
        comic.ReleaseDate = comicInfo.GetReleaseDate();

        await _databaseService.SaveComicAsync(comic);

        // 2. Persist to the file if it's a ZIP/CBZ
        if (comic.Format == ComicFormat.Cbz)
        {
            await WriteComicInfoAsync(comic.FilePath, comicInfo);
        }

        OnLibraryChanged();
    }

    private async Task WriteComicInfoAsync(string filePath, ComicInfo comicInfo)
    {
        await Task.Run(() =>
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Update))
                {
                    // Remove existing ComicInfo.xml if present
                    var existingEntry = archive.Entries.FirstOrDefault(e => 
                        Path.GetFileName(e.FullName).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                    existingEntry?.Delete();

                    // Create new entry
                    var entry = archive.CreateEntry("ComicInfo.xml", CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    
                    var settings = new System.Xml.XmlWriterSettings
                    {
                        Indent = true,
                        Encoding = System.Text.Encoding.UTF8,
                        OmitXmlDeclaration = false
                    };

                    using var writer = System.Xml.XmlWriter.Create(entryStream, settings);
                    WriteComicInfo(writer, comicInfo);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update ComicInfo.xml for {filePath}: {ex.Message}");
                throw;
            }
        });
    }

    public async Task UpdateReadingProgressAsync(Comic comic, int currentPage, DateTime? lastModified = null, bool? isCompletedOverride = null)
    {
        if (comic.PageCount <= 0)
        {
            return;
        }

        bool isCompleted;
        if (isCompletedOverride.HasValue)
        {
            isCompleted = isCompletedOverride.Value;
        }
        else
        {
            var conversionState = await _epubShadowConversionService.GetConversionStateAsync(comic.Id);
            isCompleted = conversionState is null && currentPage >= comic.PageCount - 1;
        }
        
        await _databaseService.UpdateReadingProgressAsync(comic.Id, currentPage, isCompleted, lastModified);
        
        // Sync with Komga if this is a Komga comic and it's a local update (lastModified == null)
        if (lastModified == null && comic.Source == ComicSource.Komga && !string.IsNullOrEmpty(comic.KomgaId) && _komgaApiService.IsConfigured)
        {
            try
            {
                await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId, currentPage + 1, isCompleted);
            }
            catch (Exception ex)
            {
                // Failed to sync with Komga, continue anyway
                Logger.Error($"Failed to sync reading progress to Komga for comic '{comic.Title}' (Komga ID: {comic.KomgaId}), Page: {currentPage + 1}", ex);
            }
        }

        OnLibraryChanged();
    }

    public Task<EpubConversionState?> GetEpubConversionStateAsync(int comicId)
    {
        return _epubShadowConversionService.GetConversionStateAsync(comicId);
    }

    public async Task<Comic> PrepareComicForReadingAsync(Comic comic, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (comic.Format != ComicFormat.Epub ||
            !Path.GetExtension(comic.FilePath).Equals(".epub", StringComparison.OrdinalIgnoreCase))
        {
            return comic;
        }

        using var importData = await _epubConverter.ConvertEpubToCbzForImportAsync(
            comic.FilePath,
            _comicsDirectory,
            progress: progress,
            cancellationToken: cancellationToken);

        var originalFilePath = comic.FilePath;
        comic.FilePath = importData.FilePath;
        comic.Format = importData.Format;
        comic.PageCount = importData.PageCount;
        comic.FileSize = importData.FileSize;

        if (!string.Equals(importData.FilePath, originalFilePath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(originalFilePath) &&
            IsAppOwnedSourcePath(originalFilePath))
        {
            await DeleteManagedSourceFileAsync(originalFilePath);
        }

        await _databaseService.SaveComicAsync(comic);
        OnLibraryChanged();
        return comic;
    }

    public async Task<Comic> ConvertPendingEpubAsync(Comic comic, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var state = await _epubShadowConversionService.GetConversionStateAsync(comic.Id);
        if (state is null)
        {
            return comic;
        }

        await _epubShadowConversionService.StopReadingSessionAsync(comic.Id);

        using var importData = await _epubConverter.ConvertEpubToCbzForImportAsync(
            state.SourceEpubPath,
            _comicsDirectory,
            progress: progress,
            cancellationToken: cancellationToken);

        await _epubShadowConversionService.DeleteConversionArtifactsAsync(comic.Id);

        var originalFilePath = comic.FilePath;
        comic.FilePath = importData.FilePath;
        comic.Format = importData.Format;
        comic.PageCount = importData.PageCount;
        comic.FileSize = importData.FileSize;

        if (!string.Equals(originalFilePath, importData.FilePath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(originalFilePath) &&
            IsAppOwnedSourcePath(originalFilePath))
        {
            await DeleteManagedSourceFileAsync(originalFilePath);
        }

        await _databaseService.SaveComicAsync(comic);
        OnLibraryChanged();
        return comic;
    }

    /// <summary>
    /// Removes a comic from the library database but keeps the physical file
    /// </summary>
    public async Task RemoveComicFromLibraryAsync(Comic comic)
    {
        await _epubShadowConversionService.DeleteConversionArtifactsAsync(comic.Id);

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
        await _epubShadowConversionService.DeleteConversionArtifactsAsync(comic.Id);

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

        var files = GetSupportedComicFilesInDirectory(directoryPath);

        using var deferredLibraryChanged = DeferLibraryChanged();
        foreach (var file in files)
        {
            try
            {
                var comic = await ImportLocalComicAsync(file, seriesNameFallback: GetDirectorySeriesNameFallback(file, directoryPath));
                comics.Add(comic);
            }
            catch
            {
                // Skip files that can't be imported
            }
        }

        return comics;
    }

    public static string GetDirectoryDisplayName(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return string.Empty;
        }

        var trimmedPath = Path.TrimEndingDirectorySeparator(directoryPath);
        return Path.GetFileName(trimmedPath);
    }

    public static string GetSuggestedSeriesNameFromDirectory(string directoryPath)
    {
        return GetSuggestedSeriesNameFromDirectoryName(GetDirectoryDisplayName(directoryPath));
    }

    public static string GetSuggestedSeriesNameFromDirectoryName(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return string.Empty;
        }

        var match = Regex.Match(directoryName, @"^[A-Za-z0-9_ ]+");
        var candidate = match.Success ? match.Value : string.Empty;
        candidate = candidate.Replace('_', ' ');
        candidate = Regex.Replace(candidate, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(candidate.ToLower(CultureInfo.CurrentCulture));
    }

    public List<string> GetSupportedComicFilesInDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        return Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(filePath => !ComicConstants.IsIgnoredImportPath(filePath) && ComicConstants.IsSupportedComicFile(filePath))
            .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? GetDirectorySeriesNameFallback(string filePath, string rootDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(rootDirectoryPath))
        {
            return null;
        }

        var relativeDirectory = Path.GetRelativePath(rootDirectoryPath, Path.GetDirectoryName(filePath) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(relativeDirectory) ||
            relativeDirectory == "." ||
            relativeDirectory.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        var segments = relativeDirectory
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !segment.Equals(".", StringComparison.Ordinal) &&
                              !segment.Equals("..", StringComparison.Ordinal))
            .ToArray();

        return segments.Length == 0 ? null : string.Join(" / ", segments);
    }

    /// <summary>
    /// Extracts a cover to the unified covers directory with the specified ID as filename
    /// </summary>
    private async Task<ComicImportData> PrepareLocalImportAsync(string filePath, ComicFormat format, IProgress<double>? progress)
    {
        var needsConversion = format == ComicFormat.Pdf ||
                               format == ComicFormat.Epub ||
                               (format == ComicFormat.Cbr && ComicConverterService.IsSolidRar(filePath));

        if (needsConversion)
        {
            if (format == ComicFormat.Pdf)
            {
                if (ShouldRenderUnsupportedFormatWhileReading(format))
                {
                    return await _pdfConverter.AnalyzePdfForImportAsync(filePath);
                }

                return await _pdfConverter.ConvertPdfToCbzForImportAsync(filePath, _comicsDirectory, progress);
            }

            if (format == ComicFormat.Epub)
            {
                if (ShouldRenderUnsupportedFormatWhileReading(format))
                {
                    return await _epubConverter.AnalyzeEpubForImportAsync(filePath, progress: progress);
                }

                return await _epubConverter.ConvertEpubToCbzForImportAsync(filePath, _comicsDirectory, progress: progress);
            }

            return await _comicConverter.ConvertToCbzForImportAsync(filePath, _comicsDirectory, progress);
        }

        return await _comicConverter.AnalyzeArchiveForImportAsync(filePath);
    }

    private async Task<string> ExtractCoverToUnifiedDirectoryAsync(string comicFilePath, string coverId)
    {
        using var coverStream = RecyclableStreamManagerProvider.Manager.GetStream(nameof(LibraryService));
        await _comicReaderService.CopyPageWithoutCacheAsync(comicFilePath, 0, coverStream);
        coverStream.Position = 0;
        return await CreateCoverThumbnailAsync(coverStream, coverId);
    }

    private async Task<string> CreateCoverImageAsync(Stream imageStream, string coverId, bool preferOriginal)
    {
        if (preferOriginal && TryGetImageExtension(imageStream, out var extension))
        {
            var coverPath = Path.Combine(_coversDirectory, $"{coverId}{extension}");
            if (imageStream.CanSeek)
            {
                imageStream.Position = 0;
            }

            await using var outputStream = File.Create(coverPath);
            await imageStream.CopyToAsync(outputStream);
            return coverPath;
        }

        return await CreateCoverThumbnailAsync(imageStream, coverId);
    }

    private async Task<string> CreateCoverThumbnailAsync(Stream imageStream, string coverId)
    {
        var coverPath = Path.Combine(_coversDirectory, $"{coverId}.jpg");

        if (imageStream.CanSeek)
        {
            imageStream.Position = 0;
        }

        await Task.Run(() =>
        {
            if (imageStream.CanSeek)
            {
                imageStream.Position = 0;
            }

            using var sourceImage = Image.Load<Rgba32>(imageStream);
            sourceImage.Mutate(context => context.AutoOrient());

            var resizeOptions = new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(CoverThumbnailMaxWidth, CoverThumbnailMaxHeight),
                Sampler = KnownResamplers.Lanczos3
            };

            using var resizedImage = sourceImage.Clone(context => context.Resize(resizeOptions));
            using var flattenedImage = new Image<Rgba32>(resizedImage.Width, resizedImage.Height, Color.White.ToPixel<Rgba32>());
            flattenedImage.Mutate(context => context.DrawImage(resizedImage, 1f));

            using var outputStream = File.Create(coverPath);
            flattenedImage.SaveAsJpeg(outputStream, new JpegEncoder
            {
                Quality = CoverThumbnailJpegQuality
            });

            // Return pooled memory to the system
            Configuration.Default.MemoryAllocator.ReleaseRetainedResources();
        });

        return coverPath;
    }

    private static bool TryGetImageExtension(Stream imageStream, out string extension)
    {
        extension = string.Empty;
        if (!imageStream.CanSeek)
        {
            return false;
        }

        var originalPosition = imageStream.Position;
        Span<byte> header = stackalloc byte[16];
        var read = imageStream.Read(header);
        imageStream.Position = originalPosition;

        if (read >= 3 &&
            header[0] == 0xFF &&
            header[1] == 0xD8 &&
            header[2] == 0xFF)
        {
            extension = ".jpg";
            return true;
        }

        if (read >= 8 &&
            header[0] == 0x89 &&
            header[1] == 0x50 &&
            header[2] == 0x4E &&
            header[3] == 0x47 &&
            header[4] == 0x0D &&
            header[5] == 0x0A &&
            header[6] == 0x1A &&
            header[7] == 0x0A)
        {
            extension = ".png";
            return true;
        }

        if (read >= 6 &&
            header[0] == 0x47 &&
            header[1] == 0x49 &&
            header[2] == 0x46 &&
            header[3] == 0x38)
        {
            extension = ".gif";
            return true;
        }

        if (read >= 2 &&
            header[0] == 0x42 &&
            header[1] == 0x4D)
        {
            extension = ".bmp";
            return true;
        }

        if (read >= 12 &&
            header[0] == 0x52 &&
            header[1] == 0x49 &&
            header[2] == 0x46 &&
            header[3] == 0x46 &&
            header[8] == 0x57 &&
            header[9] == 0x45 &&
            header[10] == 0x42 &&
            header[11] == 0x50)
        {
            extension = ".webp";
            return true;
        }

        return false;
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
                var settings = new System.Xml.XmlWriterSettings
                {
                    Indent = true,
                    Encoding = System.Text.Encoding.UTF8,
                    OmitXmlDeclaration = false
                };

                using var writer = System.Xml.XmlWriter.Create(sidecarPath, settings);
                WriteComicInfo(writer, comicInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write sidecar ComicInfo for {comicFilePath}: {ex.Message}");
            }
        });
    }

    private static void WriteComicInfo(System.Xml.XmlWriter writer, ComicInfo info)
    {
        writer.WriteStartElement("ComicInfo");
        
        if (!string.IsNullOrEmpty(info.Title)) writer.WriteElementString("Title", info.Title);
        if (!string.IsNullOrEmpty(info.Series)) writer.WriteElementString("Series", info.Series);
        if (!string.IsNullOrEmpty(info.Number)) writer.WriteElementString("Number", info.Number);
        if (info.Count.HasValue) writer.WriteElementString("Count", info.Count.Value.ToString());
        if (info.Volume.HasValue) writer.WriteElementString("Volume", info.Volume.Value.ToString());
        if (!string.IsNullOrEmpty(info.AlternateSeries)) writer.WriteElementString("AlternateSeries", info.AlternateSeries);
        if (!string.IsNullOrEmpty(info.AlternateNumber)) writer.WriteElementString("AlternateNumber", info.AlternateNumber);
        if (info.AlternateCount.HasValue) writer.WriteElementString("AlternateCount", info.AlternateCount.Value.ToString());
        if (!string.IsNullOrEmpty(info.Summary)) writer.WriteElementString("Summary", info.Summary);
        if (!string.IsNullOrEmpty(info.Notes)) writer.WriteElementString("Notes", info.Notes);
        if (info.Year.HasValue) writer.WriteElementString("Year", info.Year.Value.ToString());
        if (info.Month.HasValue) writer.WriteElementString("Month", info.Month.Value.ToString());
        if (info.Day.HasValue) writer.WriteElementString("Day", info.Day.Value.ToString());
        if (!string.IsNullOrEmpty(info.Writer)) writer.WriteElementString("Writer", info.Writer);
        if (!string.IsNullOrEmpty(info.Penciller)) writer.WriteElementString("Penciller", info.Penciller);
        if (!string.IsNullOrEmpty(info.Inker)) writer.WriteElementString("Inker", info.Inker);
        if (!string.IsNullOrEmpty(info.Colorist)) writer.WriteElementString("Colorist", info.Colorist);
        if (!string.IsNullOrEmpty(info.Letterer)) writer.WriteElementString("Letterer", info.Letterer);
        if (!string.IsNullOrEmpty(info.CoverArtist)) writer.WriteElementString("CoverArtist", info.CoverArtist);
        if (!string.IsNullOrEmpty(info.Editor)) writer.WriteElementString("Editor", info.Editor);
        if (!string.IsNullOrEmpty(info.Publisher)) writer.WriteElementString("Publisher", info.Publisher);
        if (!string.IsNullOrEmpty(info.Imprint)) writer.WriteElementString("Imprint", info.Imprint);
        if (!string.IsNullOrEmpty(info.Genre)) writer.WriteElementString("Genre", info.Genre);
        if (!string.IsNullOrEmpty(info.Tags)) writer.WriteElementString("Tags", info.Tags);
        if (!string.IsNullOrEmpty(info.Web)) writer.WriteElementString("Web", info.Web);
        if (info.PageCount.HasValue) writer.WriteElementString("PageCount", info.PageCount.Value.ToString());
        if (!string.IsNullOrEmpty(info.LanguageISO)) writer.WriteElementString("LanguageISO", info.LanguageISO);
        if (!string.IsNullOrEmpty(info.Format)) writer.WriteElementString("Format", info.Format);
        if (info.BlackAndWhite.HasValue) writer.WriteElementString("BlackAndWhite", info.BlackAndWhite.Value.ToString());
        if (info.Manga.HasValue) writer.WriteElementString("Manga", info.Manga.Value.ToString());
        if (!string.IsNullOrEmpty(info.Characters)) writer.WriteElementString("Characters", info.Characters);
        if (!string.IsNullOrEmpty(info.Teams)) writer.WriteElementString("Teams", info.Teams);
        if (!string.IsNullOrEmpty(info.Locations)) writer.WriteElementString("Locations", info.Locations);
        if (!string.IsNullOrEmpty(info.StoryArc)) writer.WriteElementString("StoryArc", info.StoryArc);
        if (!string.IsNullOrEmpty(info.StoryArcNumber)) writer.WriteElementString("StoryArcNumber", info.StoryArcNumber);
        if (!string.IsNullOrEmpty(info.SeriesGroup)) writer.WriteElementString("SeriesGroup", info.SeriesGroup);
        if (info.AgeRating.HasValue) writer.WriteElementString("AgeRating", GetAgeRatingString(info.AgeRating.Value));
        if (info.CommunityRating.HasValue) writer.WriteElementString("CommunityRating", info.CommunityRating.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(info.ScanInformation)) writer.WriteElementString("ScanInformation", info.ScanInformation);

        if (info.Pages is { Count: > 0 })
        {
            writer.WriteStartElement("Pages");
            foreach (var page in info.Pages)
            {
                writer.WriteStartElement("Page");
                writer.WriteAttributeString("Image", page.Image.ToString());
                if (!string.IsNullOrEmpty(page.TypeString)) writer.WriteAttributeString("Type", page.TypeString);
                if (page.DoublePage) writer.WriteAttributeString("DoublePage", "Yes");
                if (page.ImageWidth > 0) writer.WriteAttributeString("ImageWidth", page.ImageWidth.ToString());
                if (page.ImageHeight > 0) writer.WriteAttributeString("ImageHeight", page.ImageHeight.ToString());
                if (page.ImageSize > 0) writer.WriteAttributeString("ImageSize", page.ImageSize.ToString());
                if (!string.IsNullOrEmpty(page.Bookmark)) writer.WriteAttributeString("Bookmark", page.Bookmark);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string GetAgeRatingString(AgeRating rating) => rating switch
    {
        AgeRating.AdultsOnly18Plus => "Adults Only 18+",
        AgeRating.EarlyChildhood => "Early Childhood",
        AgeRating.Everyone10Plus => "Everyone 10+",
        AgeRating.KidsToAdults => "Kids to Adults",
        AgeRating.Mature17Plus => "Mature 17+",
        AgeRating.RatingPending => "Rating Pending",
        _ => rating.ToString()
    };

    internal static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string GetKomgaDownloadExtension(KomgaBook book)
    {
        var baseMediaType = book.Media?.MediaType?.Split(';')[0].Trim();
        return baseMediaType switch
        {
            "application/zip" => ".cbz",
            "application/x-rar-compressed" => ".cbr",
            "application/x-7z-compressed" => ".cb7",
            "application/x-tar" => ".cbt",
            "application/pdf" => ".pdf",
            "application/epub+zip" => ".epub",
            _ => ".cbz"
        };
    }

    private static bool RequiresKomgaConversion(string filePath, ComicFormat format)
    {
        return format == ComicFormat.Pdf ||
               format == ComicFormat.Epub ||
               (format == ComicFormat.Cbr && ComicConverterService.IsSolidRar(filePath));
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
        bool shouldRaise;
        lock (_libraryChangedLock)
        {
            if (_deferredLibraryChangedCount > 0)
            {
                _libraryChangedPending = true;
                return;
            }

            shouldRaise = true;
        }

        if (shouldRaise)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EndDeferredLibraryChanged()
    {
        bool shouldRaise = false;
        lock (_libraryChangedLock)
        {
            if (_deferredLibraryChangedCount == 0)
            {
                return;
            }

            _deferredLibraryChangedCount--;
            if (_deferredLibraryChangedCount == 0 && _libraryChangedPending)
            {
                _libraryChangedPending = false;
                shouldRaise = true;
            }
        }

        if (shouldRaise)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class DeferredLibraryChangedScope : IDisposable
    {
        private LibraryService? _owner;

        public DeferredLibraryChangedScope(LibraryService owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndDeferredLibraryChanged();
        }
    }
}
