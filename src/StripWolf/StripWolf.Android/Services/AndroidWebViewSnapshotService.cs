using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
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
        await _gate.WaitAsync();
        try
        {
            return await RunOnUiThreadAsync(async () =>
            {
                using var webView = CreateWebView();
                await LoadHtmlAsync(webView, htmlContent, viewportWidth, viewportHeight);
                await WaitForReadyAsync(webView);
                await Task.Delay(50);

                using var bitmap = Bitmap.CreateBitmap(viewportWidth, viewportHeight, Bitmap.Config.Argb8888!);
                bitmap!.Density = (int?)webView.Resources?.DisplayMetrics?.DensityDpi ?? 160;
                using var canvas = new Canvas(bitmap!);
                bitmap!.EraseColor(Color.White);
                webView.Invalidate();
                webView.Draw(canvas);

                var output = new MemoryStream();
                bitmap.Compress(Bitmap.CompressFormat.Png!, 100, output);
                output.Position = 0;
                return (Stream)output;
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> GetPageCountAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        await _gate.WaitAsync();
        try
        {
            return await RunOnUiThreadAsync(async () =>
            {
                using var webView = CreateWebView();
                await LoadHtmlAsync(webView, htmlContent, viewportWidth, viewportHeight);
                await WaitForReadyAsync(webView);

                var pageCountValue = await EvaluateJavascriptAsync(webView, "String(window.__stripWolfPageCount ?? 1)");
                return int.TryParse(UnwrapJavascriptString(pageCountValue), out var pageCount)
                    ? Math.Max(1, pageCount)
                    : 1;
            });
        }
        finally
        {
            _gate.Release();
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

    private static async Task LoadHtmlAsync(WebView webView, string htmlContent, int viewportWidth, int viewportHeight)
    {
        var navigationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        webView.SetWebViewClient(new SnapshotWebViewClient(navigationCompleted));
        string? temporaryHtmlPath = null;

        var widthSpec = View.MeasureSpec.MakeMeasureSpec(viewportWidth, MeasureSpecMode.Exactly);
        var heightSpec = View.MeasureSpec.MakeMeasureSpec(viewportHeight, MeasureSpecMode.Exactly);
        webView.Measure(widthSpec, heightSpec);
        webView.Layout(0, 0, viewportWidth, viewportHeight);
        webView.ForceLayout();

        try
        {
            temporaryHtmlPath = EpubSnapshotHtmlHelper.CreateTemporaryHtmlFile(htmlContent);
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
        }
        finally
        {
            CleanupTemporaryHtmlFile(temporaryHtmlPath);
        }
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
            catch (System.Exception ex)
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
}
