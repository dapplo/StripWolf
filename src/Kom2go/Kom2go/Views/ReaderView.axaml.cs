using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kom2go.ViewModels;

namespace Kom2go.Views;

public partial class ReaderView : UserControl
{
    private Image? _pageImage;
    private ScrollViewer? _imageScroller;
    private Canvas? _zoomCanvas;
    private Grid? _imageContainer;
    
    // Swipe gesture tracking
    private Point? _swipeStartPoint;
    private DateTime _swipeStartTime;
    private const double SwipeThreshold = 80; // Minimum distance for a swipe (in pixels)
    private const double SwipeMaxTimeMs = 500; // Maximum time in milliseconds for a swipe
    private const double SwipeMaxVerticalDeviation = 100; // Maximum vertical deviation allowed
    
    // Pinch zoom tracking (for multi-touch)
    private readonly Dictionary<long, Point> _activeZoomPointers = new();
    private double _initialPinchDistance;
    private double _initialZoomLevel;
    private Point _pinchCenter;
    private Vector _initialScrollOffset;
    
    // Pan tracking (for single finger drag when zoomed)
    private bool _isPanning;
    private Point _panStartPoint;
    private Vector _panScrollOffset;

    public ReaderView()
    {
        InitializeComponent();
        
        // Handle keyboard navigation
        KeyDown += OnKeyDown;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is ReaderViewModel vm)
        {
            // Handle scroll wheel zoom when Ctrl is held
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Get position relative to image for zoom focus
                var position = e.GetPosition(_imageScroller);
                ZoomAtPoint(vm, e.Delta.Y > 0, position);
                e.Handled = true;
            }
            else
            {
                // Use scroll wheel to change pages
                // Scroll down (negative delta) = next page (like scrolling through a document)
                // Scroll up (positive delta) = previous page (going back)
                if (e.Delta.Y > 0 && vm.HasPreviousPage)
                {
                    vm.GoToPreviousPageCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Delta.Y < 0 && vm.HasNextPage)
                {
                    vm.GoToNextPageCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
            case Key.PageUp:
                vm.GoToPreviousPageCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                vm.GoToNextPageCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Home:
                vm.GoToPageCommand.Execute(0);
                e.Handled = true;
                break;
                
            case Key.End:
                if (vm.Comic is not null)
                {
                    vm.GoToPageCommand.Execute(vm.Comic.PageCount - 1);
                }
                e.Handled = true;
                break;
                
            case Key.Add:
            case Key.OemPlus:
                vm.ZoomInCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Subtract:
            case Key.OemMinus:
                vm.ZoomOutCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.D0:
            case Key.NumPad0:
                vm.ResetZoomCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.D2:
            case Key.NumPad2:
                // Toggle two-page mode with "2" key
                vm.ToggleTwoPageModeCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.F:
            case Key.F11:
                vm.ToggleFullScreenCommand.Execute(null);
                ToggleWindowFullScreen();
                e.Handled = true;
                break;
                
            case Key.Escape:
                if (vm.IsFullScreen)
                {
                    vm.ToggleFullScreenCommand.Execute(null);
                    ToggleWindowFullScreen();
                }
                else
                {
                    vm.GoBackCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }

    private void ToggleWindowFullScreen()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
        {
            return;
        }

        if (DataContext is ReaderViewModel { IsFullScreen: true })
        {
            window.WindowState = WindowState.FullScreen;
            window.SystemDecorations = SystemDecorations.None;
        }
        else
        {
            window.WindowState = WindowState.Normal;
            window.SystemDecorations = SystemDecorations.Full;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // Cache control references to avoid repeated FindControl calls
        _pageImage = this.FindControl<Image>("PageImage");
        _imageScroller = this.FindControl<ScrollViewer>("ImageScroller");
        _zoomCanvas = this.FindControl<Canvas>("ZoomCanvas");
        _imageContainer = this.FindControl<Grid>("ImageContainer");
        
        // Focus this control to receive keyboard events
        Focus();
        
        // Update image sizing based on stretch mode and zoom
        UpdateImageSizing();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // Clear cached references
        _pageImage = null;
        _imageScroller = null;
        _zoomCanvas = null;
        _imageContainer = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is ReaderViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ReaderViewModel.StretchMode) ||
                    args.PropertyName == nameof(ReaderViewModel.ZoomLevel) ||
                    args.PropertyName == nameof(ReaderViewModel.CurrentPageImage))
                {
                    UpdateImageSizing();
                }
            };
        }
    }

    /// <summary>
    /// Zooms the image centered on a specific point
    /// </summary>
    private void ZoomAtPoint(ReaderViewModel vm, bool zoomIn, Point viewportPoint)
    {
        if (_imageScroller is null || _imageContainer is null)
        {
            return;
        }

        var oldZoom = vm.ZoomLevel;
        var newZoom = zoomIn ? Math.Min(5.0, oldZoom + 0.25) : Math.Max(0.25, oldZoom - 0.25);
        
        if (Math.Abs(newZoom - oldZoom) < 0.001)
        {
            return;
        }

        // Calculate the position in content coordinates before zoom
        var contentX = _imageScroller.Offset.X + viewportPoint.X;
        var contentY = _imageScroller.Offset.Y + viewportPoint.Y;

        // Apply zoom
        vm.ZoomLevel = newZoom;
        UpdateImageSizing();

        // Calculate new scroll position to keep the point under the cursor
        var scale = newZoom / oldZoom;
        var newScrollX = contentX * scale - viewportPoint.X;
        var newScrollY = contentY * scale - viewportPoint.Y;

        // Clamp to valid range
        var maxX = Math.Max(0, (_imageContainer.Width * newZoom) - _imageScroller.Viewport.Width);
        var maxY = Math.Max(0, (_imageContainer.Height * newZoom) - _imageScroller.Viewport.Height);
        
        _imageScroller.Offset = new Vector(
            Math.Max(0, Math.Min(maxX, newScrollX)),
            Math.Max(0, Math.Min(maxY, newScrollY))
        );
    }

    private void UpdateImageSizing()
    {
        if (DataContext is not ReaderViewModel vm || _zoomCanvas is null || _imageScroller is null || _imageContainer is null)
        {
            return;
        }

        // Get viewport size
        var viewportWidth = _imageScroller.Viewport.Width;
        var viewportHeight = _imageScroller.Viewport.Height;
        
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        // Get image size (use the bitmap size if available)
        double imageWidth = 800;  // Default fallback
        double imageHeight = 600; // Default fallback
        
        if (_pageImage?.Source is Avalonia.Media.Imaging.Bitmap bitmap)
        {
            imageWidth = bitmap.PixelSize.Width;
            imageHeight = bitmap.PixelSize.Height;
        }
        
        // Calculate base size based on stretch mode
        double baseWidth, baseHeight;
        
        switch (vm.StretchMode)
        {
            case StretchMode.FitPage:
                // Fit the entire page within the viewport (both width and height)
                var scaleToFit = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
                baseWidth = imageWidth * scaleToFit;
                baseHeight = imageHeight * scaleToFit;
                break;
                
            case StretchMode.FitWidth:
                // Fit to viewport width
                var scaleToWidth = viewportWidth / imageWidth;
                baseWidth = viewportWidth;
                baseHeight = imageHeight * scaleToWidth;
                break;
                
            case StretchMode.FitHeight:
                // Fit to viewport height
                var scaleToHeight = viewportHeight / imageHeight;
                baseWidth = imageWidth * scaleToHeight;
                baseHeight = viewportHeight;
                break;
                
            default:
                // Original size (StretchMode.Original or any unknown mode)
                baseWidth = imageWidth;
                baseHeight = imageHeight;
                break;
        }
        
        // Apply zoom level
        var zoomedWidth = baseWidth * vm.ZoomLevel;
        var zoomedHeight = baseHeight * vm.ZoomLevel;
        
        // Size the image container
        _imageContainer.Width = zoomedWidth;
        _imageContainer.Height = zoomedHeight;
        
        // Size the canvas to ensure scroll works correctly
        _zoomCanvas.Width = Math.Max(zoomedWidth, viewportWidth);
        _zoomCanvas.Height = Math.Max(zoomedHeight, viewportHeight);
        
        // Center the image if smaller than viewport
        if (zoomedWidth < viewportWidth)
        {
            Canvas.SetLeft(_imageContainer, (viewportWidth - zoomedWidth) / 2);
        }
        else
        {
            Canvas.SetLeft(_imageContainer, 0);
        }
        
        if (zoomedHeight < viewportHeight)
        {
            Canvas.SetTop(_imageContainer, (viewportHeight - zoomedHeight) / 2);
        }
        else
        {
            Canvas.SetTop(_imageContainer, 0);
        }
        
        // Update scroll bar visibility
        _imageScroller.HorizontalScrollBarVisibility = zoomedWidth > viewportWidth 
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto 
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        _imageScroller.VerticalScrollBarVisibility = zoomedHeight > viewportHeight 
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto 
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Re-apply sizing when size changes
        UpdateImageSizing();
    }
    
    #region Zoom Pointer Handling (for pinch zoom on image location)
    
    /// <summary>
    /// Handle pointer pressed for pinch zoom and pan tracking
    /// </summary>
    private void OnZoomPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(_imageScroller);
        var pointerId = (long)e.Pointer.Id;
        var position = pointer.Position;
        
        _activeZoomPointers[pointerId] = position;
        
        if (_activeZoomPointers.Count == 1 && DataContext is ReaderViewModel vm && vm.ZoomLevel > 1.0)
        {
            // Start panning when zoomed
            _isPanning = true;
            _panStartPoint = position;
            _panScrollOffset = _imageScroller?.Offset ?? new Vector(0, 0);
        }
        else if (_activeZoomPointers.Count == 2 && DataContext is ReaderViewModel vm2)
        {
            // Two touches - start tracking for pinch zoom
            _isPanning = false;
            var points = _activeZoomPointers.Values.ToArray();
            _initialPinchDistance = GetDistance(points[0], points[1]);
            _initialZoomLevel = vm2.ZoomLevel;
            _pinchCenter = new Point((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2);
            _initialScrollOffset = _imageScroller?.Offset ?? new Vector(0, 0);
        }
    }
    
    /// <summary>
    /// Handle pointer moved for pinch zoom and pan
    /// </summary>
    private void OnZoomPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointer = e.GetCurrentPoint(_imageScroller);
        var pointerId = (long)e.Pointer.Id;
        var position = pointer.Position;
        
        if (!_activeZoomPointers.ContainsKey(pointerId))
        {
            return;
        }
        
        _activeZoomPointers[pointerId] = position;
        
        // Handle pan (single finger drag when zoomed)
        if (_isPanning && _activeZoomPointers.Count == 1 && _imageScroller is not null)
        {
            var delta = _panStartPoint - position;
            var newOffset = new Vector(
                Math.Max(0, _panScrollOffset.X + delta.X),
                Math.Max(0, _panScrollOffset.Y + delta.Y)
            );
            _imageScroller.Offset = newOffset;
        }
        // Handle pinch zoom with two fingers
        else if (_activeZoomPointers.Count == 2 && DataContext is ReaderViewModel vm && _initialPinchDistance > 0 && _imageScroller is not null)
        {
            var points = _activeZoomPointers.Values.ToArray();
            var currentDistance = GetDistance(points[0], points[1]);
            var currentCenter = new Point((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2);
            
            // Calculate scale factor
            var scale = currentDistance / _initialPinchDistance;
            var newZoom = Math.Max(0.25, Math.Min(5.0, _initialZoomLevel * scale));
            
            if (Math.Abs(newZoom - vm.ZoomLevel) > 0.01)
            {
                // Calculate the point in content coordinates at the initial pinch center
                var contentX = _initialScrollOffset.X + _pinchCenter.X;
                var contentY = _initialScrollOffset.Y + _pinchCenter.Y;
                
                // Apply new zoom
                vm.ZoomLevel = newZoom;
                UpdateImageSizing();
                
                // Calculate new scroll position to keep pinch center stable
                var actualScale = newZoom / _initialZoomLevel;
                var newScrollX = contentX * actualScale - currentCenter.X;
                var newScrollY = contentY * actualScale - currentCenter.Y;
                
                _imageScroller.Offset = new Vector(
                    Math.Max(0, newScrollX),
                    Math.Max(0, newScrollY)
                );
            }
        }
    }
    
    /// <summary>
    /// Handle pointer released for pinch zoom
    /// </summary>
    private void OnZoomPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pointerId = (long)e.Pointer.Id;
        
        _activeZoomPointers.Remove(pointerId);
        
        if (_activeZoomPointers.Count == 0)
        {
            _isPanning = false;
            _initialPinchDistance = 0;
        }
        else if (_activeZoomPointers.Count == 1 && DataContext is ReaderViewModel vm && vm.ZoomLevel > 1.0)
        {
            // Switch to panning
            _isPanning = true;
            _panStartPoint = _activeZoomPointers.Values.First();
            _panScrollOffset = _imageScroller?.Offset ?? new Vector(0, 0);
        }
    }
    
    #endregion
    
    #region Gesture Handling (Swipe and Tap)
    
    // Dictionary for swipe gesture tracking (separate from zoom pointers)
    private readonly Dictionary<long, Point> _gesturePointers = new();
    
    /// <summary>
    /// Handle pointer pressed for swipe gesture tracking
    /// </summary>
    private void OnGesturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        var pointerId = (long)e.Pointer.Id;
        var position = pointer.Position;
        
        _gesturePointers[pointerId] = position;
        
        if (_gesturePointers.Count == 1)
        {
            // Single touch - start tracking for potential swipe
            _swipeStartPoint = position;
            _swipeStartTime = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Handle pointer moved for swipe tracking (pinch handled by zoom handlers)
    /// </summary>
    private void OnGesturePointerMoved(object? sender, PointerEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        var pointerId = (long)e.Pointer.Id;
        var position = pointer.Position;
        
        if (_gesturePointers.ContainsKey(pointerId))
        {
            _gesturePointers[pointerId] = position;
        }
    }
    
    /// <summary>
    /// Handle pointer released for swipe detection and tap handling
    /// </summary>
    private void OnGesturePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        var pointerId = (long)e.Pointer.Id;
        var position = pointer.Position;
        
        var wasSwipe = false;
        
        // Check for swipe gesture when the last finger is released
        if (_gesturePointers.Count == 1 && _swipeStartPoint.HasValue && DataContext is ReaderViewModel vm)
        {
            var elapsed = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
            var deltaX = position.X - _swipeStartPoint.Value.X;
            var deltaY = position.Y - _swipeStartPoint.Value.Y;
            
            // Check if this qualifies as a horizontal swipe
            if (elapsed < SwipeMaxTimeMs && 
                Math.Abs(deltaX) > SwipeThreshold && 
                Math.Abs(deltaY) < SwipeMaxVerticalDeviation)
            {
                wasSwipe = true;
                if (deltaX > 0 && vm.HasPreviousPage)
                {
                    // Swipe right = previous page
                    vm.GoToPreviousPageCommand.Execute(null);
                    e.Handled = true;
                }
                else if (deltaX < 0 && vm.HasNextPage)
                {
                    // Swipe left = next page
                    vm.GoToNextPageCommand.Execute(null);
                    e.Handled = true;
                }
            }
            
            // If no swipe was detected, handle as a tap based on position
            if (!wasSwipe && _gesturePointers.Count == 1)
            {
                HandleTap(position, vm);
                e.Handled = true;
            }
        }
        
        // Clean up tracking
        _gesturePointers.Remove(pointerId);
        
        if (_gesturePointers.Count == 0)
        {
            _swipeStartPoint = null;
        }
        else if (_gesturePointers.Count == 1)
        {
            // Reset for potential new swipe
            _swipeStartPoint = _gesturePointers.Values.First();
            _swipeStartTime = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Handle a tap at the given position by determining which zone was tapped
    /// </summary>
    private void HandleTap(Point position, ReaderViewModel vm)
    {
        var width = Bounds.Width;
        if (width <= 0) return;
        
        var relativeX = position.X / width;
        
        if (relativeX < 0.25)
        {
            // Left zone - previous page
            if (vm.HasPreviousPage)
            {
                vm.GoToPreviousPageCommand.Execute(null);
            }
        }
        else if (relativeX > 0.75)
        {
            // Right zone - next page
            if (vm.HasNextPage)
            {
                vm.GoToNextPageCommand.Execute(null);
            }
        }
        else
        {
            // Center zone - toggle controls
            vm.ToggleControlsCommand.Execute(null);
        }
    }
    
    /// <summary>
    /// Calculate distance between two points
    /// </summary>
    private static double GetDistance(Point p1, Point p2)
    {
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
    
    #endregion
}
