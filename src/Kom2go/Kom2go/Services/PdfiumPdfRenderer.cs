using PDFiumCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Kom2go.Services;

/// <summary>
/// PDFium-based PDF renderer for desktop platforms (Windows, macOS, Linux).
/// Uses the PDFiumCore library for native PDF rendering.
/// </summary>
public class PdfiumPdfRenderer : IPdfRenderer
{
    private static bool _pdfiumInitialized;
    private static readonly object InitLock = new();

    /// <summary>
    /// White background color in ARGB format (opaque white)
    /// </summary>
    private const uint WhiteBackgroundColor = 0xFFFFFFFF;

    /// <inheritdoc />
    public int RenderDpi { get; set; } = 150;

    /// <inheritdoc />
    public int JpegQuality { get; set; } = 85;

    /// <summary>
    /// Ensures PDFium is initialized (thread-safe)
    /// </summary>
    private static void EnsurePdfiumInitialized()
    {
        if (_pdfiumInitialized) return;

        lock (InitLock)
        {
            if (_pdfiumInitialized) return;

            fpdfview.FPDF_InitLibrary();
            _pdfiumInitialized = true;
        }
    }

    /// <inheritdoc />
    public int GetPageCount(string pdfFilePath)
    {
        EnsurePdfiumInitialized();

        var document = fpdfview.FPDF_LoadDocument(pdfFilePath, null);
        if (document == null)
        {
            var fileName = Path.GetFileName(pdfFilePath);
            throw new InvalidOperationException($"Failed to open PDF file: {fileName}");
        }

        try
        {
            return fpdfview.FPDF_GetPageCount(document);
        }
        finally
        {
            fpdfview.FPDF_CloseDocument(document);
        }
    }

    /// <inheritdoc />
    public async Task RenderPdfPagesToJpgAsync(
        string pdfFilePath,
        string outputDir,
        IProgress<double>? progress)
    {
        await Task.Run(() =>
        {
            EnsurePdfiumInitialized();

            var document = fpdfview.FPDF_LoadDocument(pdfFilePath, null);
            if (document == null)
            {
                var error = fpdfview.FPDF_GetLastError();
                var fileName = Path.GetFileName(pdfFilePath);
                throw new InvalidOperationException($"Failed to open PDF file '{fileName}'. Error code: {error}");
            }

            try
            {
                var pageCount = fpdfview.FPDF_GetPageCount(document);

                for (var i = 0; i < pageCount; i++)
                {
                    RenderPage(document, i, outputDir);
                    progress?.Report((double)(i + 1) / pageCount);
                }
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(document);
            }
        });
    }

    private void RenderPage(FpdfDocumentT document, int pageIndex, string outputDir)
    {
        var page = fpdfview.FPDF_LoadPage(document, pageIndex);
        if (page == null)
        {
            throw new InvalidOperationException($"Failed to load page {pageIndex}");
        }

        FpdfBitmapT? bitmap = null;
        try
        {
            // Get page dimensions in points (1 point = 1/72 inch)
            var widthInPoints = fpdfview.FPDF_GetPageWidthF(page);
            var heightInPoints = fpdfview.FPDF_GetPageHeightF(page);

            // Calculate pixel dimensions based on DPI
            var widthInPixels = (int)(widthInPoints * RenderDpi / 72.0);
            var heightInPixels = (int)(heightInPoints * RenderDpi / 72.0);

            // Create bitmap
            bitmap = fpdfview.FPDFBitmapCreateEx(
                widthInPixels,
                heightInPixels,
                (int)FPDFBitmapFormat.BGRA,
                IntPtr.Zero,
                0);

            if (bitmap == null)
            {
                throw new InvalidOperationException($"Failed to create bitmap for page {pageIndex}");
            }

            // Fill with white background
            fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, widthInPixels, heightInPixels, WhiteBackgroundColor);

            // Render page to bitmap
            fpdfview.FPDF_RenderPageBitmap(
                bitmap,
                page,
                0, 0,
                widthInPixels,
                heightInPixels,
                0, // No rotation
                (int)RenderFlags.RenderAnnotations);

            // Get bitmap data
            var buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
            var stride = fpdfview.FPDFBitmapGetStride(bitmap);

            // Copy bitmap data to managed array
            var dataSize = stride * heightInPixels;
            var pixelData = new byte[dataSize];
            System.Runtime.InteropServices.Marshal.Copy(buffer, pixelData, 0, dataSize);

            // Convert BGRA to RGBA for ImageSharp using Span for better performance
            ConvertBgraToRgba(pixelData);

            // Save as JPG using ImageSharp
            using var image = Image.LoadPixelData<Rgba32>(pixelData, widthInPixels, heightInPixels);
            var outputPath = Path.Combine(outputDir, $"{pageIndex + 1:D5}.jpg");
            var encoder = new JpegEncoder { Quality = JpegQuality };
            image.Save(outputPath, encoder);
        }
        finally
        {
            if (bitmap != null)
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
            fpdfview.FPDF_ClosePage(page);
        }
    }

    /// <summary>
    /// Converts pixel data from BGRA to RGBA format in-place
    /// </summary>
    private static void ConvertBgraToRgba(Span<byte> pixelData)
    {
        for (var i = 0; i < pixelData.Length; i += 4)
        {
            // Swap B and R channels (indices 0 and 2)
            (pixelData[i], pixelData[i + 2]) = (pixelData[i + 2], pixelData[i]);
        }
    }
}

/// <summary>
/// PDFium render flags
/// </summary>
[Flags]
internal enum RenderFlags
{
    RenderAnnotations = 0x01,
    LcdText = 0x02,
    NoNativeText = 0x04,
    Grayscale = 0x08,
    LimitedImageCache = 0x200,
    ForceHalftone = 0x400,
    Printing = 0x800,
    NoSmoothText = 0x1000,
    NoSmoothImage = 0x2000,
    NoSmoothPath = 0x4000
}
