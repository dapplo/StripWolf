using System.IO.Compression;
using StripWolf.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Tar;

namespace StripWolf.Services;

/// <summary>
/// Service for reading comic book archives after import/conversion.
/// </summary>
public class ComicReaderService
{
    private readonly Dictionary<(string, int), byte[]> _pageCache = new();
    private readonly Dictionary<string, List<string>> _pageNamesCache = new();
    private readonly object _cacheLock = new();

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
            ".epub" => ComicFormat.Epub,
            _ => ComicFormat.Unknown
        };
    }

    /// <summary>
    /// Gets information about a comic file without extracting all pages
    /// </summary>
    public async Task<(int pageCount, long fileSize)> GetComicInfoAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Comic file not found", filePath);
        }

        var pageNames = await GetCachedPageNamesAsync(filePath);
        return (pageNames.Count, fileInfo.Length);
    }

    /// <summary>
    /// Gets information about a comic file without populating reader caches.
    /// Useful for import/scan workflows that should not retain archive metadata in memory.
    /// </summary>
    public async Task<(int pageCount, long fileSize)> GetComicInfoWithoutCacheAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Comic file not found", filePath);
        }

        var pageNames = await GetPageNamesWithoutCacheAsync(filePath);
        return (pageNames.Count, fileInfo.Length);
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
        var format = GetComicFormat(filePath);
        return format switch
        {
            ComicFormat.Cbz => await BuildCbzPageNamesAsync(filePath),
            ComicFormat.Cbr => await BuildCbrPageNamesAsync(filePath),
            ComicFormat.Cb7 => await BuildCb7PageNamesAsync(filePath),
            ComicFormat.Cbt => await BuildCbtPageNamesAsync(filePath),
            _ => throw new NotSupportedException($"Unsupported comic format: {format}")
        };
    }

    /// <summary>
    /// Returns the cached sorted page-name list, building and caching it on first access
    /// </summary>
    private async Task<List<string>> GetCachedPageNamesAsync(string filePath)
    {
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
        lock (_cacheLock)
        {
            if (_pageCache.TryGetValue((filePath, pageIndex), out var cached))
            {
                return cached;
            }
        }

        var sortedNames = await GetCachedPageNamesAsync(filePath);

        var data = await ReadPageAsync(filePath, pageIndex, sortedNames);

        lock (_cacheLock)
        {
            _pageCache[(filePath, pageIndex)] = data;
        }

        return data;
    }

    /// <summary>
    /// Extracts a specific page from the comic without populating the page cache.
    /// Useful for one-off work such as thumbnail generation during import.
    /// </summary>
    public async Task<byte[]> GetPageWithoutCacheAsync(string filePath, int pageIndex)
    {
        lock (_cacheLock)
        {
            if (_pageCache.TryGetValue((filePath, pageIndex), out var cached))
            {
                return cached;
            }
        }

        var sortedNames = await GetPageNamesWithoutCacheAsync(filePath);
        return await ReadPageAsync(filePath, pageIndex, sortedNames);
    }

    /// <summary>
    /// Copies a specific page to the supplied stream without populating the page cache.
    /// </summary>
    public async Task CopyPageWithoutCacheAsync(string filePath, int pageIndex, Stream outputStream)
    {
        var sortedNames = await GetPageNamesWithoutCacheAsync(filePath);
        await CopyPageAsync(filePath, pageIndex, sortedNames, outputStream);
    }

    private static async Task<byte[]> ReadPageAsync(string filePath, int pageIndex, List<string> sortedNames)
    {
        if (pageIndex < 0 || pageIndex >= sortedNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
        }

        var entryName = sortedNames[pageIndex];
        var format = GetComicFormat(filePath);

        return format switch
        {
            ComicFormat.Cbz => await ReadCbzPageAsync(filePath, entryName),
            ComicFormat.Cbr => await ReadCbrPageAsync(filePath, pageIndex, entryName, sortedNames),
            ComicFormat.Cb7 => await ReadCb7PageAsync(filePath, entryName),
            ComicFormat.Cbt => await ReadCbtPageAsync(filePath, entryName),
            _ => throw new NotSupportedException($"Unsupported comic format: {format}")
        };
    }

    private static async Task CopyPageAsync(string filePath, int pageIndex, List<string> sortedNames, Stream outputStream)
    {
        if (pageIndex < 0 || pageIndex >= sortedNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
        }

        var entryName = sortedNames[pageIndex];
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
            default:
                throw new NotSupportedException($"Unsupported comic format: {format}");
        }
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _pageCache.Clear();
            _pageNamesCache.Clear();
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

    #region CBZ (ZIP) Operations

    private static async Task<List<string>> BuildCbzPageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.Entries
                .Where(e => IsImageFile(e.FullName))
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
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
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
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
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
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
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
}

/// <summary>
/// Comparer for comic page file paths.
/// Sorts directories first, then files within each directory.
/// Uses natural string ordering for proper numeric sorting.
/// </summary>
internal sealed class ComicPageComparer : IComparer<string>
{
    public static readonly ComicPageComparer Instance = new();

    private ComicPageComparer() { }

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        // Normalize path separators
        x = x.Replace('\\', '/');
        y = y.Replace('\\', '/');

        var xParts = x.Split('/');
        var yParts = y.Split('/');

        // Compare path components
        var minParts = Math.Min(xParts.Length, yParts.Length);
        
        for (var i = 0; i < minParts; i++)
        {
            var isLastX = i == xParts.Length - 1;
            var isLastY = i == yParts.Length - 1;
            
            // If one is a directory component and the other is a file, directory goes first
            if (!isLastX && isLastY)
            {
                // x has more path components (is in a subdirectory), compare at this level
                var cmp = NaturalCompare(xParts[i], yParts[i]);
                if (cmp != 0) return cmp;
                // If same prefix, directory path sorts before file at same level
                return -1;
            }
            if (isLastX && !isLastY)
            {
                var cmp = NaturalCompare(xParts[i], yParts[i]);
                if (cmp != 0) return cmp;
                // If same prefix, file sorts after directory at same level
                return 1;
            }

            // Both are at the same depth level, compare naturally
            var result = NaturalCompare(xParts[i], yParts[i]);
            if (result != 0)
            {
                return result;
            }
        }

        // If all compared parts are equal, shorter path comes first
        return xParts.Length.CompareTo(yParts.Length);
    }

    /// <summary>
    /// Natural string comparison that handles numbers correctly.
    /// "page2" comes before "page10".
    /// </summary>
    private static int NaturalCompare(string x, string y)
    {
        var xi = 0;
        var yi = 0;

        while (xi < x.Length && yi < y.Length)
        {
            var xc = x[xi];
            var yc = y[yi];

            // If both are digits, compare as numbers
            if (char.IsDigit(xc) && char.IsDigit(yc))
            {
                // Extract the full number from both strings
                var xNumStart = xi;
                while (xi < x.Length && char.IsDigit(x[xi])) xi++;
                if (!long.TryParse(x.AsSpan(xNumStart, xi - xNumStart), out var xNum))
                {
                    // Fallback to string comparison if number is too large
                    return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                }

                var yNumStart = yi;
                while (yi < y.Length && char.IsDigit(y[yi])) yi++;
                if (!long.TryParse(y.AsSpan(yNumStart, yi - yNumStart), out var yNum))
                {
                    // Fallback to string comparison if number is too large
                    return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                }

                var numCmp = xNum.CompareTo(yNum);
                if (numCmp != 0) return numCmp;
            }
            else
            {
                // Compare as characters (case-insensitive)
                var charCmp = char.ToLowerInvariant(xc).CompareTo(char.ToLowerInvariant(yc));
                if (charCmp != 0) return charCmp;
                xi++;
                yi++;
            }
        }

        // If we've exhausted one string, the shorter one comes first
        return (x.Length - xi).CompareTo(y.Length - yi);
    }
}
