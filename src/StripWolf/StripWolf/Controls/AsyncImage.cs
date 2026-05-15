using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace StripWolf.Controls;

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
    private static readonly LruCache<string, Bitmap> LocalBitmapCache = new(200); // Cache up to 200 bitmaps
    private static readonly SemaphoreSlim BitmapDecodeSemaphore = new(1, 1);
    private const int UncachedLocalLoadDelayMs = 150;
    
    private Bitmap? _loadedBitmap;
    private bool _ownsLoadedBitmap;
    private bool _isLoading;
    private int _loadVersion;
    private int _activeLoadVersion;

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
    private async Task LoadImageSafeAsync(int loadVersion)
    {
        try
        {
            await LoadImageAsync(loadVersion);
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

    private async Task LoadImageAsync(int loadVersion)
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

            using var response = await SharedHttpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                Bitmap? bitmap = null;
                try
                {
                    await BitmapDecodeSemaphore.WaitAsync();
                    try
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (!IsStale(loadVersion))
                            {
                                using var stream = new MemoryStream(imageBytes, writable: false);
                                bitmap = new Bitmap(stream);
                            }
                        }, DispatcherPriority.ContextIdle);
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
            if (LocalBitmapCache.TryGetValue(SourceUrl, out var cachedBitmap))
            {
                if (!IsStale(loadVersion))
                {
                    _loadedBitmap = cachedBitmap;
                    _ownsLoadedBitmap = false;
                    InvalidateVisual();
                }
                return;
            }

            if (!File.Exists(SourceUrl))
            {
                return;
            }

            await Task.Delay(UncachedLocalLoadDelayMs);
            if (IsStale(loadVersion))
            {
                return;
            }

            var imageBytes = await File.ReadAllBytesAsync(SourceUrl);
            Bitmap? sharedBitmap = null;
            await BitmapDecodeSemaphore.WaitAsync();
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsStale(loadVersion))
                    {
                        return;
                    }

                    using var stream = new MemoryStream(imageBytes, writable: false);
                    var bitmap = new Bitmap(stream);
                    sharedBitmap = LocalBitmapCache.GetOrAdd(SourceUrl, bitmap);
                    if (!ReferenceEquals(sharedBitmap, bitmap))
                    {
                        bitmap.Dispose();
                    }
                }, DispatcherPriority.ContextIdle);
            }
            finally
            {
                BitmapDecodeSemaphore.Release();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsStale(loadVersion))
                {
                    _loadedBitmap = sharedBitmap;
                    _ownsLoadedBitmap = false;
                    InvalidateVisual();
                }
            });
        }
    }

    private class LruCache<TKey, TValue> where TKey : notnull where TValue : IDisposable
    {
        private readonly int _capacity;
        private readonly ConcurrentDictionary<TKey, LinkedListNode<CacheEntry>> _dictionary = new();
        private readonly LinkedList<CacheEntry> _list = new();
        private readonly object _lock = new();

        public LruCache(int capacity)
        {
            _capacity = capacity;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_dictionary.TryGetValue(key, out var node))
            {
                lock (_lock)
                {
                    _list.Remove(node);
                    _list.AddFirst(node);
                }
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }

        public TValue GetOrAdd(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_dictionary.TryGetValue(key, out var existingNode))
                {
                    _list.Remove(existingNode);
                    _list.AddFirst(existingNode);
                    return existingNode.Value.Value;
                }

                if (_dictionary.Count >= _capacity)
                {
                    var last = _list.Last;
                    if (last != null)
                    {
                        _list.RemoveLast();
                        _dictionary.TryRemove(last.Value.Key, out _);
                        last.Value.Value.Dispose();
                    }
                }

                var newNode = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
                _list.AddFirst(newNode);
                _dictionary[key] = newNode;
                return value;
            }
        }

        private record CacheEntry(TKey Key, TValue Value);
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

        _isLoading = true;
        var loadVersion = Volatile.Read(ref _loadVersion);
        Volatile.Write(ref _activeLoadVersion, loadVersion);
        _ = LoadImageSafeAsync(loadVersion);
    }

    private void ResetImageState()
    {
        Interlocked.Increment(ref _loadVersion);

        if (_ownsLoadedBitmap)
        {
            _loadedBitmap?.Dispose();
        }

        _loadedBitmap = null;
        _ownsLoadedBitmap = false;
        _isLoading = false;
    }
}
