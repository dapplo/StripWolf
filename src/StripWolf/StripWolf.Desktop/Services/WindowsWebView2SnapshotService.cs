using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DrawingColor = System.Drawing.Color;
using Microsoft.Web.WebView2.Core;
using StripWolf.Services;

namespace StripWolf.Desktop.Services;

/// <summary>
/// Windows off-screen snapshot service backed by a hidden native WebView2 host.
/// </summary>
public sealed class WindowsWebView2SnapshotService : IWebViewPaginationService
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(6);
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

    public async Task<IWebViewPaginationSession> CreatePaginationSessionAsync(int viewportWidth, int viewportHeight, double renderScale = 1)
    {
        await _gate.WaitAsync();
        try
        {
            var environment = await RunOnUiThreadAsync(async () =>
            {
                _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
                return _environment;
            });

            return await PaginationSession.CreateAsync(this, environment, viewportWidth, viewportHeight, renderScale);
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

    private static async Task<int> GetPageCountAsync(CoreWebView2 coreWebView)
    {
        var pageCountJson = await coreWebView.ExecuteScriptAsync("window.__stripWolfPageCount ?? 1");
        var parsed = JsonSerializer.Deserialize<int?>(pageCountJson);
        return Math.Max(1, parsed ?? 1);
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        return Dispatcher.UIThread.InvokeAsync(action);
    }

    private Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        return Dispatcher.UIThread.InvokeAsync(action);
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
        private readonly int _viewportWidth;
        private readonly int _viewportHeight;
        private readonly double _renderScale;
        private string? _temporaryHtmlPath;
        private TaskCompletionSource? _readyCompletionSource;
        private bool _disposed;

        private PaginationSession(
            WindowsWebView2SnapshotService owner,
            IntPtr hostWindow,
            CoreWebView2Controller controller,
            CoreWebView2 coreWebView,
            int viewportWidth,
            int viewportHeight,
            double renderScale)
        {
            _owner = owner;
            _hostWindow = hostWindow;
            _controller = controller;
            _coreWebView = coreWebView;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            _renderScale = renderScale;
            _coreWebView.WebMessageReceived += OnWebMessageReceived;
        }

        public static async Task<PaginationSession> CreateAsync(
            WindowsWebView2SnapshotService owner,
            CoreWebView2Environment environment,
            int viewportWidth,
            int viewportHeight,
            double renderScale)
        {
            var hostWindow = CreateHostWindow(viewportWidth, viewportHeight);
            CoreWebView2Controller? controller = null;

            try
            {
                return await owner.RunOnUiThreadAsync(async () =>
                {
                    controller = await environment.CreateCoreWebView2ControllerAsync(hostWindow);
                    controller.Bounds = new Rectangle(0, 0, viewportWidth, viewportHeight);
                    controller.RasterizationScale = renderScale;
                    controller.IsVisible = true;
                    controller.DefaultBackgroundColor = DrawingColor.White;

                    var coreWebView = controller.CoreWebView2;
                    coreWebView.Settings.IsStatusBarEnabled = false;
                    coreWebView.Settings.AreDefaultContextMenusEnabled = false;
                    coreWebView.Settings.AreDevToolsEnabled = false;
                    coreWebView.Settings.IsZoomControlEnabled = false;
                    return new PaginationSession(owner, hostWindow, controller, coreWebView, viewportWidth, viewportHeight, renderScale);
                });
            }
            catch
            {
                if (controller is not null)
                {
                    await owner.RunOnUiThreadAsync(() =>
                    {
                        controller.Close();
                        DestroyWindow(hostWindow);
                        return Task.CompletedTask;
                    });
                }
                else
                {
                    DestroyWindow(hostWindow);
                }
                throw;
            }
        }

        public async Task LoadHtmlAsync(string htmlContent)
        {
            ThrowIfDisposed();
            await _owner.RunOnUiThreadAsync(() => LoadHtmlCoreAsync(htmlContent));
        }

        private async Task LoadHtmlCoreAsync(string htmlContent)
        {
            var readyTask = PrepareReadyAwaiter();

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
                nextTemporaryHtmlPath = EpubSnapshotHtmlHelper.CreateTemporaryHtmlFile(PrepareHtmlForCapture(htmlContent));
                if (!string.IsNullOrEmpty(nextTemporaryHtmlPath))
                {
                    _coreWebView.Navigate(new Uri(nextTemporaryHtmlPath).AbsoluteUri);
                }
                else
                {
                    _coreWebView.NavigateToString(htmlContent);
                }

                await navigationCompleted.Task;
                await WaitForReadyAsync(readyTask, "Timed out waiting for WebView2 EPUB pagination to finish loading.");

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
            return _owner.RunOnUiThreadAsync(() => WindowsWebView2SnapshotService.GetPageCountAsync(_coreWebView));
        }

        public async Task<Stream> CapturePageAsync(int pageIndex)
        {
            ThrowIfDisposed();
            return await _owner.RunOnUiThreadAsync(async () =>
            {
                var output = RecyclableStreamManagerProvider.Manager.GetStream(nameof(WindowsWebView2SnapshotService));
                await CapturePageToStreamCoreAsync(pageIndex, output);
                output.Position = 0;
                return (Stream)output;
            });
        }

        public async Task CapturePageToStreamAsync(int pageIndex, Stream outputStream)
        {
            ThrowIfDisposed();
            await _owner.RunOnUiThreadAsync(() => CapturePageToStreamCoreAsync(pageIndex, outputStream));
        }

        private async Task CapturePageToStreamCoreAsync(int pageIndex, Stream outputStream)
        {
            var readyTask = PrepareReadyAwaiter();
            await _coreWebView.ExecuteScriptAsync($"window.__stripWolfSetPage({Math.Max(0, pageIndex)})");
            await WaitForReadyAsync(readyTask, "Timed out waiting for WebView2 EPUB pagination to finish paging.");
            await _coreWebView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, outputStream);
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
                _coreWebView.WebMessageReceived -= OnWebMessageReceived;
                _controller.Close();
                DestroyWindow(_hostWindow);
                return Task.CompletedTask;
            });
            CleanupTemporaryHtmlFile(_temporaryHtmlPath);
            _owner._gate.Release();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!string.Equals(args.TryGetWebMessageAsString(), "stripwolf-ready", StringComparison.Ordinal))
            {
                return;
            }

            var completionSource = _readyCompletionSource;
            _readyCompletionSource = null;
            completionSource?.TrySetResult();
        }

        private string PrepareHtmlForCapture(string htmlContent)
        {
            return htmlContent;
        }
    }
}
