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

using System.Collections.Concurrent;
using StripWolf.Core.Services;

namespace StripWolf.Core.Tests;

/// <summary>
/// IPdfRenderer which "renders" pre-made JPEG pages. The real renderer (PDFium) lives in the StripWolf.Desktop executable.
/// </summary>
internal sealed class FakePdfRenderer : IPdfRenderer
{
    private readonly ConcurrentQueue<FakePdfRenderSession> _sessions = new();

    public FakePdfRenderer(IReadOnlyList<byte[]> pages, PdfMetadata? metadata = null)
    {
        Pages = pages;
        Metadata = metadata;
    }

    public IReadOnlyList<byte[]> Pages { get; }

    public PdfMetadata? Metadata { get; set; }

    public int RenderDpi { get; set; } = 150;

    public int JpegQuality { get; set; } = 85;

    /// <summary>
    /// When set, every render waits for this task (after signalling <see cref="RenderStarted"/>): simulates a slow render
    /// </summary>
    public TaskCompletionSource? RenderGate { get; set; }

    /// <summary>
    /// Completed as soon as a render waits on <see cref="RenderGate"/>
    /// </summary>
    public TaskCompletionSource RenderStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<FakePdfRenderSession> Sessions => _sessions.ToArray();

    public Task<IPdfRenderSession> CreateRenderSessionAsync(string pdfFilePath)
    {
        if (!File.Exists(pdfFilePath))
        {
            throw new FileNotFoundException("PDF not found", pdfFilePath);
        }

        var session = new FakePdfRenderSession(this);
        _sessions.Enqueue(session);
        return Task.FromResult<IPdfRenderSession>(session);
    }

    public int GetPageCount(string pdfFilePath)
    {
        return Pages.Count;
    }

    public Task RenderPdfPagesToJpgAsync(string pdfFilePath, string outputDir, IProgress<double>? progress)
    {
        throw new NotSupportedException("Not used by StripWolf.Core");
    }

    public PdfMetadata? GetMetadata(string pdfFilePath)
    {
        return Metadata;
    }
}

internal sealed class FakePdfRenderSession : IPdfRenderSession
{
    private readonly FakePdfRenderer _owner;
    private readonly ConcurrentQueue<int> _renderedPages = new();
    private int _rendersInProgress;

    public FakePdfRenderSession(FakePdfRenderer owner)
    {
        _owner = owner;
    }

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<int> RenderedPages => _renderedPages.ToArray();

    public int GetPageCount()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _owner.Pages.Count;
    }

    public PdfMetadata? GetMetadata()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _owner.Metadata;
    }

    public async Task RenderPageToJpegAsync(int pageIndex, Stream outputStream)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _owner.Pages.Count);

        _renderedPages.Enqueue(pageIndex);
        Interlocked.Increment(ref _rendersInProgress);
        try
        {
            if (_owner.RenderGate is { } gate)
            {
                _owner.RenderStarted.TrySetResult();
                await gate.Task;
            }

            var page = _owner.Pages[pageIndex];
            await outputStream.WriteAsync(page, 0, page.Length);
        }
        finally
        {
            Interlocked.Decrement(ref _rendersInProgress);
        }
    }

    /// <summary>
    /// True when the session was disposed while a render was still running in it
    /// </summary>
    public bool DisposedDuringRender { get; private set; }

    public void Dispose()
    {
        if (Volatile.Read(ref _rendersInProgress) > 0)
        {
            DisposedDuringRender = true;
        }

        IsDisposed = true;
    }
}
