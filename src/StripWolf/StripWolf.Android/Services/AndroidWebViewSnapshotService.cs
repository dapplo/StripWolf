using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using StripWolf.Services;

namespace StripWolf.Android.Services;

/// <summary>
/// Android off-screen snapshot service backed by a hidden native WebView.
/// </summary>
public sealed class AndroidWebViewSnapshotService : Java.Lang.Object, IWebViewPaginationService
{
    private const int PollDelayMilliseconds = 50;
    private const int MaxReadyChecks = 120;

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

    public async Task<IWebViewPaginationSession> CreatePaginationSessionAsync(int viewportWidth, int viewportHeight)
    {
        await _gate.WaitAsync();
        try
        {
            return await PaginationSession.CreateAsync(this, viewportWidth, viewportHeight);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    public async Task<IWebViewPaginationSession> CreatePaginationSessionAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        var session = await CreatePaginationSessionAsync(viewportWidth, viewportHeight);
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

    private static async Task WaitForReadyAsync(WebView webView)
    {
        for (var attempt = 0; attempt < MaxReadyChecks; attempt++)
        {
            var readyValue = await EvaluateJavascriptAsync(webView, "String(window.__stripWolfReady === true)");
            if (string.Equals(UnwrapJavascriptString(readyValue), "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(PollDelayMilliseconds);
        }

        throw new TimeoutException("Timed out waiting for Android WebView EPUB pagination to finish.");
    }

    private static async Task<int> GetPageCountAsync(WebView webView)
    {
        var pageCountValue = await EvaluateJavascriptAsync(webView, "String(window.__stripWolfPageCount ?? 1)");
        return int.TryParse(UnwrapJavascriptString(pageCountValue), out var pageCount)
            ? Math.Max(1, pageCount)
            : 1;
    }

    private static async Task SetPageAsync(WebView webView, int pageIndex)
    {
        await EvaluateJavascriptAsync(webView, $"window.__stripWolfSetPage({Math.Max(0, pageIndex)});");
        await WaitForReadyAsync(webView);
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
        private bool _disposed;

        private PaginationSession(
            AndroidWebViewSnapshotService owner,
            WebView webView,
            int viewportWidth,
            int viewportHeight)
        {
            _owner = owner;
            _webView = webView;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
        }

        public static async Task<PaginationSession> CreateAsync(
            AndroidWebViewSnapshotService owner,
            int viewportWidth,
            int viewportHeight)
        {
            WebView? webView = null;

            try
            {
                return await owner.RunOnUiThreadAsync(async () =>
                {
                    webView = owner.CreateWebView();
                    await Task.CompletedTask;
                    return new PaginationSession(owner, webView, viewportWidth, viewportHeight);
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
            string? nextTemporaryHtmlPath = null;
            try
            {
                nextTemporaryHtmlPath = await AndroidWebViewSnapshotService.LoadHtmlAsync(_webView, htmlContent, _viewportWidth, _viewportHeight);
                await WaitForReadyAsync(_webView);

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
            await SetPageAsync(_webView, pageIndex);
            await Task.Delay(50);

            using var bitmap = Bitmap.CreateBitmap(_viewportWidth, _viewportHeight, Bitmap.Config.Argb8888!);
            bitmap!.Density = (int?)_webView.Resources?.DisplayMetrics?.DensityDpi ?? 160;
            using var canvas = new Canvas(bitmap!);
            bitmap.EraseColor(Color.White);
            _webView.Invalidate();
            _webView.Draw(canvas);
            bitmap.Compress(Bitmap.CompressFormat.Png!, 100, outputStream);
        }
    }
}
