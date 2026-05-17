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

using System.IO.Compression;
using StripWolf.Models;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Tar;

namespace StripWolf.Services;

/// <summary>
/// Service for reading comic book archives after import/conversion.
/// </summary>
public class ComicReaderService
{
    private readonly IPdfRenderer _pdfRenderer;
    private readonly EpubToCbzConverterService _epubConverter;
    private readonly Dictionary<(string, int), PageCacheEntry> _pageCache = new();
    private readonly LinkedList<(string, int)> _pageCacheLru = new();
    private readonly Dictionary<string, List<string>> _pageNamesCache = new();
    private readonly Dictionary<string, Task<PdfReaderSession>> _pdfSessions = new();
    private readonly Dictionary<string, Task<EpubToCbzConverterService.EpubReaderSession>> _epubSessions = new();
    private readonly object _cacheLock = new();
    private const int MaxCachedPageEntries = 16; // Increased from 8
    private const long MaxCachedPageBytes = 64L * 1024 * 1024; // Increased from 24MB
    private long _cachedPageBytes;

    public ComicReaderService(IPdfRenderer pdfRenderer, EpubToCbzConverterService epubConverter)
    {
        _pdfRenderer = pdfRenderer;
        _epubConverter = epubConverter;
    }

    /// <summary>
    /// Gets the format of a comic file based on its extension
    /// </summary>
    public static ComicFormat GetComicFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cbz" => ComicFormat.Cbz,
            ".cbr" => ComicFormat.Cbr,
            ".cb7" => ComicFormat.Cb7,
            ".cbt" => ComicFormat.Cbt,
            ".pdf" => ComicFormat.Pdf,
#if !DISABLE_EPUB_SUPPORT
            ".epub" => ComicFormat.Epub,
#endif
            _ => ComicFormat.Unknown
        };
    }

    /// <summary>
    /// Gets information about a comic file without extracting all pages
    /// </summary>
    public async Task<(int pageCount, long fileSize)> GetComicInfoAsync(string filePath)
    {
        if (Directory.Exists(filePath))
        {
            var pageNames = await GetCachedPageNamesAsync(filePath);
            return (pageNames.Count, 0);
        }

        var fileInfo = new FileInfo(filePath);
        
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Comic file not found", filePath);
        }

        var format = GetComicFormat(filePath);
        return format switch
        {
            ComicFormat.Pdf => await GetPdfComicInfoAsync(filePath),
            ComicFormat.Epub => await GetEpubComicInfoAsync(filePath),
            _ => ((await GetCachedPageNamesAsync(filePath)).Count, fileInfo.Length)
        };
    }

    /// <summary>
    /// Gets information about a comic file without populating reader caches.
    /// Useful for import/scan workflows that should not retain archive metadata in memory.
    /// </summary>
    public async Task<(int pageCount, long fileSize)> GetComicInfoWithoutCacheAsync(string filePath)
    {
        if (Directory.Exists(filePath))
        {
            var pageNames = await GetPageNamesWithoutCacheAsync(filePath);
            return (pageNames.Count, 0);
        }

        var fileInfo = new FileInfo(filePath);

        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Comic file not found", filePath);
        }

        var format = GetComicFormat(filePath);
        return format switch
        {
            ComicFormat.Pdf => (GetPdfPageCount(filePath), fileInfo.Length),
            ComicFormat.Epub => await GetEpubComicInfoWithoutCacheAsync(filePath),
            _ => ((await GetPageNamesWithoutCacheAsync(filePath)).Count, fileInfo.Length)
        };
    }

    /// <summary>
    /// Gets all page file names from a comic archive (cached after the first call)
    /// </summary>
    public async Task<List<string>> GetPageNamesAsync(string filePath)
    {
        return await GetCachedPageNamesAsync(filePath);
    }

    /// <summary>
    /// Gets all page file names from a comic archive without populating reader caches.
    /// </summary>
    public async Task<List<string>> GetPageNamesWithoutCacheAsync(string filePath)
    {
        if (Directory.Exists(filePath))
        {
            return await BuildDirectoryPageNamesAsync(filePath);
        }

        var format = GetComicFormat(filePath);
        return format switch
        {
            ComicFormat.Cbz => await BuildCbzPageNamesAsync(filePath),
            ComicFormat.Cbr => await BuildCbrPageNamesAsync(filePath),
            ComicFormat.Cb7 => await BuildCb7PageNamesAsync(filePath),
            ComicFormat.Cbt => await BuildCbtPageNamesAsync(filePath),
            ComicFormat.Pdf => BuildSyntheticPageNames(GetPdfPageCount(filePath), ".jpg"),
            ComicFormat.Epub => await GetEpubPageNamesWithoutCacheAsync(filePath),
            _ => throw new NotSupportedException($"Unsupported comic format: {format}")
        };
    }

    /// <summary>
    /// Returns the cached sorted page-name list, building and caching it on first access
    /// </summary>
    private async Task<List<string>> GetCachedPageNamesAsync(string filePath)
    {
        if (Directory.Exists(filePath))
        {
            return await BuildDirectoryPageNamesAsync(filePath);
        }

        lock (_cacheLock)
        {
            if (_pageNamesCache.TryGetValue(filePath, out var cached))
            {
                return cached;
            }
        }

        var format = GetComicFormat(filePath);
        var names = format switch
        {
            ComicFormat.Cbz => await BuildCbzPageNamesAsync(filePath),
            ComicFormat.Cbr => await BuildCbrPageNamesAsync(filePath),
            ComicFormat.Cb7 => await BuildCb7PageNamesAsync(filePath),
            ComicFormat.Cbt => await BuildCbtPageNamesAsync(filePath),
            ComicFormat.Pdf => BuildSyntheticPageNames((await GetOrCreatePdfSessionAsync(filePath)).PageCount, ".jpg"),
            ComicFormat.Epub => BuildSyntheticPageNames((await GetOrCreateEpubSessionAsync(filePath)).PageCount, ".png"),
            _ => throw new NotSupportedException($"Unsupported comic format: {format}")
        };

        lock (_cacheLock)
        {
            // Another thread may have populated the cache while we were building; prefer theirs
            if (!_pageNamesCache.TryGetValue(filePath, out var existing))
            {
                _pageNamesCache[filePath] = names;
                existing = names;
            }
            return existing;
        }
    }

    /// <summary>
    /// Extracts a specific page from the comic as a byte array
    /// </summary>
    public async Task<byte[]> GetPageAsync(string filePath, int pageIndex)
    {
        if (TryGetCachedPageData(filePath, pageIndex, out var cached))
        {
            return cached;
        }

        var sortedNames = await GetCachedPageNamesAsync(filePath);
        var data = await ReadPageAsync(filePath, pageIndex, sortedNames);
        CachePageData(filePath, pageIndex, data);

        return data;
    }

    /// <summary>
    /// Extracts a specific page from the comic without populating the page cache.
    /// Useful for one-off work such as thumbnail generation during import.
    /// </summary>
    public async Task<byte[]> GetPageWithoutCacheAsync(string filePath, int pageIndex)
    {
        if (TryGetCachedPageData(filePath, pageIndex, out var cached))
        {
            return cached;
        }

        var sortedNames = await GetPageNamesWithoutCacheAsync(filePath);
        return await ReadPageAsync(filePath, pageIndex, sortedNames);
    }

    /// <summary>
    /// Copies a specific page to the supplied stream, using cached page bytes when available.
    /// </summary>
    public async Task CopyPageAsync(string filePath, int pageIndex, Stream outputStream)
    {
        if (TryGetCachedPageData(filePath, pageIndex, out var cached))
        {
            await outputStream.WriteAsync(cached, 0, cached.Length);
            return;
        }

        var sortedNames = await GetCachedPageNamesAsync(filePath);
        await CopyPageAsync(filePath, pageIndex, sortedNames, outputStream);
    }

    /// <summary>
    /// Copies a specific page to the supplied stream without populating the page cache.
    /// </summary>
    public async Task CopyPageWithoutCacheAsync(string filePath, int pageIndex, Stream outputStream)
    {
        var sortedNames = await GetPageNamesWithoutCacheAsync(filePath);
        await CopyPageAsync(filePath, pageIndex, sortedNames, outputStream);
    }

    private async Task<byte[]> ReadPageAsync(string filePath, int pageIndex, List<string> sortedNames)
    {
        using var stream = RecyclableStreamManagerProvider.Manager.GetStream(nameof(ComicReaderService));
        await CopyPageAsync(filePath, pageIndex, sortedNames, stream);
        return stream.ToArray();
    }

    private async Task CopyPageAsync(string filePath, int pageIndex, List<string> sortedNames, Stream outputStream)
    {
        if (pageIndex < 0 || pageIndex >= sortedNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
        }

        var entryName = sortedNames[pageIndex];
        if (Directory.Exists(filePath))
        {
            await CopyDirectoryPageAsync(filePath, entryName, outputStream);
            return;
        }

        var format = GetComicFormat(filePath);

        switch (format)
        {
            case ComicFormat.Cbz:
                await CopyCbzPageAsync(filePath, entryName, outputStream);
                break;
            case ComicFormat.Cbr:
                await CopyCbrPageAsync(filePath, pageIndex, entryName, sortedNames, outputStream);
                break;
            case ComicFormat.Cb7:
                await CopyCb7PageAsync(filePath, entryName, outputStream);
                break;
            case ComicFormat.Cbt:
                await CopyCbtPageAsync(filePath, entryName, outputStream);
                break;
            case ComicFormat.Pdf:
                await CopyPdfPageAsync(filePath, pageIndex, outputStream);
                break;
            case ComicFormat.Epub:
                await CopyEpubPageAsync(filePath, pageIndex, outputStream);
                break;
            default:
                throw new NotSupportedException($"Unsupported comic format: {format}");
        }
    }

    public void ClearCache()
    {
        Task<PdfReaderSession>[] pdfSessions;
        Task<EpubToCbzConverterService.EpubReaderSession>[] epubSessions;

        lock (_cacheLock)
        {
            _pageCache.Clear();
            _pageCacheLru.Clear();
            _cachedPageBytes = 0;
            _pageNamesCache.Clear();
            pdfSessions = _pdfSessions.Values.ToArray();
            epubSessions = _epubSessions.Values.ToArray();
            _pdfSessions.Clear();
            _epubSessions.Clear();
        }

        foreach (var pdfSession in pdfSessions.Where(task => task.IsCompletedSuccessfully))
        {
            pdfSession.Result.Dispose();
        }

        foreach (var epubSession in epubSessions.Where(task => task.IsCompletedSuccessfully))
        {
            epubSession.Result.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Extracts the cover image (first page) and saves it to the specified path
    /// </summary>
    public async Task<string> ExtractCoverAsync(string filePath, string outputPath)
    {
        var coverData = await GetPageWithoutCacheAsync(filePath, 0);
        var pageNames = await GetPageNamesWithoutCacheAsync(filePath);
        
        if (pageNames.Count == 0)
        {
            throw new InvalidOperationException("Comic has no pages");
        }

        var extension = Path.GetExtension(pageNames[0]);
        var coverPath = Path.Combine(outputPath, $"cover{extension}");
        
        await File.WriteAllBytesAsync(coverPath, coverData);
        
        return coverPath;
    }

    private async Task<(int pageCount, long fileSize)> GetPdfComicInfoAsync(string filePath)
    {
        var session = await GetOrCreatePdfSessionAsync(filePath);
        return (session.PageCount, session.FileSize);
    }

    private async Task<(int pageCount, long fileSize)> GetEpubComicInfoAsync(string filePath)
    {
        var session = await GetOrCreateEpubSessionAsync(filePath);
        return (session.PageCount, session.FileSize);
    }

    private async Task<(int pageCount, long fileSize)> GetEpubComicInfoWithoutCacheAsync(string filePath)
    {
        await using var session = await _epubConverter.CreateReaderSessionAsync(filePath);
        return (session.PageCount, session.FileSize);
    }

    private async Task<List<string>> GetEpubPageNamesWithoutCacheAsync(string filePath)
    {
        await using var session = await _epubConverter.CreateReaderSessionAsync(filePath);
        return BuildSyntheticPageNames(session.PageCount, ".png");
    }

    private bool TryGetCachedPageData(string filePath, int pageIndex, out byte[] data)
    {
        lock (_cacheLock)
        {
            if (_pageCache.TryGetValue((filePath, pageIndex), out var cached))
            {
                MovePageCacheEntryToFront(cached.Node);
                data = cached.Data;
                return true;
            }
        }

        data = [];
        return false;
    }

    private void CachePageData(string filePath, int pageIndex, byte[] data)
    {
        if (!ShouldCachePageData(filePath, data.Length))
        {
            return;
        }

        lock (_cacheLock)
        {
            if (_pageCache.TryGetValue((filePath, pageIndex), out var existing))
            {
                _cachedPageBytes -= existing.Data.Length;
                existing.Data = data;
                _cachedPageBytes += data.Length;
                MovePageCacheEntryToFront(existing.Node);
            }
            else
            {
                var key = (filePath, pageIndex);
                var node = _pageCacheLru.AddFirst(key);
                _pageCache[key] = new PageCacheEntry(data, node);
                _cachedPageBytes += data.Length;
            }

            TrimPageCache();
        }
    }

    private static bool ShouldCachePageData(string filePath, int dataLength)
    {
        if (dataLength > MaxCachedPageBytes / 2)
        {
            return false;
        }

        var format = GetComicFormat(filePath);
        if (OperatingSystem.IsWindows() && (format == ComicFormat.Pdf || format == ComicFormat.Epub))
        {
            return false;
        }

        return true;
    }

    private void TrimPageCache()
    {
        while (_pageCache.Count > MaxCachedPageEntries || _cachedPageBytes > MaxCachedPageBytes)
        {
            var oldestNode = _pageCacheLru.Last;
            if (oldestNode is null)
            {
                break;
            }

            var key = oldestNode.Value;
            if (_pageCache.Remove(key, out var entry))
            {
                _cachedPageBytes -= entry.Data.Length;
            }

            _pageCacheLru.Remove(oldestNode);
        }
    }

    private void MovePageCacheEntryToFront(LinkedListNode<(string, int)> node)
    {
        if (!ReferenceEquals(_pageCacheLru.First, node))
        {
            _pageCacheLru.Remove(node);
            _pageCacheLru.AddFirst(node);
        }
    }

    private int GetPdfPageCount(string filePath)
    {
        return _pdfRenderer.GetPageCount(filePath);
    }

    private static List<string> BuildSyntheticPageNames(int pageCount, string extension)
    {
        return Enumerable.Range(1, pageCount)
            .Select(index => $"Page_{index:D5}{extension}")
            .ToList();
    }

    private static Task<List<string>> BuildDirectoryPageNamesAsync(string filePath)
    {
        return Task.FromResult(Directory.EnumerateFiles(filePath, "Page_*.*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(static name => name, ComicPageComparer.Instance)
            .ToList());
    }

    private static Task<byte[]> ReadDirectoryPageAsync(string directoryPath, string entryName)
    {
        return File.ReadAllBytesAsync(Path.Combine(directoryPath, entryName));
    }

    private static async Task CopyDirectoryPageAsync(string directoryPath, string entryName, Stream outputStream)
    {
        await using var inputStream = File.OpenRead(Path.Combine(directoryPath, entryName));
        await inputStream.CopyToAsync(outputStream);
    }

    private Task<PdfReaderSession> GetOrCreatePdfSessionAsync(string filePath)
    {
        lock (_cacheLock)
        {
            if (_pdfSessions.TryGetValue(filePath, out var existing))
            {
                return existing;
            }

            var created = CreatePdfSessionAsync(filePath);
            _pdfSessions[filePath] = created;
            return created;
        }
    }

    private Task<EpubToCbzConverterService.EpubReaderSession> GetOrCreateEpubSessionAsync(string filePath)
    {
        lock (_cacheLock)
        {
            if (_epubSessions.TryGetValue(filePath, out var existing))
            {
                return existing;
            }

            var created = CreateEpubSessionAsync(filePath);
            _epubSessions[filePath] = created;
            return created;
        }
    }

    private async Task<PdfReaderSession> CreatePdfSessionAsync(string filePath)
    {
        try
        {
            var session = await _pdfRenderer.CreateRenderSessionAsync(filePath);
            return new PdfReaderSession(session, new FileInfo(filePath).Length);
        }
        catch
        {
            lock (_cacheLock)
            {
                _pdfSessions.Remove(filePath);
            }

            throw;
        }
    }

    private async Task<EpubToCbzConverterService.EpubReaderSession> CreateEpubSessionAsync(string filePath)
    {
        try
        {
            return await Task.Run(() => _epubConverter.CreateReaderSessionAsync(filePath));
        }
        catch
        {
            lock (_cacheLock)
            {
                _epubSessions.Remove(filePath);
            }

            throw;
        }
    }

    private async Task<byte[]> ReadPdfPageAsync(string filePath, int pageIndex)
    {
        using var stream = new MemoryStream();
        await CopyPdfPageAsync(filePath, pageIndex, stream);
        return stream.ToArray();
    }

    private async Task CopyPdfPageAsync(string filePath, int pageIndex, Stream outputStream)
    {
        var session = await GetOrCreatePdfSessionAsync(filePath);
        await session.RenderPageAsync(pageIndex, outputStream);
    }

    private async Task<byte[]> ReadEpubPageAsync(string filePath, int pageIndex)
    {
        using var stream = new MemoryStream();
        await CopyEpubPageAsync(filePath, pageIndex, stream);
        return stream.ToArray();
    }

    private async Task CopyEpubPageAsync(string filePath, int pageIndex, Stream outputStream)
    {
        var session = await GetOrCreateEpubSessionAsync(filePath);
        await session.RenderPageToStreamAsync(pageIndex, outputStream);
    }

    private sealed class PdfReaderSession(IPdfRenderSession session, long fileSize) : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public int PageCount { get; } = session.GetPageCount();

        public long FileSize { get; } = fileSize;

        public async Task RenderPageAsync(int pageIndex, Stream outputStream)
        {
            await _gate.WaitAsync();
            try
            {
                await session.RenderPageToJpegAsync(pageIndex, outputStream);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _gate.Dispose();
            session.Dispose();
        }
    }

    #region CBZ (ZIP) Operations

    private static async Task<List<string>> BuildCbzPageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.Entries
                .Where(e => !ComicConstants.IsIgnoredImportPath(e.FullName) && IsImageFile(e.FullName))
                .Select(e => e.FullName)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();
        });
    }

    private static async Task<byte[]> ReadCbzPageAsync(string filePath, string entryName)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = archive.GetEntry(entryName);
            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find entry '{entryName}' in archive");
            }

            using var entryStream = entry.Open();
            using var memoryStream = new MemoryStream();
            entryStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        });
    }

    private static async Task CopyCbzPageAsync(string filePath, string entryName, Stream outputStream)
    {
        await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = archive.GetEntry(entryName);
            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find entry '{entryName}' in archive");
            }

            using var entryStream = entry.Open();
            entryStream.CopyTo(outputStream);
        });
    }

    #endregion

    #region CBR (RAR) Operations

    private static async Task<List<string>> BuildCbrPageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = RarArchive.OpenArchive(stream);
            return archive.Entries
                .Where(e => !e.IsDirectory &&
                    !ComicConstants.IsIgnoredImportPath(e.Key ?? string.Empty) &&
                    IsImageFile(e.Key ?? string.Empty))
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();
        });
    }

    private static async Task<byte[]> ReadCbrPageAsync(string filePath, int pageIndex, string entryName, List<string> sortedNames)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = RarArchive.OpenArchive(stream);

            // Solid archives must be read sequentially
            if (archive.IsSolid)
            {
                return ReadPageFromSolidRar(archive, pageIndex, entryName, sortedNames);
            }

            var entry = archive.Entries.FirstOrDefault(e => e.Key == entryName);
            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find entry '{entryName}' in archive");
            }

            using var entryStream = entry.OpenEntryStream();
            if (entryStream is null)
            {
                throw new InvalidOperationException($"Could not extract entry '{entryName}' from CBR archive. The entry may be corrupted or use an unsupported compression method.");
            }

            using var memoryStream = new MemoryStream();
            entryStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        });
    }

    private static async Task CopyCbrPageAsync(string filePath, int pageIndex, string entryName, List<string> sortedNames, Stream outputStream)
    {
        await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = RarArchive.OpenArchive(stream);

            if (archive.IsSolid)
            {
                CopyPageFromSolidRar(archive, pageIndex, entryName, sortedNames, outputStream);
                return;
            }

            var entry = archive.Entries.FirstOrDefault(e => e.Key == entryName);
            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find entry '{entryName}' in archive");
            }

            using var entryStream = entry.OpenEntryStream();
            if (entryStream is null)
            {
                throw new InvalidOperationException($"Could not extract entry '{entryName}' from CBR archive. The entry may be corrupted or use an unsupported compression method.");
            }

            entryStream.CopyTo(outputStream);
        });
    }

    private static byte[] ReadPageFromSolidRar(IRarArchive archive, int pageIndex, string targetName, List<string> sortedNames)
    {
        if (pageIndex < 0 || pageIndex >= sortedNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
        }

        // For solid archives, we must read through sequentially
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            if (!reader.Entry.IsDirectory && reader.Entry.Key == targetName)
            {
                using var entryStream = reader.OpenEntryStream();
                using var memoryStream = new MemoryStream();
                entryStream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        throw new InvalidOperationException($"Could not find page {pageIndex} in solid RAR archive");
    }

    private static void CopyPageFromSolidRar(IRarArchive archive, int pageIndex, string targetName, List<string> sortedNames, Stream outputStream)
    {
        if (pageIndex < 0 || pageIndex >= sortedNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
        }

        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            if (!reader.Entry.IsDirectory && reader.Entry.Key == targetName)
            {
                using var entryStream = reader.OpenEntryStream();
                entryStream.CopyTo(outputStream);
                return;
            }
        }

        throw new InvalidOperationException($"Could not find page {pageIndex} in solid RAR archive");
    }

    #endregion

    #region CB7 (7-Zip) Operations

    private static async Task<List<string>> BuildCb7PageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = SevenZipArchive.OpenArchive(stream);
            return archive.Entries
                .Where(e => !e.IsDirectory &&
                    !ComicConstants.IsIgnoredImportPath(e.Key ?? string.Empty) &&
                    IsImageFile(e.Key ?? string.Empty))
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();
        });
    }

    private static async Task<byte[]> ReadCb7PageAsync(string filePath, string entryName)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = SevenZipArchive.OpenArchive(stream);

            // 7z archives may be solid, so we use ExtractAllEntries
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                if (!reader.Entry.IsDirectory && reader.Entry.Key == entryName)
                {
                    using var entryStream = reader.OpenEntryStream();
                    using var memoryStream = new MemoryStream();
                    entryStream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }

            throw new InvalidOperationException($"Could not find entry '{entryName}' in CB7 archive");
        });
    }

    private static async Task CopyCb7PageAsync(string filePath, string entryName, Stream outputStream)
    {
        await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = SevenZipArchive.OpenArchive(stream);

            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                if (!reader.Entry.IsDirectory && reader.Entry.Key == entryName)
                {
                    using var entryStream = reader.OpenEntryStream();
                    entryStream.CopyTo(outputStream);
                    return;
                }
            }

            throw new InvalidOperationException($"Could not find entry '{entryName}' in CB7 archive");
        });
    }

    #endregion

    #region CBT (TAR) Operations

    private static async Task<List<string>> BuildCbtPageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = TarArchive.OpenArchive(stream);
            return archive.Entries
                .Where(e => !e.IsDirectory &&
                    !ComicConstants.IsIgnoredImportPath(e.Key ?? string.Empty) &&
                    IsImageFile(e.Key ?? string.Empty))
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();
        });
    }

    private static async Task<byte[]> ReadCbtPageAsync(string filePath, string entryName)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = TarArchive.OpenArchive(stream);

            var entry = archive.Entries
                .FirstOrDefault(e => !e.IsDirectory && e.Key == entryName);

            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find entry '{entryName}' in CBT archive");
            }

            using var entryStream = entry.OpenEntryStream();
            using var memoryStream = new MemoryStream();
            entryStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        });
    }

    private static async Task CopyCbtPageAsync(string filePath, string entryName, Stream outputStream)
    {
        await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = TarArchive.OpenArchive(stream);

            var entry = archive.Entries
                .FirstOrDefault(e => !e.IsDirectory && e.Key == entryName);

            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find entry '{entryName}' in CBT archive");
            }

            using var entryStream = entry.OpenEntryStream();
            entryStream.CopyTo(outputStream);
        });
    }

    #endregion

    private static bool IsImageFile(string fileName) => ComicConstants.IsImageFile(fileName);

    private sealed class PageCacheEntry(byte[] data, LinkedListNode<(string, int)> node)
    {
        public byte[] Data { get; set; } = data;

        public LinkedListNode<(string, int)> Node { get; } = node;
    }
}

