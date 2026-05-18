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
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DrawingColor = System.Drawing.Color;
using Microsoft.Web.WebView2.Core;
using StripWolf.Core.Data;
using StripWolf.Core.Services;

namespace StripWolf.Core.Desktop.Services.Windows;

/// <summary>
/// Windows off-screen snapshot service backed by a hidden native WebView2 host.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWebView2SnapshotService : IWebViewPaginationService
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(6);
    private const int SwHide = 0;
    private const int WsDisabled = 0x08000000;
    private const int WsOverlapped = 0x00000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExNoRedirectionBitmap = 0x00200000;
    private const uint WmApp = 0x8000;
    private const uint WmWorkerInvoke = WmApp + 1;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _userDataFolder = Path.Combine(Path.GetTempPath(), "StripWolf.WebView2");
    private readonly WebViewWorker _worker = new();

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
            var environment = await _worker.ExecuteAsync(async () =>
            {
                _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
                return _environment;
            });

            return await PaginationSession.CreateAsync(this, _worker, environment, viewportWidth, viewportHeight, renderScale);
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
            WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap,
            "STATIC",
            string.Empty,
            WsOverlapped | WsClipSiblings | WsClipChildren | WsPopup | WsDisabled,
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
        var parsed = JsonSerializer.Deserialize(pageCountJson, StripWolfJsonContext.Default.NullableInt32);
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(in Message lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(in Message lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PeekMessage(out Message lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    private struct Message
    {
        public IntPtr HWnd;
        public uint Msg;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    private sealed class PaginationSession : IWebViewPaginationSession
    {
        private readonly WindowsWebView2SnapshotService _owner;
        private readonly WebViewWorker _worker;
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
            WebViewWorker worker,
            IntPtr hostWindow,
            CoreWebView2Controller controller,
            CoreWebView2 coreWebView,
            int viewportWidth,
            int viewportHeight,
            double renderScale)
        {
            _owner = owner;
            _worker = worker;
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
            WebViewWorker worker,
            CoreWebView2Environment environment,
            int viewportWidth,
            int viewportHeight,
            double renderScale)
        {
            return await worker.ExecuteAsync(async () =>
            {
                var hostWindow = CreateHostWindow(viewportWidth, viewportHeight);
                CoreWebView2Controller? controller = null;

                try
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
                    return new PaginationSession(owner, worker, hostWindow, controller, coreWebView, viewportWidth, viewportHeight, renderScale);
                }
                catch
                {
                    controller?.Close();
                    DestroyWindow(hostWindow);
                    throw;
                }
            });
        }

        public async Task LoadHtmlAsync(string htmlContent)
        {
            ThrowIfDisposed();
            await _worker.ExecuteAsync(() => LoadHtmlCoreAsync(htmlContent));
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
            return _worker.ExecuteAsync(() => WindowsWebView2SnapshotService.GetPageCountAsync(_coreWebView));
        }

        public async Task<Stream> CapturePageAsync(int pageIndex)
        {
            ThrowIfDisposed();
            return await _worker.ExecuteAsync(async () =>
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
            await _worker.ExecuteAsync(() => CapturePageToStreamCoreAsync(pageIndex, outputStream));
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
            await _worker.ExecuteAsync(() =>
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

    private sealed class WebViewWorker : IAsyncDisposable
    {
        private readonly ConcurrentQueue<Action> _pendingActions = new();
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;
        private int _disposeRequested;
        private uint _threadId;

        public WebViewWorker()
        {
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "StripWolf.WebView2Worker"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            await _started.Task;

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
            {
                _ = ExecuteCoreAsync(action, completion);
            });

            return await completion.Task;
        }

        public async Task ExecuteAsync(Func<Task> action)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            await _started.Task;

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
            {
                _ = ExecuteCoreAsync(action, completion);
            });

            await completion.Task;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            if (_started.Task.IsCompletedSuccessfully)
            {
                PostQuit();
                _thread.Join();
            }

            return ValueTask.CompletedTask;
        }

        private async Task ExecuteCoreAsync<T>(Func<Task<T>> action, TaskCompletionSource<T> completion)
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private async Task ExecuteCoreAsync(Func<Task> action, TaskCompletionSource completion)
        {
            try
            {
                await action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private void ThreadMain()
        {
            SynchronizationContext.SetSynchronizationContext(new WorkerSynchronizationContext(this));
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            _threadId = GetCurrentThreadId();
            _started.TrySetResult();

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Msg == WmWorkerInvoke)
                {
                    DrainPendingActions();
                    continue;
                }

                TranslateMessage(in message);
                DispatchMessage(in message);
            }

            DrainPendingActions();
        }

        private void Enqueue(Action action)
        {
            _pendingActions.Enqueue(action);
            if (!PostThreadMessage(_threadId, WmWorkerInvoke, IntPtr.Zero, IntPtr.Zero))
            {
                throw new InvalidOperationException($"Failed to post WebView2 worker message. Win32 error: {Marshal.GetLastWin32Error()}");
            }
        }

        private void DrainPendingActions()
        {
            while (_pendingActions.TryDequeue(out var action))
            {
                action();
            }
        }

        private void PostQuit()
        {
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private sealed class WorkerSynchronizationContext(WebViewWorker owner) : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                owner.Enqueue(() => d(state));
            }
        }
    }
}

