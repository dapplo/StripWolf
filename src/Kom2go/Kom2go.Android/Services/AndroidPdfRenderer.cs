using System;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using Kom2go.Services;

namespace Kom2go.Android.Services;

/// <summary>
/// Android-specific PDF renderer using Android.Graphics.Pdf.PdfRenderer.
/// This implementation is used on Android devices where PDFium native library is not available.
/// </summary>
public class AndroidPdfRenderer : IPdfRenderer
{
    /// <inheritdoc />
    public int RenderDpi { get; set; } = 150;

    /// <inheritdoc />
    public int JpegQuality { get; set; } = 85;

    /// <inheritdoc />
    public int GetPageCount(string pdfFilePath)
    {
        using var fileDescriptor = ParcelFileDescriptor.Open(
            new Java.IO.File(pdfFilePath),
            ParcelFileMode.ReadOnly);

        if (fileDescriptor == null)
        {
            throw new InvalidOperationException($"Failed to open PDF file: {System.IO.Path.GetFileName(pdfFilePath)}");
        }

        using var pdfRenderer = new PdfRenderer(fileDescriptor);
        return pdfRenderer.PageCount;
    }

    /// <inheritdoc />
    public PdfMetadata? GetMetadata(string pdfFilePath)
    {
        // Android's PdfRenderer does not provide access to PDF metadata.
        // Return null to indicate no metadata is available.
        // The file name will be used as the title fallback in PdfToCbzConverterService.
        return null;
    }

    /// <inheritdoc />
    public async Task RenderPdfPagesToJpgAsync(
        string pdfFilePath,
        string outputDir,
        IProgress<double>? progress)
    {
        await Task.Run(() =>
        {
            using var fileDescriptor = ParcelFileDescriptor.Open(
                new Java.IO.File(pdfFilePath),
                ParcelFileMode.ReadOnly);

            if (fileDescriptor == null)
            {
                throw new InvalidOperationException($"Failed to open PDF file: {System.IO.Path.GetFileName(pdfFilePath)}");
            }

            using var pdfRenderer = new PdfRenderer(fileDescriptor);
            var pageCount = pdfRenderer.PageCount;

            for (var i = 0; i < pageCount; i++)
            {
                RenderPage(pdfRenderer, i, outputDir);
                progress?.Report((double)(i + 1) / pageCount);
            }
        });
    }

    private void RenderPage(PdfRenderer pdfRenderer, int pageIndex, string outputDir)
    {
        using var page = pdfRenderer.OpenPage(pageIndex);
        if (page == null)
        {
            throw new InvalidOperationException($"Failed to load page {pageIndex}");
        }

        // Get page dimensions in points (1 point = 1/72 inch)
        var widthInPoints = page.Width;
        var heightInPoints = page.Height;

        // Calculate pixel dimensions based on DPI
        var widthInPixels = (int)(widthInPoints * RenderDpi / 72.0);
        var heightInPixels = (int)(heightInPoints * RenderDpi / 72.0);

        // Create bitmap with white background
        using var bitmap = Bitmap.CreateBitmap(widthInPixels, heightInPixels, Bitmap.Config.Argb8888!);
        if (bitmap == null)
        {
            throw new InvalidOperationException($"Failed to create bitmap for page {pageIndex}");
        }

        // Fill with white background
        bitmap.EraseColor(Color.White);

        // Render page to bitmap
        page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

        // Save as JPEG
        var outputPath = System.IO.Path.Combine(outputDir, $"{pageIndex + 1:D5}.jpg");
        using var outputStream = System.IO.File.OpenWrite(outputPath);
        bitmap.Compress(Bitmap.CompressFormat.Jpeg!, JpegQuality, outputStream);
    }
}
