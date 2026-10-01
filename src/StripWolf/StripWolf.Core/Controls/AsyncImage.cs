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
using System.Net.Http.Headers;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace StripWolf.Core.Controls;

/// <summary>
/// Image control that loads images asynchronously with optional Basic authentication.
/// Uses a shared HttpClient for efficient connection pooling and resource management.
/// </summary>
public class AsyncImage : Control
{
    // Shared HttpClient is intentionally kept as a static singleton for efficient connection pooling.
    // This is the recommended pattern for HttpClient as per Microsoft guidelines.
    // The client is never disposed as it's shared across all AsyncImage instances.
    private static readonly HttpClient SharedHttpClient;
    private static readonly SharedBitmapCache LocalBitmapCache = new(200); // Cache up to 200 bitmaps
    // Decoding happens on the thread pool now (it used to run on the UI thread), allow a little parallelism
    private static readonly SemaphoreSlim BitmapDecodeSemaphore = new(2, 2);
    // Small debounce so items which are only scrolled past quickly don't read and decode their image
    private const int UncachedLocalLoadDelayMs = 150;
    // Covers/thumbnails never need to be larger than this (in pixels), large originals (e.g. EPUB covers)
    // are decoded at this width instead of their full size.
    private const int MaxDecodeWidth = 600;
    
    private Bitmap? _loadedBitmap;
    private bool _ownsLoadedBitmap;
    private SharedBitmapCache.Entry? _sharedEntry;
    private bool _isLoading;
    private int _loadVersion;
    private int _activeLoadVersion;
    private CancellationTokenSource? _cts;

    static AsyncImage()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        SharedHttpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        AffectsRender<AsyncImage>(SourceUrlProperty, PlaceholderBrushProperty, StretchProperty);
        SourceUrlProperty.Changed.AddClassHandler<AsyncImage>((x, _) => x.OnSourceUrlChanged());
        UsernameProperty.Changed.AddClassHandler<AsyncImage>((x, _) => x.OnCredentialsChanged());
        PasswordProperty.Changed.AddClassHandler<AsyncImage>((x, _) => x.OnCredentialsChanged());
    }

    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<AsyncImage, string?>(nameof(SourceUrl));

    public static readonly StyledProperty<string?> UsernameProperty =
        AvaloniaProperty.Register<AsyncImage, string?>(nameof(Username));

    public static readonly StyledProperty<string?> PasswordProperty =
        AvaloniaProperty.Register<AsyncImage, string?>(nameof(Password));

    public static readonly StyledProperty<IBrush?> PlaceholderBrushProperty =
        AvaloniaProperty.Register<AsyncImage, IBrush?>(nameof(PlaceholderBrush), Brushes.LightGray);

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<AsyncImage, Stretch>(nameof(Stretch), Stretch.Uniform);

    public static readonly StyledProperty<bool> IsImageLoadedProperty =
        AvaloniaProperty.Register<AsyncImage, bool>(nameof(IsImageLoaded), false);

    /// <summary>
    /// URL of the image to load
    /// </summary>
    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    /// <summary>
    /// Username for Basic authentication (optional)
    /// </summary>
    public string? Username
    {
        get => GetValue(UsernameProperty);
        set => SetValue(UsernameProperty, value);
    }

    /// <summary>
    /// Password for Basic authentication (optional)
    /// </summary>
    public string? Password
    {
        get => GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    /// <summary>
    /// Brush to use as placeholder while loading
    /// </summary>
    public IBrush? PlaceholderBrush
    {
        get => GetValue(PlaceholderBrushProperty);
        set => SetValue(PlaceholderBrushProperty, value);
    }

    /// <summary>
    /// How to stretch the image
    /// </summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets whether the image has finished loading
    /// </summary>
    public bool IsImageLoaded
    {
        get => GetValue(IsImageLoadedProperty);
        private set => SetValue(IsImageLoadedProperty, value);
    }

    private void OnSourceUrlChanged()
    {
        ResetImageState();
        TryStartLoading();
        InvalidateVisual();
    }

    /// <summary>
    /// Called when Username or Password changes - reload the image if we have a URL but no loaded bitmap
    /// </summary>
    private void OnCredentialsChanged()
    {
        // Only retry loading if:
        // 1. We have a URL but no bitmap loaded (possibly due to failed auth)
        // 2. Both username AND password are now available (not null)
        // 3. We're still attached to the visual tree
        var hasCredentials = !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);
        
        if (!string.IsNullOrEmpty(SourceUrl) && _loadedBitmap is null && !_isLoading && hasCredentials && IsAttachedToVisualTree)
        {
            System.Diagnostics.Debug.WriteLine($"AsyncImage: Credentials now available, retrying load for '{SourceUrl}'");
            TryStartLoading();
        }
    }
    
    /// <summary>
    /// Tracks whether this control is currently attached to the visual tree
    /// </summary>
    private bool IsAttachedToVisualTree { get; set; }

    /// <summary>
    /// Safely loads the image with proper exception handling for fire-and-forget scenarios
    /// </summary>
    private async Task LoadImageSafeAsync(int loadVersion, CancellationToken cancellationToken)
    {
        try
        {
            await LoadImageAsync(loadVersion, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Gracefully handle cancellation - do not log as failure.
            System.Diagnostics.Debug.WriteLine($"AsyncImage: Load cancelled for '{SourceUrl}'");
        }
        catch (Exception ex)
        {
            if (!IsStale(loadVersion))
            {
                System.Diagnostics.Debug.WriteLine($"AsyncImage: Failed to load image from '{SourceUrl}': {ex.GetType().Name} - {ex.Message}");
            }
        }
        finally
        {
            if (Volatile.Read(ref _activeLoadVersion) == loadVersion)
            {
                _isLoading = false;
            }
        }
    }

    private async Task LoadImageAsync(int loadVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(SourceUrl))
        {
            return;
        }

        // For remote URLs, require credentials before attempting to load
        // This prevents unnecessary failed requests
        var isRemoteUrl = SourceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          SourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        
        if (isRemoteUrl && (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password)))
        {
            // Don't log for every image, just silently wait for credentials
            return;
        }

        if (IsStale(loadVersion))
        {
            return;
        }

        if (isRemoteUrl)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
            
            // Add Basic authentication if credentials are provided
            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                Bitmap? bitmap = null;
                try
                {
                    await BitmapDecodeSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (!IsStale(loadVersion))
                        {
                            // Decode on a background thread, bitmaps can be created off the UI thread
                            bitmap = await Task.Run(() => DecodeThumbnail(imageBytes), cancellationToken);
                        }
                    }
                    finally
                    {
                        BitmapDecodeSemaphore.Release();
                    }

                    if (bitmap is not null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (!IsStale(loadVersion))
                            {
                                _loadedBitmap = bitmap;
                                _ownsLoadedBitmap = true;
                                IsImageLoaded = true;
                                InvalidateVisual();
                                bitmap = null;
                            }
                        }, DispatcherPriority.ContextIdle);
                    }
                }
                finally
                {
                    bitmap?.Dispose();
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"AsyncImage: Failed to load '{SourceUrl}': HTTP {(int)response.StatusCode}");
            }
        }
        else
        {
            var sourceUrl = SourceUrl;
            if (LocalBitmapCache.TryAcquire(sourceUrl, out var cachedEntry))
            {
                if (!IsStale(loadVersion))
                {
                    SetSharedBitmap(cachedEntry);
                }
                else
                {
                    LocalBitmapCache.Release(cachedEntry);
                }
                return;
            }

            if (!File.Exists(SourceUrl))
            {
                return;
            }

            await Task.Delay(UncachedLocalLoadDelayMs, cancellationToken);
            if (IsStale(loadVersion))
            {
                return;
            }

            var imageBytes = await File.ReadAllBytesAsync(sourceUrl, cancellationToken);
            SharedBitmapCache.Entry sharedEntry;
            await BitmapDecodeSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (IsStale(loadVersion))
                {
                    return;
                }

                // Decode on a background thread (this used to run on the UI thread and caused scroll stutter)
                var bitmap = await Task.Run(() => DecodeThumbnail(imageBytes), cancellationToken);
                sharedEntry = LocalBitmapCache.AddOrAcquire(sourceUrl, bitmap);
            }
            finally
            {
                BitmapDecodeSemaphore.Release();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsStale(loadVersion))
                {
                    SetSharedBitmap(sharedEntry);
                }
                else
                {
                    LocalBitmapCache.Release(sharedEntry);
                }
            });
        }
    }

    private void SetSharedBitmap(SharedBitmapCache.Entry entry)
    {
        if (_sharedEntry is not null)
        {
            LocalBitmapCache.Release(_sharedEntry);
        }
        _sharedEntry = entry;
        _loadedBitmap = entry.Bitmap;
        _ownsLoadedBitmap = false;
        IsImageLoaded = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Decode an image for display as a cover/thumbnail, large images are decoded at a reduced width.
    /// </summary>
    private static Bitmap DecodeThumbnail(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(imageBytes.AsSpan());
            if (info.Width > MaxDecodeWidth)
            {
                return Bitmap.DecodeToWidth(stream, MaxDecodeWidth, BitmapInterpolationMode.HighQuality);
            }
        }
        catch
        {
            // Format unknown to ImageSharp, decode normally
        }

        stream.Position = 0;
        return new Bitmap(stream);
    }

    /// <summary>
    /// LRU cache of decoded local images, shared between AsyncImage instances.
    /// Entries are reference counted: the previous cache disposed evicted bitmaps even when another AsyncImage
    /// was still displaying it, which throws an ObjectDisposedException in the next render pass.
    /// </summary>
    private sealed class SharedBitmapCache
    {
        public sealed class Entry
        {
            public Entry(string key, Bitmap bitmap)
            {
                Key = key;
                Bitmap = bitmap;
            }

            public string Key { get; }
            public Bitmap Bitmap { get; }
            internal int RefCount;
            internal bool IsEvicted;
            internal LinkedListNode<Entry>? Node;
        }

        private readonly int _capacity;
        private readonly Dictionary<string, Entry> _dictionary = new();
        private readonly LinkedList<Entry> _list = new();
        private readonly Lock _lock = new();

        public SharedBitmapCache(int capacity)
        {
            _capacity = capacity;
        }

        public bool TryAcquire(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Entry? entry)
        {
            lock (_lock)
            {
                if (_dictionary.TryGetValue(key, out entry))
                {
                    MoveToFront(entry);
                    entry.RefCount++;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>
        /// Add the bitmap (or use the entry another load added in the meantime) and acquire a reference.
        /// </summary>
        public Entry AddOrAcquire(string key, Bitmap bitmap)
        {
            lock (_lock)
            {
                if (_dictionary.TryGetValue(key, out var existing))
                {
                    bitmap.Dispose();
                    MoveToFront(existing);
                    existing.RefCount++;
                    return existing;
                }

                while (_dictionary.Count >= _capacity && _list.Last is { } last)
                {
                    var evicted = last.Value;
                    _list.RemoveLast();
                    evicted.Node = null;
                    _dictionary.Remove(evicted.Key);
                    evicted.IsEvicted = true;
                    if (evicted.RefCount == 0)
                    {
                        evicted.Bitmap.Dispose();
                    }
                }

                var entry = new Entry(key, bitmap) { RefCount = 1 };
                entry.Node = _list.AddFirst(entry);
                _dictionary[key] = entry;
                return entry;
            }
        }

        public void Release(Entry entry)
        {
            lock (_lock)
            {
                if (entry.RefCount > 0)
                {
                    entry.RefCount--;
                }

                if (entry.IsEvicted && entry.RefCount == 0)
                {
                    entry.Bitmap.Dispose();
                }
            }
        }

        private void MoveToFront(Entry entry)
        {
            if (entry.Node is null) return;
            _list.Remove(entry.Node);
            _list.AddFirst(entry.Node);
        }
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        
        if (_loadedBitmap is not null)
        {
            // Calculate the destination rect based on stretch mode
            var sourceSize = new Size(_loadedBitmap.PixelSize.Width, _loadedBitmap.PixelSize.Height);
            var destRect = CalculateDestRect(rect, sourceSize, Stretch);
            
            context.DrawImage(_loadedBitmap, destRect);
        }
        else if (PlaceholderBrush is not null)
        {
            context.FillRectangle(PlaceholderBrush, rect);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width)
            ? (_loadedBitmap?.PixelSize.Width ?? 0)
            : availableSize.Width;

        var height = double.IsInfinity(availableSize.Height)
            ? (_loadedBitmap?.PixelSize.Height ?? 0)
            : availableSize.Height;

        return new Size(width, height);
    }

    private static Rect CalculateDestRect(Rect bounds, Size sourceSize, Stretch stretch)
    {
        if (sourceSize.Width == 0 || sourceSize.Height == 0)
        {
            return bounds;
        }

        switch (stretch)
        {
            case Stretch.None:
                return new Rect(0, 0, sourceSize.Width, sourceSize.Height);

            case Stretch.Fill:
                return bounds;

            case Stretch.Uniform:
                var scaleX = bounds.Width / sourceSize.Width;
                var scaleY = bounds.Height / sourceSize.Height;
                var scale = Math.Min(scaleX, scaleY);
                var width = sourceSize.Width * scale;
                var height = sourceSize.Height * scale;
                var x = (bounds.Width - width) / 2;
                var y = (bounds.Height - height) / 2;
                return new Rect(x, y, width, height);

            case Stretch.UniformToFill:
                scaleX = bounds.Width / sourceSize.Width;
                scaleY = bounds.Height / sourceSize.Height;
                scale = Math.Max(scaleX, scaleY);
                width = sourceSize.Width * scale;
                height = sourceSize.Height * scale;
                x = (bounds.Width - width) / 2;
                y = (bounds.Height - height) / 2;
                return new Rect(x, y, width, height);

            default:
                return bounds;
        }
    }

    /// <summary>
    /// When attached to the visual tree, check if we need to load an image
    /// This handles the case where credentials become available after initial binding
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        IsAttachedToVisualTree = true;

        if (!string.IsNullOrEmpty(SourceUrl) && _loadedBitmap is null && !_isLoading)
        {
            TryStartLoading();
        }
    }

    /// <summary>
    /// Cleanup resources when the control is detached
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        IsAttachedToVisualTree = false;
        ResetImageState();
    }

    private bool ShouldLoad()
    {
        return IsAttachedToVisualTree &&
               !string.IsNullOrEmpty(SourceUrl);
    }

    private bool IsStale(int loadVersion)
    {
        return loadVersion != Volatile.Read(ref _loadVersion) || !ShouldLoad();
    }

    private void TryStartLoading()
    {
        if (!ShouldLoad() || _loadedBitmap is not null || _isLoading)
        {
            return;
        }

        IsImageLoaded = false;
        _isLoading = true;

        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _cts, newCts);
        if (oldCts is not null)
        {
            try
            {
                oldCts.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { }
            oldCts.Dispose();
        }

        var loadVersion = Volatile.Read(ref _loadVersion);
        Volatile.Write(ref _activeLoadVersion, loadVersion);
        _ = LoadImageSafeAsync(loadVersion, newCts.Token);
    }

    private void ResetImageState()
    {
        IsImageLoaded = false;
        Interlocked.Increment(ref _loadVersion);

        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { }
            cts.Dispose();
        }

        if (_ownsLoadedBitmap)
        {
            _loadedBitmap?.Dispose();
        }

        if (_sharedEntry is not null)
        {
            LocalBitmapCache.Release(_sharedEntry);
            _sharedEntry = null;
        }

        _loadedBitmap = null;
        _ownsLoadedBitmap = false;
        _isLoading = false;
    }
}

