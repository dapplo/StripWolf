namespace Kom2go.Services;

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
}
