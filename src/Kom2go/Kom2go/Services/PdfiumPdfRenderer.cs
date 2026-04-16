using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Buffers;
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
    public PdfMetadata? GetMetadata(string pdfFilePath)
    {
        EnsurePdfiumInitialized();

        var document = fpdfview.FPDF_LoadDocument(pdfFilePath, null);
        if (document == null)
        {
            return null;
        }

        try
        {
            var metadata = new PdfMetadata
            {
                Title = GetMetaText(document, "Title"),
                Author = GetMetaText(document, "Author"),
                Subject = GetMetaText(document, "Subject"),
                Keywords = GetMetaText(document, "Keywords"),
                Creator = GetMetaText(document, "Creator"),
                Producer = GetMetaText(document, "Producer"),
                CreationDate = ParsePdfDate(GetMetaText(document, "CreationDate")),
                ModificationDate = ParsePdfDate(GetMetaText(document, "ModDate"))
            };

            return metadata.HasAnyMetadata ? metadata : null;
        }
        finally
        {
            fpdfview.FPDF_CloseDocument(document);
        }
    }

    /// <summary>
    /// Gets a metadata text value from the PDF document
    /// </summary>
    private static string? GetMetaText(FpdfDocumentT document, string tag)
    {
        // First call with null buffer to get the required buffer size
        var requiredSize = fpdf_doc.FPDF_GetMetaText(document, tag, IntPtr.Zero, 0);
        if (requiredSize <= 2) // Size includes null terminator, 2 bytes for empty UTF-16 string
        {
            return null;
        }

        // Allocate buffer and get the actual text
        var buffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            fpdf_doc.FPDF_GetMetaText(document, tag, buffer, requiredSize);
            
            // PDFium returns UTF-16LE encoded strings
            var text = Marshal.PtrToStringUni(buffer);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Parses a PDF date string to a DateTime.
    /// PDF dates are in the format: D:YYYYMMDDHHmmSSOHH'mm'
    /// where O is the timezone offset direction (+ or -)
    /// </summary>
    private static DateTime? ParsePdfDate(string? pdfDate)
    {
        if (string.IsNullOrWhiteSpace(pdfDate))
        {
            return null;
        }

        // Remove the "D:" prefix if present
        if (pdfDate.StartsWith("D:", StringComparison.Ordinal))
        {
            pdfDate = pdfDate[2..];
        }

        // Try to parse the date components
        // Format: YYYYMMDDHHmmSS with optional timezone
        if (pdfDate.Length < 4)
        {
            return null;
        }

        try
        {
            // Extract year (required)
            if (!int.TryParse(pdfDate.AsSpan(0, 4), out var year))
            {
                return null;
            }

            // Extract month (default to 1)
            var month = 1;
            if (pdfDate.Length >= 6 && int.TryParse(pdfDate.AsSpan(4, 2), out var m))
            {
                month = m;
            }

            // Extract day (default to 1)
            var day = 1;
            if (pdfDate.Length >= 8 && int.TryParse(pdfDate.AsSpan(6, 2), out var d))
            {
                day = d;
            }

            // Extract hour (default to 0)
            var hour = 0;
            if (pdfDate.Length >= 10 && int.TryParse(pdfDate.AsSpan(8, 2), out var h))
            {
                hour = h;
            }

            // Extract minute (default to 0)
            var minute = 0;
            if (pdfDate.Length >= 12 && int.TryParse(pdfDate.AsSpan(10, 2), out var min))
            {
                minute = min;
            }

            // Extract second (default to 0)
            var second = 0;
            if (pdfDate.Length >= 14 && int.TryParse(pdfDate.AsSpan(12, 2), out var s))
            {
                second = s;
            }

            // Validate ranges
            if (year < 1 || year > 9999 ||
                month < 1 || month > 12 ||
                day < 1 || day > DateTime.DaysInMonth(year, month) ||
                hour < 0 || hour > 23 ||
                minute < 0 || minute > 59 ||
                second < 0 || second > 59)
            {
                return null;
            }

            // Use Unspecified since PDF dates may contain timezone info that we're not fully parsing
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        }
        catch
        {
            return null;
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

            // Copy bitmap data to rented array from pool to avoid LOH allocations
            var dataSize = stride * heightInPixels;
            var pixelData = ArrayPool<byte>.Shared.Rent(dataSize);
            
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(buffer, pixelData, 0, dataSize);

                // Load as BGRA since that is what PDFium returns. This avoids a manual R/B swap.
                using var image = Image.LoadPixelData<Bgra32>(pixelData.AsSpan(0, dataSize), widthInPixels, heightInPixels);
                var outputPath = Path.Combine(outputDir, $"{pageIndex + 1:D5}.jpg");
                var encoder = new JpegEncoder { Quality = JpegQuality };
                image.Save(outputPath, encoder);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixelData);
            }
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
