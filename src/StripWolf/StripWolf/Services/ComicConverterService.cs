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

        await using var inputStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await ConvertArchiveToCbzAsync(inputStream, archiveType, cbzFilePath, progress);
        return cbzFilePath;
    }

    /// <summary>
    /// Converts a comic book archive stream to CBZ format.
    /// </summary>
    public async Task<string> ConvertToCbzAsync(
        Stream inputStream,
        string sourceFileName,
        ComicArchiveType archiveType,
        string outputDirectory,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        if (archiveType == ComicArchiveType.Unknown)
        {
            throw new NotSupportedException("Unsupported comic archive format.");
        }

        if (archiveType == ComicArchiveType.Ace)
        {
            throw new NotSupportedException("ACE format is not supported. Please convert the file manually.");
        }

        Directory.CreateDirectory(outputDirectory);
        var cbzFileName = Path.GetFileNameWithoutExtension(sourceFileName) + ".cbz";
        var cbzFilePath = Path.Combine(outputDirectory, cbzFileName);

        if (archiveType == ComicArchiveType.Zip)
        {
            if (File.Exists(cbzFilePath))
            {
                File.Delete(cbzFilePath);
            }

            await using var outputStream = new FileStream(cbzFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await inputStream.CopyToAsync(outputStream);
            progress?.Report(1);
            return cbzFilePath;
        }

        await ConvertArchiveToCbzAsync(inputStream, archiveType, cbzFilePath, progress);
        return cbzFilePath;
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

    private async Task ConvertArchiveToCbzAsync(
        Stream inputStream,
        ComicArchiveType archiveType,
        string cbzFilePath,
        IProgress<double>? progress)
    {
        if (File.Exists(cbzFilePath))
        {
            File.Delete(cbzFilePath);
        }

        await Task.Run(() =>
        {
            using var outputStream = new FileStream(cbzFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create);
            ConvertArchiveStreamToCbz(inputStream, archiveType, archive, progress);
        });
    }

    private static void ConvertArchiveStreamToCbz(
        Stream inputStream,
        ComicArchiveType archiveType,
        ZipArchive outputArchive,
        IProgress<double>? progress)
    {
        if (inputStream.CanSeek)
        {
            inputStream.Position = 0;
        }

        using var reader = ReaderFactory.OpenReader(inputStream, new ReaderOptions
        {
            LeaveStreamOpen = true,
            ExtensionHint = GetExtensionHint(archiveType)
        });
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            var safeEntryName = GetSafeEntryName(reader.Entry.Key ?? string.Empty);
            if (!ShouldIncludeEntry(safeEntryName))
            {
                continue;
            }

            using var entryStream = reader.OpenEntryStream();
            CopyEntryToZip(entryStream, outputArchive, safeEntryName);
            ReportStreamingProgress(progress, inputStream);
        }

        progress?.Report(1);
    }

    private static bool ShouldIncludeEntry(string safeEntryName)
    {
        return ComicConstants.IsImageFile(safeEntryName) || ComicConstants.IsComicInfoFile(safeEntryName);
    }

    private static void CopyEntryToZip(Stream entryStream, ZipArchive outputArchive, string safeEntryName)
    {
        var zipEntry = outputArchive.CreateEntry(safeEntryName, CompressionLevel.Optimal);
        using var zipEntryStream = zipEntry.Open();
        entryStream.CopyTo(zipEntryStream);
    }

    private static void ReportStreamingProgress(IProgress<double>? progress, Stream inputStream)
    {
        if (progress is null || !inputStream.CanSeek)
        {
            return;
        }

        var length = inputStream.Length;
        if (length <= 0)
        {
            return;
        }

        progress.Report(Math.Min(1, (double)inputStream.Position / length));
    }

    private static string GetExtensionHint(ComicArchiveType archiveType)
    {
        return archiveType switch
        {
            ComicArchiveType.Rar => ".cbr",
            ComicArchiveType.SevenZip => ".cb7",
            ComicArchiveType.Tar => ".cbt",
            ComicArchiveType.Zip => ".cbz",
            _ => string.Empty
        };
    }

    private static string GetSafeEntryName(string entryKey)
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

        return safePath;
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
