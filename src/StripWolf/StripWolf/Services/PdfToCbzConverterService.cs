using System.IO.Compression;
using System.Xml.Serialization;
using StripWolf.Models;

namespace StripWolf.Services;

/// <summary>
/// Service for converting PDF files to CBZ format.
/// Uses platform-specific IPdfRenderer implementations for PDF rendering.
/// </summary>
public class PdfToCbzConverterService
{
    private readonly IPdfRenderer _pdfRenderer;

    /// <summary>
    /// Creates a new instance of the PDF to CBZ converter service.
    /// </summary>
    /// <param name="pdfRenderer">The platform-specific PDF renderer to use</param>
    public PdfToCbzConverterService(IPdfRenderer pdfRenderer)
    {
        _pdfRenderer = pdfRenderer;
    }

    /// <summary>
    /// The DPI to use when rendering PDF pages
    /// </summary>
    public int RenderDpi
    {
        get => _pdfRenderer.RenderDpi;
        set => _pdfRenderer.RenderDpi = value;
    }

    /// <summary>
    /// The JPEG quality to use when saving pages (1-100)
    /// </summary>
    public int JpegQuality
    {
        get => _pdfRenderer.JpegQuality;
        set => _pdfRenderer.JpegQuality = value;
    }

    /// <summary>
    /// Converts a PDF file to CBZ format
    /// </summary>
    /// <param name="pdfFilePath">Path to the PDF file</param>
    /// <param name="outputDirectory">Directory where the CBZ file will be created</param>
    /// <param name="progress">Optional progress reporter (0-1)</param>
    /// <returns>Path to the created CBZ file</returns>
    public async Task<string> ConvertPdfToCbzAsync(
        string pdfFilePath, 
        string outputDirectory,
        IProgress<double>? progress = null)
    {
        if (!File.Exists(pdfFilePath))
        {
            throw new FileNotFoundException("PDF file not found", pdfFilePath);
        }

        var pdfFileName = Path.GetFileNameWithoutExtension(pdfFilePath);
        var cbzFilePath = Path.Combine(outputDirectory, $"{pdfFileName}.cbz");

        // Ensure output directory exists
        Directory.CreateDirectory(outputDirectory);

        // Create a temporary directory for extracted pages with a unique random name
        var tempDir = Path.Combine(Path.GetTempPath(), $"StripWolf_PDF_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Extract PDF metadata before rendering pages
            var pdfMetadata = _pdfRenderer.GetMetadata(pdfFilePath);
            
            // Render PDF pages to JPG files
            await RenderPdfPagesToJpgAsync(pdfFilePath, tempDir, progress);

            // Generate ComicInfo.xml from PDF metadata if available
            if (pdfMetadata is not null)
            {
                var comicInfo = CreateComicInfoFromPdfMetadata(pdfMetadata, pdfFileName);
                await WriteComicInfoAsync(comicInfo, tempDir);
            }

            // Create CBZ file from the JPG files and ComicInfo.xml
            await CreateCbzFromImagesAsync(tempDir, cbzFilePath);

            return cbzFilePath;
        }
        finally
        {
            // Clean up temporary directory - best effort, non-critical
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (IOException)
            {
                // Temporary directory cleanup is best-effort; files will be cleaned up by OS temp cleanup
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary directory cleanup is best-effort; files will be cleaned up by OS temp cleanup
            }
        }
    }

    /// <summary>
    /// Gets the number of pages in a PDF file
    /// </summary>
    public int GetPageCount(string pdfFilePath)
    {
        return _pdfRenderer.GetPageCount(pdfFilePath);
    }

    /// <summary>
    /// Checks if a file is a PDF
    /// </summary>
    public static bool IsPdfFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a ComicInfo object from PDF metadata
    /// </summary>
    private static ComicInfo CreateComicInfoFromPdfMetadata(PdfMetadata pdfMetadata, string fallbackTitle)
    {
        var comicInfo = new ComicInfo
        {
            Title = !string.IsNullOrEmpty(pdfMetadata.Title) ? pdfMetadata.Title : fallbackTitle,
            Writer = pdfMetadata.Author,
            Summary = pdfMetadata.Subject,
            Tags = pdfMetadata.Keywords,
            Notes = !string.IsNullOrEmpty(pdfMetadata.Creator) 
                ? $"Created with: {pdfMetadata.Creator}" 
                : null
        };

        // Set year/month/day from creation date
        if (pdfMetadata.CreationDate.HasValue)
        {
            comicInfo.Year = pdfMetadata.CreationDate.Value.Year;
            comicInfo.Month = pdfMetadata.CreationDate.Value.Month;
            comicInfo.Day = pdfMetadata.CreationDate.Value.Day;
        }

        return comicInfo;
    }

    /// <summary>
    /// Writes a ComicInfo.xml file to the specified directory
    /// </summary>
    private static async Task WriteComicInfoAsync(ComicInfo comicInfo, string directory)
    {
        var comicInfoPath = Path.Combine(directory, "ComicInfo.xml");
        
        await Task.Run(() =>
        {
            // Configure XmlSerializer to not emit null elements
            var serializer = new XmlSerializer(typeof(ComicInfo));
            
            // Create namespaces without xsi to avoid xsi:nil attributes
            var namespaces = new System.Xml.Serialization.XmlSerializerNamespaces();
            namespaces.Add("", ""); // Removes default xsi and xsd namespaces
            
            var settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                Encoding = System.Text.Encoding.UTF8,
                OmitXmlDeclaration = false
            };
            
            using var writer = System.Xml.XmlWriter.Create(comicInfoPath, settings);
            serializer.Serialize(writer, comicInfo, namespaces);
        });
    }

    private async Task RenderPdfPagesToJpgAsync(
        string pdfFilePath, 
        string outputDir,
        IProgress<double>? progress)
    {
        await _pdfRenderer.RenderPdfPagesToJpgAsync(pdfFilePath, outputDir, progress);
    }

    private static async Task CreateCbzFromImagesAsync(string sourceDir, string cbzPath)
    {
        // Delete existing CBZ if it exists
        if (File.Exists(cbzPath))
        {
            File.Delete(cbzPath);
        }

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(cbzPath, ZipArchiveMode.Create);
            
            // First add ComicInfo.xml if it exists
            var comicInfoPath = Path.Combine(sourceDir, "ComicInfo.xml");
            if (File.Exists(comicInfoPath))
            {
                archive.CreateEntryFromFile(comicInfoPath, "ComicInfo.xml", CompressionLevel.Optimal);
            }
            
            // Then add all image files
            var imageFiles = Directory.GetFiles(sourceDir, "*.jpg")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var imageFile in imageFiles)
            {
                var entryName = Path.GetFileName(imageFile);
                archive.CreateEntryFromFile(imageFile, entryName, CompressionLevel.Optimal);
            }
        });
    }
}
