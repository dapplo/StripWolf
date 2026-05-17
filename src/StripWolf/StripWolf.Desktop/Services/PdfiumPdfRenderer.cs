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

using System.Runtime.InteropServices;
using System.Buffers;
using PDFiumCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Threading.Tasks;
using System.IO;

namespace StripWolf.Services;

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

    public Task<IPdfRenderSession> CreateRenderSessionAsync(string pdfFilePath)
    {
        EnsurePdfiumInitialized();

        var document = OpenDocument(pdfFilePath);
        try
        {
            var pageCount = fpdfview.FPDF_GetPageCount(document);
            var metadata = CreateMetadata(document);
            return Task.FromResult<IPdfRenderSession>(new PdfiumRenderSession(pdfFilePath, pageCount, metadata, RenderDpi, JpegQuality));
        }
        finally
        {
            fpdfview.FPDF_CloseDocument(document);
        }
    }

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

    private static FpdfDocumentT OpenDocument(string pdfFilePath)
    {
        var document = fpdfview.FPDF_LoadDocument(pdfFilePath, null);
        if (document == null)
        {
            var error = fpdfview.FPDF_GetLastError();
            var fileName = Path.GetFileName(pdfFilePath);
            throw new InvalidOperationException($"Failed to open PDF file '{fileName}'. Error code: {error}");
        }

        return document;
    }

    private static PdfMetadata? CreateMetadata(FpdfDocumentT document)
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
        using var renderSession = await CreateRenderSessionAsync(pdfFilePath);
        var pageCount = renderSession.GetPageCount();
        for (var i = 0; i < pageCount; i++)
        {
            var outputPath = Path.Combine(outputDir, $"{i + 1:D5}.jpg");
            await using var outputStream = File.OpenWrite(outputPath);
            await renderSession.RenderPageToJpegAsync(i, outputStream);
            progress?.Report((double)(i + 1) / pageCount);
        }
    }

    private sealed class PdfiumRenderSession(string pdfFilePath, int pageCount, PdfMetadata? metadata, int renderDpi, int jpegQuality) : IPdfRenderSession
    {
        private PinnedBgra32Buffer? _pageBuffer;

        public int GetPageCount()
        {
            return pageCount;
        }

        public PdfMetadata? GetMetadata()
        {
            return metadata;
        }

        public Task RenderPageToJpegAsync(int pageIndex, Stream outputStream)
        {
            return Task.Run(() => RenderPageToJpeg(pageIndex, outputStream));
        }

        public void Dispose()
        {
            ((IDisposable?)_pageBuffer)?.Dispose();
        }

        private void RenderPageToJpeg(int pageIndex, Stream outputStream)
        {
            var document = OpenDocument(pdfFilePath);
            var page = fpdfview.FPDF_LoadPage(document, pageIndex);
            if (page == null)
            {
                throw new InvalidOperationException($"Failed to load page {pageIndex}");
            }

            FpdfBitmapT? bitmap = null;
            try
            {
                var widthInPoints = fpdfview.FPDF_GetPageWidthF(page);
                var heightInPoints = fpdfview.FPDF_GetPageHeightF(page);
                var widthInPixels = (int)(widthInPoints * renderDpi / 72.0);
                var heightInPixels = (int)(heightInPoints * renderDpi / 72.0);
                var stride = widthInPixels * Marshal.SizeOf<Bgra32>();
                var pixelBuffer = EnsurePageBufferCapacity(stride * heightInPixels);

                bitmap = fpdfview.FPDFBitmapCreateEx(
                    widthInPixels,
                    heightInPixels,
                    (int)FPDFBitmapFormat.BGRA,
                    pixelBuffer.Pointer,
                    stride);

                if (bitmap == null)
                {
                    throw new InvalidOperationException($"Failed to create bitmap for page {pageIndex}");
                }

                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, widthInPixels, heightInPixels, WhiteBackgroundColor);
                fpdfview.FPDF_RenderPageBitmap(
                    bitmap,
                    page,
                    0, 0,
                    widthInPixels,
                    heightInPixels,
                    0,
                    (int)(RenderFlags.RenderAnnotations | RenderFlags.LimitedImageCache));
                using var image = Image.WrapMemory<Bgra32>(
                    Configuration.Default,
                    pixelBuffer.GetMemory(widthInPixels * heightInPixels),
                    widthInPixels,
                    heightInPixels);
                var encoder = new JpegEncoder { Quality = jpegQuality };
                image.Save(outputStream, encoder);
                Configuration.Default.MemoryAllocator.ReleaseRetainedResources();
            }
            finally
            {
                if (bitmap != null)
                {
                    fpdfview.FPDFBitmapDestroy(bitmap);
                }
                fpdfview.FPDF_ClosePage(page);
                fpdfview.FPDF_CloseDocument(document);
            }
        }

        private PinnedBgra32Buffer EnsurePageBufferCapacity(int requiredByteLength)
        {
            if (_pageBuffer is not null && _pageBuffer.ByteLength >= requiredByteLength)
            {
                return _pageBuffer;
            }

            ((IDisposable?)_pageBuffer)?.Dispose();
            _pageBuffer = new PinnedBgra32Buffer(requiredByteLength);
            return _pageBuffer;
        }

        private sealed class PinnedBgra32Buffer : MemoryManager<Bgra32>
        {
            private readonly byte[] _buffer;
            private readonly GCHandle _handle;
            private readonly int _pixelCount;
            private bool _disposed;

            public PinnedBgra32Buffer(int byteLength)
            {
                _buffer = new byte[byteLength];
                _handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
                _pixelCount = _buffer.Length / Marshal.SizeOf<Bgra32>();
            }

            public int ByteLength => _buffer.Length;

            public IntPtr Pointer => _handle.AddrOfPinnedObject();

            public Memory<Bgra32> GetMemory(int pixelCount)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return Memory.Slice(0, pixelCount);
            }

            public override Span<Bgra32> GetSpan()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return MemoryMarshal.Cast<byte, Bgra32>(_buffer.AsSpan(0, _pixelCount * Marshal.SizeOf<Bgra32>()));
            }

            public override MemoryHandle Pin(int elementIndex = 0)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var byteOffset = elementIndex * Marshal.SizeOf<Bgra32>();
                return _buffer.AsMemory(byteOffset).Pin();
            }

            public override void Unpin()
            {
            }

            protected override void Dispose(bool disposing)
            {
                if (_disposed)
                {
                    return;
                }

                if (_handle.IsAllocated)
                {
                    _handle.Free();
                }

                _disposed = true;
            }
        }
    }
}

