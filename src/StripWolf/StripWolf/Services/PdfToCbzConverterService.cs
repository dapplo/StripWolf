using System.IO.Compression;
using System.Text;
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

        using var renderSession = await _pdfRenderer.CreateRenderSessionAsync(pdfFilePath);

        if (File.Exists(cbzFilePath))
        {
            File.Delete(cbzFilePath);
        }

        using var archive = ZipFile.Open(cbzFilePath, ZipArchiveMode.Create);
        var pdfMetadata = renderSession.GetMetadata();

        if (pdfMetadata is not null)
        {
            var comicInfo = CreateComicInfoFromPdfMetadata(pdfMetadata, pdfFileName);
            var comicInfoEntry = archive.CreateEntry("ComicInfo.xml", CompressionLevel.Optimal);
            await using var comicInfoStream = comicInfoEntry.Open();
            await WriteComicInfoAsync(comicInfo, comicInfoStream);
        }

        var pageCount = renderSession.GetPageCount();

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pageEntry = archive.CreateEntry($"Page_{pageIndex + 1:D5}.jpg", CompressionLevel.NoCompression);
            await using var pageStream = pageEntry.Open();
            await renderSession.RenderPageToJpegAsync(pageIndex, pageStream);
            progress?.Report((double)(pageIndex + 1) / pageCount);
        }

        return cbzFilePath;
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
    /// Writes a ComicInfo.xml file to the specified stream.
    /// </summary>
    private static async Task WriteComicInfoAsync(ComicInfo comicInfo, Stream outputStream)
    {
        await Task.Run(() =>
        {
            var serializer = new XmlSerializer(typeof(ComicInfo));
            var namespaces = new System.Xml.Serialization.XmlSerializerNamespaces();
            namespaces.Add("", "");

            var settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            using var writer = System.Xml.XmlWriter.Create(outputStream, settings);
            serializer.Serialize(writer, comicInfo, namespaces);
        });
    }
}
