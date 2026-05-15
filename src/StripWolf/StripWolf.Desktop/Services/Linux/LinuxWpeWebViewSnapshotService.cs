using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using StripWolf.Services;

namespace StripWolf.Desktop.Services.Linux;

/// <summary>
/// Linux off-screen snapshot service backed by Avalonia.Controls.WebView (WPE WebKit).
/// </summary>
public sealed class LinuxWpeWebViewSnapshotService : IWebViewPaginationService
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(8);
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
            return await Dispatcher.UIThread.InvokeAsync(() => 
                PaginationSession.CreateAsync(this, viewportWidth, viewportHeight, renderScale));
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

    private sealed class PaginationSession : IWebViewPaginationSession
    {
        private readonly LinuxWpeWebViewSnapshotService _owner;
        private readonly WebView _webView;
        private readonly Window _offscreenWindow;
        private readonly int _viewportWidth;
        private readonly int _viewportHeight;
        private string? _temporaryHtmlPath;
        private TaskCompletionSource? _readyCompletionSource;
        private bool _disposed;

        private PaginationSession(
            LinuxWpeWebViewSnapshotService owner,
            WebView webView,
            Window offscreenWindow,
            int viewportWidth,
            int viewportHeight)
        {
            _owner = owner;
            _webView = webView;
            _offscreenWindow = offscreenWindow;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;

            _webView.MessageReceived += OnWebViewMessageReceived;
        }

        public static Task<PaginationSession> CreateAsync(
            LinuxWpeWebViewSnapshotService owner,
            int viewportWidth,
            int viewportHeight,
            double renderScale)
        {
            var webView = new WebView
            {
                Width = viewportWidth,
                Height = viewportHeight
            };

            // We need a window for the WebView to initialize correctly on some platforms,
            // even if it's never shown.
            var window = new Window
                {
                    Width = viewportWidth,
                    Height = viewportHeight,
                    SystemDecorations = SystemDecorations.None,
                    ShowInTaskbar = false,
                    CanResize = false,
                    Opacity = 0,
                    Content = webView,
                    WindowState = WindowState.Minimized // Try to keep it out of the way
                };

            // Don't actually Show() it if we can avoid it, but WebView might need it.
            // On Linux WPE, OSR might work without a visible window.
            window.Show();
            window.Hide(); 

            return Task.FromResult(new PaginationSession(owner, webView, window, viewportWidth, viewportHeight));
        }

        public async Task LoadHtmlAsync(string htmlContent)
        {
            ThrowIfDisposed();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var readyTask = PrepareReadyAwaiter();
                
                CleanupTemporaryHtmlFile(_temporaryHtmlPath);
                _temporaryHtmlPath = EpubSnapshotHtmlHelper.CreateTemporaryHtmlFile(htmlContent);
                
                if (!string.IsNullOrEmpty(_temporaryHtmlPath))
                {
                    _webView.Source = new Uri(_temporaryHtmlPath);
                }
                else
                {
                    // Fallback if file creation fails
                    _webView.NavigateToString(htmlContent);
                }

                await WaitForReadyAsync(readyTask, "Timed out waiting for Linux WebView EPUB pagination to finish loading.");
            });
        }

        public async Task<int> GetPageCountAsync()
        {
            ThrowIfDisposed();
            var pageCountJson = await Dispatcher.UIThread.InvokeAsync(() => 
                _webView.ExecuteScriptAsync("window.__stripWolfPageCount ?? 1"));
            
            if (string.IsNullOrEmpty(pageCountJson)) return 1;
            
            try 
            {
                var parsed = JsonSerializer.Deserialize<int?>(pageCountJson);
                return Math.Max(1, parsed ?? 1);
            }
            catch
            {
                return 1;
            }
        }

        public async Task<Stream> CapturePageAsync(int pageIndex)
        {
            ThrowIfDisposed();
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var readyTask = PrepareReadyAwaiter();
                await _webView.ExecuteScriptAsync($"window.__stripWolfSetPage({Math.Max(0, pageIndex)})");
                await WaitForReadyAsync(readyTask, "Timed out waiting for Linux WebView EPUB pagination to finish paging.");

                // Use Avalonia's RenderTargetBitmap to capture the WebView
                var pixelSize = new PixelSize(_viewportWidth, _viewportHeight);
                var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
                bitmap.Render(_webView);

                var stream = new MemoryStream();
                bitmap.Save(stream);
                stream.Position = 0;
                return stream;
            });
        }

        public async Task CapturePageToStreamAsync(int pageIndex, Stream outputStream)
        {
            using var sourceStream = await CapturePageAsync(pageIndex);
            await sourceStream.CopyToAsync(outputStream);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _webView.MessageReceived -= OnWebViewMessageReceived;
                _offscreenWindow.Close();
            });

            CleanupTemporaryHtmlFile(_temporaryHtmlPath);
            _owner._gate.Release();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PaginationSession));
        }

        private Task PrepareReadyAwaiter()
        {
            var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _readyCompletionSource = completionSource;
            return completionSource.Task;
        }

        private static async Task WaitForReadyAsync(Task readyTask, string timeoutMessage)
        {
            var cts = new CancellationTokenSource(ReadyTimeout);
            try
            {
                await readyTask.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(timeoutMessage);
            }
        }

        private void OnWebViewMessageReceived(object? sender, WebViewMessageReceivedEventArgs args)
        {
            if (args.Message == "stripwolf-ready")
            {
                var completionSource = _readyCompletionSource;
                _readyCompletionSource = null;
                completionSource?.TrySetResult();
            }
        }

        private static void CleanupTemporaryHtmlFile(string? temporaryHtmlPath)
        {
            if (string.IsNullOrWhiteSpace(temporaryHtmlPath) || !File.Exists(temporaryHtmlPath)) return;
            try { File.Delete(temporaryHtmlPath); } catch { }
        }
    }
}
