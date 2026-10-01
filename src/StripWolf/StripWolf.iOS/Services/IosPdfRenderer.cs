// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.IO;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using UIKit;
using StripWolf.Core.Services;

namespace StripWolf.Core.iOS.Services;

/// <summary>
/// iOS-specific PDF renderer using CoreGraphics (CGPDFDocument).
/// This implementation is used on iOS devices to render PDF pages using Apple's native PDF engine.
/// </summary>
public class IosPdfRenderer : IPdfRenderer
{
    /// <inheritdoc />
    public int RenderDpi { get; set; } = 150;

    /// <inheritdoc />
    public int JpegQuality { get; set; } = 85;

    /// <inheritdoc />
    public Task<IPdfRenderSession> CreateRenderSessionAsync(string pdfFilePath)
    {
        var document = CGPDFDocument.FromFile(pdfFilePath);
        if (document == null)
        {
            throw new InvalidOperationException($"Failed to open PDF file: {Path.GetFileName(pdfFilePath)}");
        }

        return Task.FromResult<IPdfRenderSession>(new IosPdfRenderSession(document, RenderDpi, JpegQuality));
    }

    /// <inheritdoc />
    public int GetPageCount(string pdfFilePath)
    {
        using var document = CGPDFDocument.FromFile(pdfFilePath);
        if (document == null)
        {
            throw new InvalidOperationException($"Failed to open PDF file: {Path.GetFileName(pdfFilePath)}");
        }
        return (int)document.Pages;
    }

    /// <inheritdoc />
    public PdfMetadata? GetMetadata(string pdfFilePath)
    {
        // CoreGraphics doesn't provide easy access to PDF metadata.
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
            await using var outputStream = File.OpenWrite(Path.Combine(outputDir, $"{i + 1:D5}.jpg"));
            await renderSession.RenderPageToJpegAsync(i, outputStream);
            progress?.Report((double)(i + 1) / pageCount);
        }
    }

    private sealed class IosPdfRenderSession(
        CGPDFDocument document,
        int renderDpi,
        int jpegQuality) : IPdfRenderSession
    {
        public int GetPageCount()
        {
            return (int)document.Pages;
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
            document.Dispose();
        }

        private void RenderPageToJpeg(int pageIndex, Stream outputStream)
        {
            // CGPDFDocument page indices are 1-based on iOS!
            using var page = document.GetPage(pageIndex + 1);
            if (page == null)
            {
                throw new InvalidOperationException($"Failed to load page {pageIndex}");
            }

            var rect = page.GetBoxRect(CGPDFBox.Media);
            var widthInPoints = rect.Width;
            var heightInPoints = rect.Height;

            var scale = renderDpi / 72.0;
            var widthInPixels = (int)(widthInPoints * scale);
            var heightInPixels = (int)(heightInPoints * scale);

            using var colorSpace = CGColorSpace.CreateDeviceRGB();
            using var context = new CGBitmapContext(
                IntPtr.Zero,
                widthInPixels,
                heightInPixels,
                8,
                4 * widthInPixels,
                colorSpace,
                CGImageAlphaInfo.PremultipliedLast);

            if (context == null)
            {
                throw new InvalidOperationException($"Failed to create bitmap context for page {pageIndex}");
            }

            // Fill background with white
            context.SetFillColor(1.0f, 1.0f, 1.0f, 1.0f);
            context.FillRect(new CGRect(0, 0, widthInPixels, heightInPixels));

            // Scale and draw PDF page
            context.ScaleCTM((nfloat)scale, (nfloat)scale);
            context.DrawPDFPage(page);

            using var cgImage = context.ToImage();
            if (cgImage == null)
            {
                throw new InvalidOperationException($"Failed to extract image from context for page {pageIndex}");
            }

            using var uiImage = UIImage.FromImage(cgImage);
            if (uiImage == null)
            {
                throw new InvalidOperationException($"Failed to create UIImage for page {pageIndex}");
            }

            using var jpegData = uiImage.AsJPEG((nfloat)(jpegQuality / 100.0));
            if (jpegData == null)
            {
                throw new InvalidOperationException($"Failed to compress image to JPEG for page {pageIndex}");
            }

            using var stream = jpegData.AsStream();
            stream.CopyTo(outputStream);
        }
    }
}
