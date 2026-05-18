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
using System.Threading;
using System.Threading.Tasks;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Java.Interop;
using StripWolf.Core.Services;

namespace StripWolf.Core.Android.Services;

/// <summary>
/// Android off-screen snapshot service backed by a hidden native WebView.
/// </summary>
public sealed class AndroidWebViewSnapshotService : Java.Lang.Object, IWebViewPaginationService
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(6);

    static AndroidWebViewSnapshotService()
    {
        WebView.EnableSlowWholeDocumentDraw();
    }

    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Stream> CapturePageAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        await using var session = await CreatePaginationSessionAsync(htmlContent, viewportWidth, viewportHeight);
        return await session.CapturePageAsync(0);
    }

    public async Task<int> GetPageCountAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        await using var session = await CreatePaginationSessionAsync(htmlContent, viewportWidth, viewportHeight);
        return await session.GetPageCountAsync();
    }

    public async Task<IWebViewPaginationSession> CreatePaginationSessionAsync(int viewportWidth, int viewportHeight, double renderScale = 1)
    {
        await _gate.WaitAsync();
        try
        {
            return await PaginationSession.CreateAsync(this, viewportWidth, viewportHeight, renderScale);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    public async Task<IWebViewPaginationSession> CreatePaginationSessionAsync(string htmlContent, int viewportWidth, int viewportHeight, double renderScale = 1)
    {
        var session = await CreatePaginationSessionAsync(viewportWidth, viewportHeight, renderScale);
        try
        {
            await session.LoadHtmlAsync(htmlContent);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private WebView CreateWebView()
    {
        var context = global::Android.App.Application.Context
            ?? throw new InvalidOperationException("Android application context is not available.");

        var webView = new WebView(context);
        webView.Settings.JavaScriptEnabled = true;
        webView.Settings.LoadWithOverviewMode = false;
        webView.Settings.UseWideViewPort = false;
        webView.Settings.AllowFileAccess = true;
        webView.Settings.DomStorageEnabled = true;
        webView.SetBackgroundColor(Color.White);
        webView.SetInitialScale(100);
        webView.SetLayerType(LayerType.Software, null);
        webView.SetWillNotDraw(false);
        webView.HorizontalScrollBarEnabled = false;
        webView.VerticalScrollBarEnabled = false;

        return webView;
    }

    private static async Task<string?> LoadHtmlAsync(WebView webView, string htmlContent, int viewportWidth, int viewportHeight)
    {
        var navigationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        webView.SetWebViewClient(new SnapshotWebViewClient(navigationCompleted));

        var widthSpec = View.MeasureSpec.MakeMeasureSpec(viewportWidth, MeasureSpecMode.Exactly);
        var heightSpec = View.MeasureSpec.MakeMeasureSpec(viewportHeight, MeasureSpecMode.Exactly);
        webView.Measure(widthSpec, heightSpec);
        webView.Layout(0, 0, viewportWidth, viewportHeight);
        webView.ForceLayout();

        var temporaryHtmlPath = EpubSnapshotHtmlHelper.CreateTemporaryHtmlFile(htmlContent);
        if (!string.IsNullOrEmpty(temporaryHtmlPath))
        {
            webView.LoadUrl(new Uri(temporaryHtmlPath).AbsoluteUri);
        }
        else
        {
            var baseUri = EpubSnapshotHtmlHelper.TryExtractBaseUri(htmlContent);
            webView.LoadDataWithBaseURL(baseUri?.AbsoluteUri ?? "about:blank", htmlContent, "text/html", "utf-8", null);
        }

        await navigationCompleted.Task;
        webView.Measure(widthSpec, heightSpec);
        webView.Layout(0, 0, viewportWidth, viewportHeight);
        return temporaryHtmlPath;
    }

    private static async Task<int> GetPageCountAsync(WebView webView)
    {
        var pageCountValue = await EvaluateJavascriptAsync(webView, "String(window.__stripWolfPageCount ?? 1)");
        return int.TryParse(UnwrapJavascriptString(pageCountValue), out var pageCount)
            ? Math.Max(1, pageCount)
            : 1;
    }

    private static Task<string?> EvaluateJavascriptAsync(WebView webView, string script)
    {
        var completionSource = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        webView.EvaluateJavascript(script, new JavascriptValueCallback(result => completionSource.TrySetResult(result)));
        return completionSource.Task;
    }

    private static void CleanupTemporaryHtmlFile(string? temporaryHtmlPath)
    {
        if (string.IsNullOrWhiteSpace(temporaryHtmlPath) || !File.Exists(temporaryHtmlPath))
        {
            return;
        }

        try
        {
            File.Delete(temporaryHtmlPath);
        }
        catch
        {
        }
    }

    private Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Post(async () =>
        {
            try
            {
                completionSource.TrySetResult(await action());
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        });
        return completionSource.Task;
    }

    private Task RunOnUiThreadAsync(Func<Task> action)
    {
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Post(async () =>
        {
            try
            {
                await action();
                completionSource.TrySetResult();
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        });
        return completionSource.Task;
    }

    private static string? UnwrapJavascriptString(string? javascriptValue)
    {
        if (string.IsNullOrWhiteSpace(javascriptValue))
        {
            return javascriptValue;
        }

        var trimmed = javascriptValue.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
    }

    private sealed class SnapshotWebViewClient(TaskCompletionSource navigationCompleted) : WebViewClient
    {
        public override void OnPageFinished(WebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            navigationCompleted.TrySetResult();
        }

        public override void OnReceivedError(WebView? view, IWebResourceRequest? request, WebResourceError? error)
        {
            base.OnReceivedError(view, request, error);
            if (request?.IsForMainFrame == true)
            {
                navigationCompleted.TrySetException(new InvalidOperationException($"Android WebView navigation failed: {error?.Description}"));
            }
        }
    }

    private sealed class JavascriptValueCallback(Action<string?> onValue) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
        {
            onValue(value?.ToString());
        }
    }

    private sealed class PaginationSession : IWebViewPaginationSession
    {
        private readonly AndroidWebViewSnapshotService _owner;
        private readonly WebView _webView;
        private string? _temporaryHtmlPath;
        private readonly int _viewportWidth;
        private readonly int _viewportHeight;
        private readonly double _renderScale;
        private readonly ReadyJavascriptBridge _readyBridge;
        private TaskCompletionSource? _readyCompletionSource;
        private bool _disposed;

        private PaginationSession(
            AndroidWebViewSnapshotService owner,
            WebView webView,
            int viewportWidth,
            int viewportHeight,
            double renderScale)
        {
            _owner = owner;
            _webView = webView;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            _renderScale = renderScale;
            _readyBridge = new ReadyJavascriptBridge(this);
        }

        public static async Task<PaginationSession> CreateAsync(
            AndroidWebViewSnapshotService owner,
            int viewportWidth,
            int viewportHeight,
            double renderScale)
        {
            WebView? webView = null;

            try
            {
                return await owner.RunOnUiThreadAsync(async () =>
                {
                    webView = owner.CreateWebView();
                    var session = new PaginationSession(owner, webView, viewportWidth, viewportHeight, renderScale);
                    webView.AddJavascriptInterface(session._readyBridge, "StripWolfBridge");
                    await Task.CompletedTask;
                    return session;
                });
            }
            catch
            {
                if (webView is not null)
                {
                    await owner.RunOnUiThreadAsync(() =>
                    {
                        webView.StopLoading();
                        webView.Destroy();
                        return Task.CompletedTask;
                    });
                }

                throw;
            }
        }

        public Task LoadHtmlAsync(string htmlContent)
        {
            ThrowIfDisposed();
            return _owner.RunOnUiThreadAsync(() => LoadHtmlCoreAsync(htmlContent));
        }

        public Task<int> GetPageCountAsync()
        {
            ThrowIfDisposed();
            return _owner.RunOnUiThreadAsync(() => AndroidWebViewSnapshotService.GetPageCountAsync(_webView));
        }

        public Task<Stream> CapturePageAsync(int pageIndex)
        {
            ThrowIfDisposed();
            return _owner.RunOnUiThreadAsync(async () =>
            {
                var output = RecyclableStreamManagerProvider.Manager.GetStream(nameof(AndroidWebViewSnapshotService));
                await CapturePageToStreamCoreAsync(pageIndex, output);
                output.Position = 0;
                return (Stream)output;
            });
        }

        public Task CapturePageToStreamAsync(int pageIndex, Stream outputStream)
        {
            ThrowIfDisposed();
            return _owner.RunOnUiThreadAsync(() => CapturePageToStreamCoreAsync(pageIndex, outputStream));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _owner.RunOnUiThreadAsync(() =>
            {
                _webView.StopLoading();
                _webView.Destroy();
                return Task.CompletedTask;
            });

            CleanupTemporaryHtmlFile(_temporaryHtmlPath);
            _owner._gate.Release();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private async Task LoadHtmlCoreAsync(string htmlContent)
        {
            var readyTask = PrepareReadyAwaiter();
            string? nextTemporaryHtmlPath = null;
            try
            {
                nextTemporaryHtmlPath = await AndroidWebViewSnapshotService.LoadHtmlAsync(_webView, htmlContent, _viewportWidth, _viewportHeight);
                await WaitForReadyAsync(readyTask, "Timed out waiting for Android WebView EPUB pagination to finish loading.");

                CleanupTemporaryHtmlFile(_temporaryHtmlPath);
                _temporaryHtmlPath = nextTemporaryHtmlPath;
                nextTemporaryHtmlPath = null;
            }
            finally
            {
                CleanupTemporaryHtmlFile(nextTemporaryHtmlPath);
            }
        }

        private async Task CapturePageToStreamCoreAsync(int pageIndex, Stream outputStream)
        {
            var readyTask = PrepareReadyAwaiter();
            await EvaluateJavascriptAsync(_webView, $"window.__stripWolfSetPage({Math.Max(0, pageIndex)});");
            await WaitForReadyAsync(readyTask, "Timed out waiting for Android WebView EPUB pagination to finish paging.");
            var outputWidth = Math.Max(1, (int)Math.Ceiling(_viewportWidth * _renderScale));
            var outputHeight = Math.Max(1, (int)Math.Ceiling(_viewportHeight * _renderScale));
            using var bitmap = Bitmap.CreateBitmap(outputWidth, outputHeight, Bitmap.Config.Argb8888!);
            bitmap!.Density = (int)Math.Round(((int?)_webView.Resources?.DisplayMetrics?.DensityDpi ?? 160) * _renderScale);
            using var canvas = new Canvas(bitmap!);
            canvas.Scale((float)_renderScale, (float)_renderScale);
            bitmap.EraseColor(Color.White);
            _webView.Invalidate();
            _webView.Draw(canvas);
            bitmap.Compress(Bitmap.CompressFormat.Png!, 100, outputStream);
        }

        private Task PrepareReadyAwaiter()
        {
            var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _readyCompletionSource = completionSource;
            return completionSource.Task;
        }

        private static async Task WaitForReadyAsync(Task readyTask, string timeoutMessage)
        {
            try
            {
                await readyTask.WaitAsync(ReadyTimeout);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(timeoutMessage);
            }
        }

        private void NotifyReady(string? message)
        {
            if (!string.Equals(message, "stripwolf-ready", StringComparison.Ordinal))
            {
                return;
            }

            var completionSource = _readyCompletionSource;
            _readyCompletionSource = null;
            completionSource?.TrySetResult();
        }

        private sealed class ReadyJavascriptBridge(PaginationSession owner) : Java.Lang.Object
        {
            [JavascriptInterface]
            [Export("onPaginationReady")]
            public void OnPaginationReady(string? message)
            {
                owner.NotifyReady(message);
            }
        }
    }
}

