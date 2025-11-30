using System.IO.Compression;
using Kom2go.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;

namespace Kom2go.Services;

/// <summary>
/// Service for reading comic book archives (CBZ and CBR files)
/// </summary>
public class ComicReaderService
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

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
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.FullName)
                .ToList();
        });
    }

    private static async Task<byte[]> GetCbzPageAsync(string filePath, int pageIndex)
    {
        return await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(filePath);
            var entries = archive.Entries
                .Where(e => IsImageFile(e.FullName))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pageIndex < 0 || pageIndex >= entries.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
            }

            var entry = entries[pageIndex];
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
            using var archive = RarArchive.Open(filePath);
            return archive.Entries
                .Count(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty));
        });
    }

    private static async Task<List<string>> GetCbrPageNamesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var archive = RarArchive.Open(filePath);
            return archive.Entries
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
                .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.Key ?? string.Empty)
                .ToList();
        });
    }

    private static async Task<byte[]> GetCbrPageAsync(string filePath, int pageIndex)
    {
        return await Task.Run(() =>
        {
            using var archive = RarArchive.Open(filePath);
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && IsImageFile(e.Key ?? string.Empty))
                .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pageIndex < 0 || pageIndex >= entries.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");
            }

            var entry = entries[pageIndex];
            
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

    #endregion

    private static bool IsImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }
}
