namespace StripWolf.Services;

/// <summary>
/// Interface for rendering PDF pages to image files.
/// Platform-specific implementations provide the actual rendering capability.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// The DPI to use when rendering PDF pages
    /// </summary>
    int RenderDpi { get; set; }

    /// <summary>
    /// The JPEG quality to use when saving pages (1-100)
    /// </summary>
    int JpegQuality { get; set; }

    /// <summary>
    /// Gets the number of pages in a PDF file
    /// </summary>
    /// <param name="pdfFilePath">Path to the PDF file</param>
    /// <returns>Number of pages in the PDF</returns>
    int GetPageCount(string pdfFilePath);

    /// <summary>
    /// Renders all pages of a PDF to JPG files in the specified output directory
    /// </summary>
    /// <param name="pdfFilePath">Path to the PDF file</param>
    /// <param name="outputDir">Directory where JPG files will be saved</param>
    /// <param name="progress">Optional progress reporter (0-1)</param>
    Task RenderPdfPagesToJpgAsync(string pdfFilePath, string outputDir, IProgress<double>? progress);

    /// <summary>
    /// Extracts metadata from a PDF file
    /// </summary>
    /// <param name="pdfFilePath">Path to the PDF file</param>
    /// <returns>PDF metadata, or null if no metadata is available</returns>
    PdfMetadata? GetMetadata(string pdfFilePath);
}

/// <summary>
/// Represents metadata extracted from a PDF file
/// </summary>
public class PdfMetadata
{
    /// <summary>
    /// The document's title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The name of the person who created the document
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// The subject of the document
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Keywords associated with the document
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// The name of the application that created the original document
    /// </summary>
    public string? Creator { get; set; }

    /// <summary>
    /// The name of the application that produced the PDF
    /// </summary>
    public string? Producer { get; set; }

    /// <summary>
    /// The date and time the document was created
    /// </summary>
    public DateTime? CreationDate { get; set; }

    /// <summary>
    /// The date and time the document was last modified
    /// </summary>
    public DateTime? ModificationDate { get; set; }

    /// <summary>
    /// Checks if any metadata fields are set
    /// </summary>
    public bool HasAnyMetadata =>
        !string.IsNullOrEmpty(Title) ||
        !string.IsNullOrEmpty(Author) ||
        !string.IsNullOrEmpty(Subject) ||
        !string.IsNullOrEmpty(Keywords) ||
        !string.IsNullOrEmpty(Creator) ||
        CreationDate.HasValue;
}
