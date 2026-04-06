using System.IO.Compression;
using Kom2go.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Tar;

namespace Kom2go.Services;

/// <summary>
/// Service for reading comic book archives (CBZ, CBR, CB7, and CBT files)
/// </summary>
public class ComicReaderService
{
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
            _ => ComicFormat.Unknown
        };
    }

    /// <summary>
    /// Gets information about a comic file without extracting all pages
    /// </summary>
    public async Task<(int pageCount, long fileSize)> GetComicInfoAsync(string filePath)
    {
        var format = GetComicFormat(filePath);
        var fileInfo = new FileInfo(filePath);
        
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Comic file not found", filePath);
        }

        int pageCount = format switch
        {
            ComicFormat.Cbz => await GetCbzPageCountAsync(filePath),
            ComicFormat.Cbr => await GetCbrPageCountAsync(filePath),
            ComicFormat.Cb7 => await GetCb7PageCountAsync(filePath),
            ComicFormat.Cbt => await GetCbtPageCountAsync(filePath),
            _ => throw new NotSupportedException($"Unsupported comic format: {format}")
        };

        return (pageCount, fileInfo.Length);
    }

    /// <summary>
    /// Gets all page file names from a comic archive
    /// </summary>
    public async Task<List<string>> GetPageNamesAsync(string filePath)
    {
        var format = GetComicFormat(filePath);
        
        return format switch
        {
            ComicFormat.Cbz => await GetCbzPageNamesAsync(filePath),
            ComicFormat.Cbr => await GetCbrPageNamesAsync(filePath),
            ComicFormat.Cb7 => await GetCb7PageNamesAsync(filePath),
            ComicFormat.Cbt => await GetCbtPageNamesAsync(filePath),
            _ => throw new NotSupportedException($"Unsupported comic format: {format}")
        };
    }

    /// <summary>
    /// Extracts a specific page from the comic as a byte array
    /// </summary>
    public async Task<byte[]> GetPageAsync(string filePath, int pageIndex)
    {
        var format = GetComicFormat(filePath);
        
        return format switch
        {
            ComicFormat.Cbz => await GetCbzPageAsync(filePath, pageIndex),
            ComicFormat.Cbr => await GetCbrPageAsync(filePath, pageIndex),
            ComicFormat.Cb7 => await GetCb7PageAsync(filePath, pageIndex),
            ComicFormat.Cbt => await GetCbtPageAsync(filePath, pageIndex),
            _ => throw new NotSupportedException($"Unsupported comic format: {format}")
        };
    }

    /// <summary>
    /// Extracts the cover image (first page) and saves it to the specified path
    /// </summary>
    public async Task<string> ExtractCoverAsync(string filePath, string outputPath)
    {
        var coverData = await GetPageAsync(filePath, 0);
        var pageNames = await GetPageNamesAsync(filePath);
        
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

    private static async Task<int> GetCbzPageCountAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(filePath);
            return archive.Entries
                .Count(e => IsImageFile(e.FullName));
        });
    }

    private static async Task<List<string>> GetCbzPageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(filePath);
            return archive.Entries
                .Where(e => IsImageFile(e.FullName))
                .Select(e => e.FullName)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();
        });
    }

    private static async Task<byte[]> GetCbzPageAsync(string filePath, int pageIndex)
    {
        return await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(filePath);
            var sortedNames = archive.Entries
                .Where(e => IsImageFile(e.FullName))
                .Select(e => e.FullName)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();

            if (pageIndex < 0 || pageIndex >= sortedNames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
            }

            var entry = archive.GetEntry(sortedNames[pageIndex]);
            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find page {pageIndex} in archive");
            }
            
            using var stream = entry.Open();
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        });
    }

    #endregion

    #region CBR (RAR) Operations

    private static async Task<int> GetCbrPageCountAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = RarArchive.OpenArchive(filePath);
            return archive.Entries
                .Count(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty));
        });
    }

    private static async Task<List<string>> GetCbrPageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = RarArchive.OpenArchive(filePath);
            return archive.Entries
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();
        });
    }

    private static async Task<byte[]> GetCbrPageAsync(string filePath, int pageIndex)
    {
        return await Task.Run(() =>
        {
            using var archive = RarArchive.OpenArchive(filePath);
            
            // Check if this is a solid archive
            if (archive.IsSolid)
            {
                // For solid archives, we must read sequentially
                return GetPageFromSolidRar(archive, pageIndex);
            }
            
            var sortedNames = archive.Entries
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();

            if (pageIndex < 0 || pageIndex >= sortedNames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
            }

            var targetName = sortedNames[pageIndex];
            var entry = archive.Entries.FirstOrDefault(e => e.Key == targetName);
            
            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find page {pageIndex} in archive");
            }
            
            // Handle entries that may not be extractable
            using var stream = entry.OpenEntryStream();
            if (stream is null)
            {
                throw new InvalidOperationException($"Could not extract page {pageIndex} from CBR archive. The entry may be corrupted or use an unsupported compression method.");
            }
            
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        });
    }

    private static byte[] GetPageFromSolidRar(IRarArchive archive, int pageIndex)
    {
        // Build sorted list of image entry names
        var sortedNames = archive.Entries
            .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
            .Select(e => e.Key ?? string.Empty)
            .OrderBy(name => name, ComicPageComparer.Instance)
            .ToList();

        if (pageIndex < 0 || pageIndex >= sortedNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
        }

        var targetName = sortedNames[pageIndex];
        
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

    #endregion

    #region CB7 (7-Zip) Operations

    private static async Task<int> GetCb7PageCountAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = SevenZipArchive.OpenArchive(filePath);
            return archive.Entries
                .Count(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty));
        });
    }

    private static async Task<List<string>> GetCb7PageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = SevenZipArchive.OpenArchive(filePath);
            return archive.Entries
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();
        });
    }

    private static async Task<byte[]> GetCb7PageAsync(string filePath, int pageIndex)
    {
        return await Task.Run(() =>
        {
            using var archive = SevenZipArchive.OpenArchive(filePath);
            var sortedNames = archive.Entries
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();

            if (pageIndex < 0 || pageIndex >= sortedNames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
            }

            var targetName = sortedNames[pageIndex];
            
            // 7z archives may be solid, so we use ExtractAllEntries
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

            throw new InvalidOperationException($"Could not find page {pageIndex} in CB7 archive");
        });
    }

    #endregion

    #region CBT (TAR) Operations

    private static async Task<int> GetCbtPageCountAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = TarArchive.OpenArchive(filePath);
            return archive.Entries
                .Count(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty));
        });
    }

    private static async Task<List<string>> GetCbtPageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = TarArchive.OpenArchive(filePath);
            return archive.Entries
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();
        });
    }

    private static async Task<byte[]> GetCbtPageAsync(string filePath, int pageIndex)
    {
        return await Task.Run(() =>
        {
            using var archive = TarArchive.OpenArchive(filePath);
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
                .ToList();
            
            var sortedNames = entries
                .Select(e => e.Key ?? string.Empty)
                .OrderBy(name => name, ComicPageComparer.Instance)
                .ToList();

            if (pageIndex < 0 || pageIndex >= sortedNames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
            }

            var targetName = sortedNames[pageIndex];
            var entry = entries.FirstOrDefault(e => e.Key == targetName);
            
            if (entry is null)
            {
                throw new InvalidOperationException($"Could not find page {pageIndex} in CBT archive");
            }
            
            using var stream = entry.OpenEntryStream();
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
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
