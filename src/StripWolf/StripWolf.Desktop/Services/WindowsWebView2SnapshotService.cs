using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DrawingColor = System.Drawing.Color;
using Microsoft.Web.WebView2.Core;
using StripWolf.Services;

namespace StripWolf.Desktop.Services;

/// <summary>
/// Windows off-screen snapshot service backed by a hidden native WebView2 host.
/// </summary>
public sealed class WindowsWebView2SnapshotService : IWebViewPaginationService
{
    private const int PollDelayMilliseconds = 50;
    private const int MaxReadyChecks = 120;
    private const int SwHide = 0;
    private const int WsOverlapped = 0x00000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsPopup = unchecked((int)0x80000000);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _userDataFolder = Path.Combine(Path.GetTempPath(), "StripWolf.WebView2");

    private CoreWebView2Environment? _environment;

    public async Task<Stream> CapturePageAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        await _gate.WaitAsync();
        try
        {
            return await ExecuteWithWebViewAsync(
                htmlContent,
                viewportWidth,
                viewportHeight,
                async coreWebView =>
                {
                    var output = new MemoryStream();
                    await coreWebView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                    output.Position = 0;
                    return output;
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
            return await ExecuteWithWebViewAsync(
                htmlContent,
                viewportWidth,
                viewportHeight,
                async coreWebView =>
                {
                    var pageCountJson = await coreWebView.ExecuteScriptAsync("window.__stripWolfPageCount ?? 1");
                    var parsed = JsonSerializer.Deserialize<int?>(pageCountJson);
                    return Math.Max(1, parsed ?? 1);
                });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TResult> ExecuteWithWebViewAsync<TResult>(
        string htmlContent,
        int viewportWidth,
        int viewportHeight,
        Func<CoreWebView2, Task<TResult>> action)
    {
        var hostWindow = CreateHostWindow(viewportWidth, viewportHeight);
        CoreWebView2Controller? controller = null;
        string? temporaryHtmlPath = null;

        try
        {
            var environment = _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            controller = await environment.CreateCoreWebView2ControllerAsync(hostWindow);
            controller.Bounds = new Rectangle(0, 0, viewportWidth, viewportHeight);
            controller.IsVisible = true;
            controller.DefaultBackgroundColor = DrawingColor.White;

            var coreWebView = controller.CoreWebView2;
            coreWebView.Settings.IsStatusBarEnabled = false;
            coreWebView.Settings.AreDefaultContextMenusEnabled = false;
            coreWebView.Settings.AreDevToolsEnabled = false;
            coreWebView.Settings.IsZoomControlEnabled = false;

            var navigationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult();
                }
                else
                {
                    navigationCompleted.TrySetException(new InvalidOperationException($"WebView2 navigation failed with status {args.WebErrorStatus}."));
                }
            }

            coreWebView.NavigationCompleted += OnNavigationCompleted;
            try
            {
                temporaryHtmlPath = EpubSnapshotHtmlHelper.CreateTemporaryHtmlFile(htmlContent);
                if (!string.IsNullOrEmpty(temporaryHtmlPath))
                {
                    coreWebView.Navigate(new Uri(temporaryHtmlPath).AbsoluteUri);
                }
                else
                {
                    coreWebView.NavigateToString(htmlContent);
                }

                await navigationCompleted.Task;
                await WaitForReadyAsync(coreWebView);
                return await action(coreWebView);
            }
            finally
            {
                coreWebView.NavigationCompleted -= OnNavigationCompleted;
            }
        }
        finally
        {
            controller?.Close();
            DestroyWindow(hostWindow);
            CleanupTemporaryHtmlFile(temporaryHtmlPath);
        }
    }

    private static IntPtr CreateHostWindow(int width, int height)
    {
        var moduleHandle = GetModuleHandle(null);
        var windowHandle = CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            WsOverlapped | WsClipSiblings | WsClipChildren | WsPopup,
            0,
            0,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            moduleHandle,
            IntPtr.Zero);

        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to create hidden WebView2 host window. Win32 error: {Marshal.GetLastWin32Error()}");
        }

        ShowWindow(windowHandle, SwHide);
        return windowHandle;
    }

    private static async Task WaitForReadyAsync(CoreWebView2 coreWebView)
    {
        for (var attempt = 0; attempt < MaxReadyChecks; attempt++)
        {
            var isReadyJson = await coreWebView.ExecuteScriptAsync("window.__stripWolfReady === true");
            var isReady = JsonSerializer.Deserialize<bool?>(isReadyJson) ?? false;
            if (isReady)
            {
                return;
            }

            await Task.Delay(PollDelayMilliseconds);
        }

        throw new TimeoutException("Timed out waiting for WebView2 EPUB pagination to finish.");
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
