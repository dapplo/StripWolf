using System.IO.Compression;
using System.Xml.Serialization;
using StripWolf.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Tar;
using SharpCompress.Readers;

namespace StripWolf.Services;

/// <summary>
/// Service for converting various comic book archive formats to CBZ format.
/// Supports CB7 (7z), CBT (TAR), CBR (RAR including solid archives).
/// </summary>
public class ComicConverterService
{
    /// <summary>
    /// Gets the underlying archive type for a comic book file
    /// </summary>
    public static ComicArchiveType GetArchiveType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cbz" => ComicArchiveType.Zip,
            ".cbr" => ComicArchiveType.Rar,
            ".cb7" => ComicArchiveType.SevenZip,
            ".cbt" => ComicArchiveType.Tar,
            ".cba" => ComicArchiveType.Ace, // ACE is not supported by SharpCompress
            _ => ComicArchiveType.Unknown
        };
    }

    /// <summary>
    /// Checks if a file format is supported for reading
    /// </summary>
    public static bool IsSupported(string filePath)
    {
        var archiveType = GetArchiveType(filePath);
        // ACE is not supported by SharpCompress
        return archiveType != ComicArchiveType.Unknown && archiveType != ComicArchiveType.Ace;
    }

    /// <summary>
    /// Checks if the file is a solid RAR archive that requires sequential reading
    /// </summary>
    public static bool IsSolidRar(string filePath)
    {
        var archiveType = GetArchiveType(filePath);
        if (archiveType != ComicArchiveType.Rar)
        {
            return false;
        }

        try
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = RarArchive.OpenArchive(stream))
            {
                return archive.IsSolid;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the file needs to be converted to CBZ for optimal reading
    /// </summary>
    public static bool NeedsConversion(string filePath)
    {
        var archiveType = GetArchiveType(filePath);
        
        // Zip/CBZ is the target format - no conversion needed
        if (archiveType == ComicArchiveType.Zip)
        {
            return false;
        }

        // Check if it's a supported format that can be converted
        if (archiveType == ComicArchiveType.SevenZip || archiveType == ComicArchiveType.Tar)
        {
            return true;
        }

        // For RAR, only solid archives need conversion (for performance)
        if (archiveType == ComicArchiveType.Rar)
        {
            return IsSolidRar(filePath);
        }

        return false;
    }

    /// <summary>
    /// Converts a comic book archive to CBZ format
    /// </summary>
    /// <param name="inputPath">Path to the source comic file</param>
    /// <param name="outputDirectory">Directory where the CBZ file will be created</param>
    /// <param name="progress">Optional progress reporter (0-1)</param>
    /// <returns>Path to the created CBZ file</returns>
    public async Task<string> ConvertToCbzAsync(
        string inputPath, 
        string outputDirectory,
        IProgress<double>? progress = null)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Comic file not found", inputPath);
        }

        var archiveType = GetArchiveType(inputPath);
        if (archiveType == ComicArchiveType.Zip)
        {
            // Already CBZ, just copy/return
            var fileName = Path.GetFileNameWithoutExtension(inputPath) + ".cbz";
            var outputPath = Path.Combine(outputDirectory, fileName);
            if (inputPath != outputPath)
            {
                File.Copy(inputPath, outputPath, true);
            }
            return outputPath;
        }

        if (archiveType == ComicArchiveType.Ace)
        {
            throw new NotSupportedException("ACE format is not supported. Please convert the file manually.");
        }

        var cbzFileName = Path.GetFileNameWithoutExtension(inputPath) + ".cbz";
        var cbzFilePath = Path.Combine(outputDirectory, cbzFileName);

        // Ensure output directory exists
        Directory.CreateDirectory(outputDirectory);

        // Create a temporary directory for extraction with a unique random name
        var tempDir = Path.Combine(Path.GetTempPath(), $"StripWolf_Convert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Extract files to temp directory
            await ExtractArchiveAsync(inputPath, archiveType, tempDir, progress);

            // Create CBZ from extracted files
            await CreateCbzFromDirectoryAsync(tempDir, cbzFilePath);

            return cbzFilePath;
        }
        finally
        {
            // Clean up temporary directory
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Temporary directory cleanup is best-effort
            }
        }
    }

    /// <summary>
    /// Extracts ComicInfo.xml from a comic archive if present
    /// </summary>
    public async Task<ComicInfo?> ExtractComicInfoAsync(string filePath)
    {
        var archiveType = GetArchiveType(filePath);
        
        try
        {
            return archiveType switch
            {
                ComicArchiveType.Zip => await ExtractComicInfoFromZipAsync(filePath),
                ComicArchiveType.Rar => await ExtractComicInfoFromRarAsync(filePath),
                ComicArchiveType.SevenZip => await ExtractComicInfoFromSevenZipAsync(filePath),
                ComicArchiveType.Tar => await ExtractComicInfoFromTarAsync(filePath),
                _ => null
            };
        }
        catch
        {
            // If extraction fails, return null
            return null;
        }
    }

    /// <summary>
    /// Parses ComicInfo XML from a byte array
    /// </summary>
    public static ComicInfo? ParseComicInfo(byte[] xmlData)
    {
        try
        {
            using var stream = new MemoryStream(xmlData);
            var serializer = new XmlSerializer(typeof(ComicInfo));
            return serializer.Deserialize(stream) as ComicInfo;
        }
        catch
        {
            return null;
        }
    }

    private async Task ExtractArchiveAsync(
        string inputPath, 
        ComicArchiveType archiveType, 
        string outputDir,
        IProgress<double>? progress)
    {
        await Task.Run(() =>
        {
            switch (archiveType)
            {
                case ComicArchiveType.Rar:
                    ExtractRar(inputPath, outputDir, progress);
                    break;
                case ComicArchiveType.SevenZip:
                    ExtractSevenZip(inputPath, outputDir, progress);
                    break;
                case ComicArchiveType.Tar:
                    ExtractTar(inputPath, outputDir, progress);
                    break;
                default:
                    throw new NotSupportedException($"Archive type {archiveType} is not supported for extraction");
            }
        });
    }

    private static void ExtractRar(string inputPath, string outputDir, IProgress<double>? progress)
    {
        // For solid RAR archives, we must use the Reader interface (forward-only stream)
        // For non-solid archives, we can use the Archive interface (random access)
        using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = RarArchive.OpenArchive(stream);
        
        if (archive.IsSolid)
        {
            // Use reader for solid archives - extract all entries sequentially
            using var reader = archive.ExtractAllEntries();
            var totalEntries = archive.Entries.Count(e => !e.IsDirectory);
            var processedEntries = 0;
            
            while (reader.MoveToNextEntry())
            {
                if (!reader.Entry.IsDirectory)
                {
                    var entryPath = GetSafeEntryPath(reader.Entry.Key ?? string.Empty, outputDir);
                    var entryDir = Path.GetDirectoryName(entryPath);
                    if (!string.IsNullOrEmpty(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    using var entryStream = reader.OpenEntryStream();
                    using var fileStream = File.Create(entryPath);
                    entryStream.CopyTo(fileStream);
                    
                    processedEntries++;
                    progress?.Report((double)processedEntries / totalEntries);
                }
            }
        }
        else
        {
            // Use archive interface for non-solid archives
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            var totalEntries = entries.Count;
            var processedEntries = 0;

            foreach (var entry in entries)
            {
                var entryPath = GetSafeEntryPath(entry.Key ?? string.Empty, outputDir);
                var entryDir = Path.GetDirectoryName(entryPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                using var entryStream = entry.OpenEntryStream();
                using var fileStream = File.Create(entryPath);
                entryStream.CopyTo(fileStream);
                
                processedEntries++;
                progress?.Report((double)processedEntries / totalEntries);
            }
        }
    }

    private static void ExtractSevenZip(string inputPath, string outputDir, IProgress<double>? progress)
    {
        using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = SevenZipArchive.OpenArchive(stream);
        // 7z archives may be solid, so use ExtractAllEntries which handles this
        using var reader = archive.ExtractAllEntries();
        var totalEntries = archive.Entries.Count(e => !e.IsDirectory);
        var processedEntries = 0;

        while (reader.MoveToNextEntry())
        {
            if (!reader.Entry.IsDirectory)
            {
                var entryPath = GetSafeEntryPath(reader.Entry.Key ?? string.Empty, outputDir);
                var entryDir = Path.GetDirectoryName(entryPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                using var entryStream = reader.OpenEntryStream();
                using var fileStream = File.Create(entryPath);
                entryStream.CopyTo(fileStream);
                
                processedEntries++;
                progress?.Report((double)processedEntries / totalEntries);
            }
        }
    }

    private static void ExtractTar(string inputPath, string outputDir, IProgress<double>? progress)
    {
        using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = TarArchive.OpenArchive(stream);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        var totalEntries = entries.Count;
        var processedEntries = 0;

        foreach (var entry in entries)
        {
            var entryPath = GetSafeEntryPath(entry.Key ?? string.Empty, outputDir);
            var entryDir = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            using var entryStream = entry.OpenEntryStream();
            using var fileStream = File.Create(entryPath);
            entryStream.CopyTo(fileStream);
            
            processedEntries++;
            progress?.Report((double)processedEntries / totalEntries);
        }
    }

    private static string GetSafeEntryPath(string entryKey, string outputDir)
    {
        // Sanitize the entry path to prevent path traversal
        var safePath = entryKey.Replace('\\', '/');
        
        // Remove any leading slashes or parent directory references with loop protection
        var previousLength = -1;
        while (safePath.Length != previousLength && (safePath.StartsWith('/') || safePath.StartsWith("../")))
        {
            previousLength = safePath.Length;
            safePath = safePath.TrimStart('/');
            if (safePath.StartsWith("../"))
            {
                safePath = safePath[3..];
            }
        }

        // Remove any remaining parent directory references
        safePath = safePath.Replace("../", "");

        // Ensure path is not empty
        if (string.IsNullOrEmpty(safePath))
        {
            safePath = "extracted_file";
        }

        return Path.Combine(outputDir, safePath);
    }

    private static async Task CreateCbzFromDirectoryAsync(string sourceDir, string cbzPath)
    {
        // Delete existing CBZ if it exists
        if (File.Exists(cbzPath))
        {
            File.Delete(cbzPath);
        }

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(cbzPath, ZipArchiveMode.Create);

            // Get all files maintaining directory structure
            var allFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                .Where(f => ComicConstants.IsImageFile(f) || ComicConstants.IsComicInfoFile(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in allFiles)
            {
                // Get relative path from source directory
                var relativePath = Path.GetRelativePath(sourceDir, file);
                archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
            }
        });
    }

    #region ComicInfo Extraction

    private static async Task<ComicInfo?> ExtractComicInfoFromZipAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using (var archive = ZipFile.OpenRead(filePath))
            {
                var entry = archive.Entries.FirstOrDefault(e => 
                    Path.GetFileName(e.FullName).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                
                if (entry is null)
                {
                    return null;
                }

                using (var stream = entry.Open())
                using (var memoryStream = new MemoryStream())
                {
                    stream.CopyTo(memoryStream);
                    return ParseComicInfo(memoryStream.ToArray());
                }
            }
        });
    }

    private static async Task<ComicInfo?> ExtractComicInfoFromRarAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = RarArchive.OpenArchive(stream))
            {
                if (archive.IsSolid)
                {
                    // For solid archives, use reader interface
                    using (var reader = archive.ExtractAllEntries())
                    {
                        while (reader.MoveToNextEntry())
                        {
                            if (!reader.Entry.IsDirectory && 
                                Path.GetFileName(reader.Entry.Key ?? string.Empty).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                            {
                                using (var entryStream = reader.OpenEntryStream())
                                using (var memoryStream = new MemoryStream())
                                {
                                    entryStream.CopyTo(memoryStream);
                                    return ParseComicInfo(memoryStream.ToArray());
                                }
                            }
                        }
                    }
                }
                else
                {
                    var entry = archive.Entries.FirstOrDefault(e => 
                        !e.IsDirectory && Path.GetFileName(e.Key ?? string.Empty).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                    
                    if (entry is not null)
                    {
                        using (var entryStream = entry.OpenEntryStream())
                        using (var memoryStream = new MemoryStream())
                        {
                            entryStream.CopyTo(memoryStream);
                            return ParseComicInfo(memoryStream.ToArray());
                        }
                    }
                }

                return null;
            }
        });
    }

    private static async Task<ComicInfo?> ExtractComicInfoFromSevenZipAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = SevenZipArchive.OpenArchive(stream))
            {
                using (var reader = archive.ExtractAllEntries())
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (!reader.Entry.IsDirectory && 
                            Path.GetFileName(reader.Entry.Key ?? string.Empty).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var entryStream = reader.OpenEntryStream())
                            using (var memoryStream = new MemoryStream())
                            {
                                entryStream.CopyTo(memoryStream);
                                return ParseComicInfo(memoryStream.ToArray());
                            }
                        }
                    }
                }

                return null;
            }
        });
    }

    private static async Task<ComicInfo?> ExtractComicInfoFromTarAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = TarArchive.OpenArchive(stream))
            {
                var entry = archive.Entries.FirstOrDefault(e => 
                    !e.IsDirectory && Path.GetFileName(e.Key ?? string.Empty).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                
                if (entry is null)
                {
                    return null;
                }

                using (var entryStream = entry.OpenEntryStream())
                using (var memoryStream = new MemoryStream())
                {
                    entryStream.CopyTo(memoryStream);
                    return ParseComicInfo(memoryStream.ToArray());
                }
            }
        });
    }

    #endregion
}

/// <summary>
/// Types of archive formats for comic books
/// </summary>
public enum ComicArchiveType
{
    Unknown,
    Zip,      // CBZ
    Rar,      // CBR
    SevenZip, // CB7
    Tar,      // CBT
    Ace       // CBA - Not supported by SharpCompress
}
