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
using System.Net;
using System.Text.RegularExpressions;
using StripWolf.Core.Services;

namespace StripWolf.Core.Tests;

/// <summary>
/// Replaces the platform WebView (WebView2 / WPE) used for the EPUB pagination.
/// Every loaded chapter has <see cref="PageCountForHtml"/> pages, every captured page is <see cref="PageImage"/>.
/// </summary>
internal sealed class FakeWebViewPaginationService : IWebViewPaginationService
{
    private readonly ConcurrentQueue<FakeWebViewPaginationSession> _sessions = new();

    /// <summary>
    /// Number of pages for the loaded chapter HTML, 1 by default
    /// </summary>
    public Func<string, int> PageCountForHtml { get; set; } = _ => 1;

    /// <summary>
    /// The image returned for every captured page (a real PNG, so cover thumbnails can be created from it)
    /// </summary>
    public byte[] PageImage { get; set; } = TestFiles.PageBytes("page2.png");

    /// <summary>
    /// When set, every page capture waits for this task (after signalling <see cref="RenderStarted"/>): simulates a slow WebView capture
    /// </summary>
    public TaskCompletionSource? RenderGate { get; set; }

    /// <summary>
    /// Completed as soon as a capture waits on <see cref="RenderGate"/>
    /// </summary>
    public TaskCompletionSource RenderStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<FakeWebViewPaginationSession> Sessions => _sessions.ToArray();

    public Task<IWebViewPaginationSession> CreatePaginationSessionAsync(int viewportWidth, int viewportHeight, double renderScale = 1)
    {
        var session = new FakeWebViewPaginationSession(this, viewportWidth, viewportHeight, renderScale);
        _sessions.Enqueue(session);
        return Task.FromResult<IWebViewPaginationSession>(session);
    }

    public Task<int> GetPageCountAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        return Task.FromResult(PageCountForHtml(htmlContent));
    }

    public async Task<IWebViewPaginationSession> CreatePaginationSessionAsync(string htmlContent, int viewportWidth, int viewportHeight, double renderScale = 1)
    {
        var session = await CreatePaginationSessionAsync(viewportWidth, viewportHeight, renderScale);
        await session.LoadHtmlAsync(htmlContent);
        return session;
    }

    public Task<Stream> CapturePageAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        return Task.FromResult<Stream>(new MemoryStream(PageImage, writable: false));
    }
}

internal sealed partial class FakeWebViewPaginationSession : IWebViewPaginationSession
{
    private readonly FakeWebViewPaginationService _owner;
    private readonly object _lock = new();
    private readonly List<string> _loadedHtml = [];
    private readonly List<(int LoadIndex, int PageIndex)> _captures = [];
    private readonly List<string> _missingResources = [];
    private string? _currentHtml;
    private int _capturesInProgress;

    public FakeWebViewPaginationSession(FakeWebViewPaginationService owner, int viewportWidth, int viewportHeight, double renderScale)
    {
        _owner = owner;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        RenderScale = renderScale;
    }

    public int ViewportWidth { get; }

    public int ViewportHeight { get; }

    public double RenderScale { get; }

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// All HTML documents loaded into this session, in order
    /// </summary>
    public IReadOnlyList<string> LoadedHtml
    {
        get
        {
            lock (_lock)
            {
                return _loadedHtml.ToList();
            }
        }
    }

    /// <summary>
    /// Captured pages: index of the loaded HTML (in <see cref="LoadedHtml"/>) and the page index within it
    /// </summary>
    public IReadOnlyList<(int LoadIndex, int PageIndex)> Captures
    {
        get
        {
            lock (_lock)
            {
                return _captures.ToList();
            }
        }
    }

    /// <summary>
    /// Images referenced by the loaded HTML which did not exist on disk (relative to the injected &lt;base href&gt;)
    /// </summary>
    public IReadOnlyList<string> MissingResources
    {
        get
        {
            lock (_lock)
            {
                return _missingResources.ToList();
            }
        }
    }

    public Task LoadHtmlAsync(string htmlContent)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_lock)
        {
            _loadedHtml.Add(htmlContent);
            _currentHtml = htmlContent;
            _missingResources.AddRange(FindMissingImages(htmlContent));
        }

        return Task.CompletedTask;
    }

    public Task<int> GetPageCountAsync()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        string html;
        lock (_lock)
        {
            html = _currentHtml ?? throw new InvalidOperationException("No HTML loaded");
        }

        return Task.FromResult(_owner.PageCountForHtml(html));
    }

    public async Task CapturePageToStreamAsync(int pageIndex, Stream outputStream)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_lock)
        {
            if (_currentHtml is null)
            {
                throw new InvalidOperationException("No HTML loaded");
            }

            _captures.Add((_loadedHtml.Count - 1, pageIndex));
        }

        Interlocked.Increment(ref _capturesInProgress);
        try
        {
            if (_owner.RenderGate is { } gate)
            {
                _owner.RenderStarted.TrySetResult();
                await gate.Task;
            }

            await outputStream.WriteAsync(_owner.PageImage, 0, _owner.PageImage.Length);
        }
        finally
        {
            Interlocked.Decrement(ref _capturesInProgress);
        }
    }

    /// <summary>
    /// True when the session was disposed while a capture was still running in it
    /// </summary>
    public bool DisposedDuringCapture { get; private set; }

    public async Task<Stream> CapturePageAsync(int pageIndex)
    {
        var stream = new MemoryStream();
        await CapturePageToStreamAsync(pageIndex, stream);
        stream.Position = 0;
        return stream;
    }

    public ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _capturesInProgress) > 0)
        {
            DisposedDuringCapture = true;
        }

        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    [GeneratedRegex("<base href=\"(?<href>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex BaseHrefRegex();

    [GeneratedRegex("<img\\b[^>]*\\bsrc=\"(?<src>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSourceRegex();

    private static IEnumerable<string> FindMissingImages(string html)
    {
        var baseMatch = BaseHrefRegex().Match(html);
        if (!baseMatch.Success)
        {
            yield return "<no base href>";
            yield break;
        }

        var baseUri = new Uri(WebUtility.HtmlDecode(baseMatch.Groups["href"].Value));
        foreach (Match match in ImageSourceRegex().Matches(html))
        {
            var imageUri = new Uri(baseUri, WebUtility.HtmlDecode(match.Groups["src"].Value));
            if (!imageUri.IsFile || !File.Exists(imageUri.LocalPath))
            {
                yield return imageUri.ToString();
            }
        }
    }
}
