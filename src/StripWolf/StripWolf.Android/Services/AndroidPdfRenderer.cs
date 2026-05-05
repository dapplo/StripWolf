using System;
using System.IO;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using StripWolf.Services;

namespace StripWolf.Android.Services;

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

    public Task<IPdfRenderSession> CreateRenderSessionAsync(string pdfFilePath)
    {
        var fileDescriptor = ParcelFileDescriptor.Open(
            new Java.IO.File(pdfFilePath),
            ParcelFileMode.ReadOnly);

        if (fileDescriptor == null)
        {
            throw new InvalidOperationException($"Failed to open PDF file: {System.IO.Path.GetFileName(pdfFilePath)}");
        }

        var pdfRenderer = new PdfRenderer(fileDescriptor);
        return Task.FromResult<IPdfRenderSession>(new AndroidPdfRenderSession(fileDescriptor, pdfRenderer, RenderDpi, JpegQuality));
    }

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
        using var renderSession = await CreateRenderSessionAsync(pdfFilePath);
        var pageCount = renderSession.GetPageCount();
        for (var i = 0; i < pageCount; i++)
        {
            await using var outputStream = File.OpenWrite(System.IO.Path.Combine(outputDir, $"{i + 1:D5}.jpg"));
            await renderSession.RenderPageToJpegAsync(i, outputStream);
            progress?.Report((double)(i + 1) / pageCount);
        }
    }

    private sealed class AndroidPdfRenderSession(
        ParcelFileDescriptor fileDescriptor,
        PdfRenderer pdfRenderer,
        int renderDpi,
        int jpegQuality) : IPdfRenderSession
    {
        public int GetPageCount()
        {
            return pdfRenderer.PageCount;
        }

        public PdfMetadata? GetMetadata()
        {
            return null;
        }

        public Task RenderPageToJpegAsync(int pageIndex, Stream outputStream)
        {
            return Task.Run(() => RenderPageToJpeg(pageIndex, outputStream));
        }

        public void Dispose()
        {
            pdfRenderer.Dispose();
            fileDescriptor.Dispose();
        }

        private void RenderPageToJpeg(int pageIndex, Stream outputStream)
        {
            using var page = pdfRenderer.OpenPage(pageIndex);
            if (page == null)
            {
                throw new InvalidOperationException($"Failed to load page {pageIndex}");
            }

            var widthInPoints = page.Width;
            var heightInPoints = page.Height;
            var widthInPixels = (int)(widthInPoints * renderDpi / 72.0);
            var heightInPixels = (int)(heightInPoints * renderDpi / 72.0);

            using var bitmap = Bitmap.CreateBitmap(widthInPixels, heightInPixels, Bitmap.Config.Argb8888!);
            if (bitmap == null)
            {
                throw new InvalidOperationException($"Failed to create bitmap for page {pageIndex}");
            }

            bitmap.EraseColor(Color.White);
            page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
            bitmap.Compress(Bitmap.CompressFormat.Jpeg!, jpegQuality, outputStream);
        }
    }
}
