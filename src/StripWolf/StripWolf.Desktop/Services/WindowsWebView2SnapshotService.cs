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
            var environment = _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            return await PaginationSession.CreateAsync(this, environment, viewportWidth, viewportHeight);
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

    private static async Task<int> GetPageCountAsync(CoreWebView2 coreWebView)
    {
        var pageCountJson = await coreWebView.ExecuteScriptAsync("window.__stripWolfPageCount ?? 1");
        var parsed = JsonSerializer.Deserialize<int?>(pageCountJson);
        return Math.Max(1, parsed ?? 1);
    }

    private static async Task SetPageAsync(CoreWebView2 coreWebView, int pageIndex)
    {
        await coreWebView.ExecuteScriptAsync($"window.__stripWolfSetPage({Math.Max(0, pageIndex)})");
        await WaitForReadyAsync(coreWebView);
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

    private sealed class PaginationSession : IWebViewPaginationSession
    {
        private readonly WindowsWebView2SnapshotService _owner;
        private readonly IntPtr _hostWindow;
        private readonly CoreWebView2Controller _controller;
        private readonly CoreWebView2 _coreWebView;
        private string? _temporaryHtmlPath;
        private bool _disposed;

        private PaginationSession(
            WindowsWebView2SnapshotService owner,
            IntPtr hostWindow,
            CoreWebView2Controller controller,
            CoreWebView2 coreWebView)
        {
            _owner = owner;
            _hostWindow = hostWindow;
            _controller = controller;
            _coreWebView = coreWebView;
        }

        public static async Task<PaginationSession> CreateAsync(
            WindowsWebView2SnapshotService owner,
            CoreWebView2Environment environment,
            int viewportWidth,
            int viewportHeight)
        {
            var hostWindow = CreateHostWindow(viewportWidth, viewportHeight);
            CoreWebView2Controller? controller = null;

            try
            {
                controller = await environment.CreateCoreWebView2ControllerAsync(hostWindow);
                controller.Bounds = new Rectangle(0, 0, viewportWidth, viewportHeight);
                controller.IsVisible = true;
                controller.DefaultBackgroundColor = DrawingColor.White;

                var coreWebView = controller.CoreWebView2;
                coreWebView.Settings.IsStatusBarEnabled = false;
                coreWebView.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView.Settings.AreDevToolsEnabled = false;
                coreWebView.Settings.IsZoomControlEnabled = false;
                return new PaginationSession(owner, hostWindow, controller, coreWebView);
            }
            catch
            {
                controller?.Close();
                DestroyWindow(hostWindow);
                throw;
            }
        }

        public async Task LoadHtmlAsync(string htmlContent)
        {
            ThrowIfDisposed();

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

            string? nextTemporaryHtmlPath = null;
            _coreWebView.NavigationCompleted += OnNavigationCompleted;
            try
            {
                nextTemporaryHtmlPath = EpubSnapshotHtmlHelper.CreateTemporaryHtmlFile(htmlContent);
                if (!string.IsNullOrEmpty(nextTemporaryHtmlPath))
                {
                    _coreWebView.Navigate(new Uri(nextTemporaryHtmlPath).AbsoluteUri);
                }
                else
                {
                    _coreWebView.NavigateToString(htmlContent);
                }

                await navigationCompleted.Task;
                await WaitForReadyAsync(_coreWebView);

                CleanupTemporaryHtmlFile(_temporaryHtmlPath);
                _temporaryHtmlPath = nextTemporaryHtmlPath;
                nextTemporaryHtmlPath = null;
            }
            finally
            {
                _coreWebView.NavigationCompleted -= OnNavigationCompleted;
                CleanupTemporaryHtmlFile(nextTemporaryHtmlPath);
            }
        }

        public Task<int> GetPageCountAsync()
        {
            ThrowIfDisposed();
            return WindowsWebView2SnapshotService.GetPageCountAsync(_coreWebView);
        }

        public async Task<Stream> CapturePageAsync(int pageIndex)
        {
            ThrowIfDisposed();
            var output = RecyclableStreamManagerProvider.Manager.GetStream(nameof(WindowsWebView2SnapshotService));
            await CapturePageToStreamAsync(pageIndex, output);
            output.Position = 0;
            return output;
        }

        public async Task CapturePageToStreamAsync(int pageIndex, Stream outputStream)
        {
            ThrowIfDisposed();
            await SetPageAsync(_coreWebView, pageIndex);
            await _coreWebView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, outputStream);
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _controller.Close();
            DestroyWindow(_hostWindow);
            CleanupTemporaryHtmlFile(_temporaryHtmlPath);
            _owner._gate.Release();
            return ValueTask.CompletedTask;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
