using System.Net.Http.Headers;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Kom2go.Controls;

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
    
    /// <summary>
    /// Delay in milliseconds to wait for bindings to settle before loading images.
    /// This helps when the control is attached before DataContext bindings are evaluated.
    /// </summary>
    private const int BindingSettleDelayMs = 100;
    
    private Bitmap? _loadedBitmap;
    private bool _isLoading;
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
        // Cancel any pending load operation
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        
        _loadedBitmap?.Dispose();
        _loadedBitmap = null;
        _isLoading = false;
        
        if (!string.IsNullOrEmpty(SourceUrl))
        {
            _cts = new CancellationTokenSource();
            _ = LoadImageSafeAsync(_cts.Token);
        }
        
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
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _ = LoadImageSafeAsync(_cts.Token);
        }
    }
    
    /// <summary>
    /// Tracks whether this control is currently attached to the visual tree
    /// </summary>
    private bool IsAttachedToVisualTree { get; set; }

    /// <summary>
    /// Safely loads the image with proper exception handling for fire-and-forget scenarios
    /// </summary>
    private async Task LoadImageSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await LoadImageAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when the source URL changes before loading completes
        }
        catch (Exception ex)
        {
            // Log error - in a production app, this would use a proper logging framework
            System.Diagnostics.Debug.WriteLine($"AsyncImage: Failed to load image from '{SourceUrl}': {ex.GetType().Name} - {ex.Message}");
        }
    }

    private async Task LoadImageAsync(CancellationToken cancellationToken)
    {
        if (_isLoading || string.IsNullOrEmpty(SourceUrl))
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

        _isLoading = true;

        try
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
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                using var stream = new MemoryStream(bytes);
                
                // Create bitmap on UI thread to ensure proper disposal
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _loadedBitmap = new Bitmap(stream);
                        InvalidateVisual();
                    }
                });
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"AsyncImage: Failed to load '{SourceUrl}': HTTP {(int)response.StatusCode}");
            }
        }
        finally
        {
            _isLoading = false;
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
        
        // If we have a URL but no bitmap, try to load it now with a short delay
        // This helps when the initial binding evaluated before DataContext was set
        if (!string.IsNullOrEmpty(SourceUrl) && _loadedBitmap is null && !_isLoading)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            // Add a small delay to allow bindings to update
            _ = LoadImageWithDelayAsync(_cts.Token);
        }
    }

    /// <summary>
    /// Load image with a small delay to allow bindings to settle
    /// </summary>
    private async Task LoadImageWithDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Wait a bit for bindings to update (especially credentials)
            await Task.Delay(BindingSettleDelayMs, cancellationToken);
            
            if (!cancellationToken.IsCancellationRequested && _loadedBitmap is null && !_isLoading)
            {
                await LoadImageAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AsyncImage: Failed to load image with delay: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleanup resources when the control is detached
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        IsAttachedToVisualTree = false;
        
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        
        _loadedBitmap?.Dispose();
        _loadedBitmap = null;
    }
}
