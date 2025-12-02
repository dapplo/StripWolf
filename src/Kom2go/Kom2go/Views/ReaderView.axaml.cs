using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Kom2go.Models;
using Kom2go.ViewModels;

namespace Kom2go.Views;

public partial class ReaderView : UserControl
{
    private Image? _pageImage;
    private ScrollViewer? _imageScroller;
    private Canvas? _zoomCanvas;
    private Grid? _imageContainer;
    
    // Overview canvas references for zoomed/guided mode
    private Canvas? _overviewCanvas;
    private Canvas? _overviewCanvasRight;
    private Image? _overviewImage;
    private Image? _overviewImageRight;
    private Image? _zoomedAreaLeftImage;
    private Image? _zoomedAreaRightImage;
    private Border? _zoomedBorderLeft;
    private Border? _zoomedBorderRight;
    
    // Zoom region tracking
    private Rectangle? _zoomRegionRect;
    private bool _isDraggingZoomRegion;
    private Point _zoomRegionDragStart;
    
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
    
    /// <summary>
    /// Handle scroll wheel events in zoomed/guided mode
    /// </summary>
    private void OnZoomedGuidedPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is ReaderViewModel vm)
        {
            if (vm.IsGuidedMode)
            {
                // In guided mode, scroll through panels
                if (e.Delta.Y > 0 && vm.HasPreviousPanel)
                {
                    vm.GoToPreviousPanelCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Delta.Y < 0 && vm.HasNextPanel)
                {
                    vm.GoToNextPanelCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (vm.ReadingMode == ReadingMode.Zoomed)
            {
                // In zoomed mode, use Ctrl+scroll to change zoom region size,
                // otherwise change pages
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (e.Delta.Y > 0)
                    {
                        vm.DecreaseZoomRegionSizeCommand.Execute(null);
                    }
                    else
                    {
                        vm.IncreaseZoomRegionSizeCommand.Execute(null);
                    }
                    e.Handled = true;
                }
                else
                {
                    // Scroll through pages
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
                // In guided mode, navigate panels instead of pages
                if (vm.IsGuidedMode)
                {
                    vm.GoToPreviousPanelCommand.Execute(null);
                }
                else
                {
                    vm.GoToPreviousPageCommand.Execute(null);
                }
                e.Handled = true;
                break;
                
            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                // In guided mode, navigate panels instead of pages
                if (vm.IsGuidedMode)
                {
                    vm.GoToNextPanelCommand.Execute(null);
                }
                else
                {
                    vm.GoToNextPageCommand.Execute(null);
                }
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
                if (vm.IsZoomedOrGuidedMode)
                {
                    vm.IncreaseZoomRegionSizeCommand.Execute(null);
                }
                else
                {
                    vm.ZoomInCommand.Execute(null);
                }
                e.Handled = true;
                break;
                
            case Key.Subtract:
            case Key.OemMinus:
                if (vm.IsZoomedOrGuidedMode)
                {
                    vm.DecreaseZoomRegionSizeCommand.Execute(null);
                }
                else
                {
                    vm.ZoomOutCommand.Execute(null);
                }
                e.Handled = true;
                break;
                
            case Key.D0:
            case Key.NumPad0:
                if (vm.IsZoomedOrGuidedMode)
                {
                    vm.ResetZoomRegionCommand.Execute(null);
                }
                else
                {
                    vm.ResetZoomCommand.Execute(null);
                }
                e.Handled = true;
                break;
                
            case Key.D2:
            case Key.NumPad2:
                // Toggle two-page mode with "2" key (only in normal mode)
                if (vm.CanUseTwoPageMode)
                {
                    vm.ToggleTwoPageModeCommand.Execute(null);
                }
                e.Handled = true;
                break;
                
            case Key.M:
                // Cycle reading mode with "M" key
                vm.CycleReadingModeCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.H:
                // Toggle handedness with "H" key
                if (vm.IsZoomedOrGuidedMode)
                {
                    vm.ToggleHandednessCommand.Execute(null);
                }
                e.Handled = true;
                break;
                
            case Key.F:
            case Key.F11:
                vm.ToggleFullScreenCommand.Execute(null);
                ToggleWindowFullScreen();
                e.Handled = true;
                break;
                
            case Key.I:
                vm.ToggleInfoPanelCommand.Execute(null);
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
        
        // Cache zoomed/guided mode control references
        _overviewCanvas = this.FindControl<Canvas>("OverviewCanvas");
        _overviewCanvasRight = this.FindControl<Canvas>("OverviewCanvasRight");
        _overviewImage = this.FindControl<Image>("OverviewImage");
        _overviewImageRight = this.FindControl<Image>("OverviewImageRight");
        _zoomedAreaLeftImage = this.FindControl<Image>("ZoomedAreaLeftImage");
        _zoomedAreaRightImage = this.FindControl<Image>("ZoomedAreaRightImage");
        _zoomedBorderLeft = this.FindControl<Border>("ZoomedBorderLeft");
        _zoomedBorderRight = this.FindControl<Border>("ZoomedBorderRight");
        
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
        _overviewCanvas = null;
        _overviewCanvasRight = null;
        _overviewImage = null;
        _overviewImageRight = null;
        _zoomedAreaLeftImage = null;
        _zoomedAreaRightImage = null;
        _zoomedBorderLeft = null;
        _zoomedBorderRight = null;
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
                
                // Update overlay when in zoomed/guided mode
                if (args.PropertyName == nameof(ReaderViewModel.ReadingMode) ||
                    args.PropertyName == nameof(ReaderViewModel.ZoomRegion) ||
                    args.PropertyName == nameof(ReaderViewModel.CurrentPagePanels) ||
                    args.PropertyName == nameof(ReaderViewModel.CurrentPanel) ||
                    args.PropertyName == nameof(ReaderViewModel.CurrentPanelIndex) ||
                    args.PropertyName == nameof(ReaderViewModel.CurrentPageImage) ||
                    args.PropertyName == nameof(ReaderViewModel.Handedness))
                {
                    // Use Dispatcher to ensure layout is complete before updating overlay
                    // Check if still attached to visual tree before updating
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (IsAttachedToVisualTree())
                        {
                            UpdateOverviewOverlay();
                            UpdateZoomedAreaClip();
                        }
                    });
                }
            };
        }
    }
    
    /// <summary>
    /// Check if this control is still attached to the visual tree
    /// </summary>
    private bool IsAttachedToVisualTree()
    {
        return _pageImage is not null || _imageScroller is not null;
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
        
        // Also update zoomed/guided mode overlays
        if (DataContext is ReaderViewModel vm && vm.IsZoomedOrGuidedMode)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (IsAttachedToVisualTree())
                {
                    UpdateOverviewOverlay();
                    UpdateZoomedAreaClip();
                }
            });
        }
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
    
    #region Zoomed/Guided Mode Methods
    
    /// <summary>
    /// Update the overlay on the overview image showing panels and zoom region
    /// </summary>
    private void UpdateOverviewOverlay()
    {
        if (DataContext is not ReaderViewModel vm)
        {
            return;
        }
        
        var canvas = vm.IsOverviewOnLeft ? _overviewCanvas : _overviewCanvasRight;
        var image = vm.IsOverviewOnLeft ? _overviewImage : _overviewImageRight;
        
        if (canvas is null || image is null)
        {
            return;
        }
        
        // Clear existing shapes
        canvas.Children.Clear();
        
        // Get image bounds within the canvas
        var imageBounds = GetImageBoundsInContainer(image);
        if (imageBounds.Width <= 0 || imageBounds.Height <= 0)
        {
            return;
        }
        
        // In guided mode, draw panel boxes
        if (vm.IsGuidedMode && vm.CurrentPagePanels is not null)
        {
            foreach (var panel in vm.CurrentPagePanels.Panels)
            {
                var isCurrentPanel = panel.PanelIndex == vm.CurrentPanelIndex;
                var rect = new Rectangle
                {
                    Width = panel.Width * imageBounds.Width,
                    Height = panel.Height * imageBounds.Height,
                    Stroke = isCurrentPanel ? Brushes.Cyan : Brushes.Yellow,
                    StrokeThickness = isCurrentPanel ? 3 : 1,
                    Fill = isCurrentPanel ? new SolidColorBrush(Color.FromArgb(40, 0, 255, 255)) : null,
                    Opacity = isCurrentPanel ? 1 : 0.7
                };
                Canvas.SetLeft(rect, imageBounds.X + panel.X * imageBounds.Width);
                Canvas.SetTop(rect, imageBounds.Y + panel.Y * imageBounds.Height);
                canvas.Children.Add(rect);
            }
        }
        
        // In zoomed mode, draw the zoom region box
        if (vm.ReadingMode == ReadingMode.Zoomed)
        {
            var bounds = vm.ZoomRegion.GetBounds();
            var rect = new Rectangle
            {
                Width = (bounds.Right - bounds.Left) * imageBounds.Width,
                Height = (bounds.Bottom - bounds.Top) * imageBounds.Height,
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 255))
            };
            Canvas.SetLeft(rect, imageBounds.X + bounds.Left * imageBounds.Width);
            Canvas.SetTop(rect, imageBounds.Y + bounds.Top * imageBounds.Height);
            canvas.Children.Add(rect);
            
            // Store reference for dragging
            _zoomRegionRect = rect;
        }
    }
    
    /// <summary>
    /// Update the zoomed area display using scale and translate transforms
    /// to show the selected region enlarged to fill the container
    /// </summary>
    private void UpdateZoomedAreaClip()
    {
        if (DataContext is not ReaderViewModel vm)
        {
            return;
        }
        
        var zoomedImage = vm.IsOverviewOnLeft ? _zoomedAreaRightImage : _zoomedAreaLeftImage;
        var zoomedBorder = vm.IsOverviewOnLeft ? _zoomedBorderRight : _zoomedBorderLeft;
        
        if (zoomedImage is null || zoomedBorder is null || vm.CurrentPageImage is null)
        {
            return;
        }
        
        // Get the region to display (normalized 0-1 coordinates relative to the image)
        double regionX, regionY, regionWidth, regionHeight;
        
        if (vm.IsGuidedMode && vm.CurrentPanel is not null)
        {
            // In guided mode, use the current panel bounds
            var panel = vm.CurrentPanel;
            regionX = panel.X;
            regionY = panel.Y;
            regionWidth = panel.Width;
            regionHeight = panel.Height;
        }
        else if (vm.ReadingMode == ReadingMode.Zoomed)
        {
            // In zoomed mode, use the zoom region bounds
            var bounds = vm.ZoomRegion.GetBounds();
            regionX = bounds.Left;
            regionY = bounds.Top;
            regionWidth = bounds.Right - bounds.Left;
            regionHeight = bounds.Bottom - bounds.Top;
        }
        else
        {
            // Normal mode - show full image
            zoomedImage.RenderTransform = null;
            zoomedImage.Clip = null;
            return;
        }
        
        // Ensure we have valid region dimensions
        if (regionWidth <= 0 || regionHeight <= 0)
        {
            return;
        }
        
        // Get actual image pixel dimensions
        var imagePixelWidth = (double)vm.CurrentPageImage.PixelSize.Width;
        var imagePixelHeight = (double)vm.CurrentPageImage.PixelSize.Height;
        
        // Validate image dimensions
        if (imagePixelWidth <= 0 || imagePixelHeight <= 0)
        {
            return;
        }
        
        // Get the container (border) dimensions
        var containerWidth = zoomedBorder.Bounds.Width;
        var containerHeight = zoomedBorder.Bounds.Height;
        
        if (containerWidth <= 0 || containerHeight <= 0)
        {
            return;
        }
        
        // Calculate how the image with Stretch="Uniform" fits in the container (before any transform)
        var imageAspect = imagePixelWidth / imagePixelHeight;
        var containerAspect = containerWidth / containerHeight;
        
        double baseImageWidth, baseImageHeight, baseImageOffsetX, baseImageOffsetY;
        
        if (imageAspect > containerAspect)
        {
            // Image is wider relative to container - fit to width
            baseImageWidth = containerWidth;
            baseImageHeight = containerWidth / imageAspect;
            baseImageOffsetX = 0;
            baseImageOffsetY = (containerHeight - baseImageHeight) / 2;
        }
        else
        {
            // Image is taller relative to container - fit to height
            baseImageHeight = containerHeight;
            baseImageWidth = containerHeight * imageAspect;
            baseImageOffsetX = (containerWidth - baseImageWidth) / 2;
            baseImageOffsetY = 0;
        }
        
        // Validate base image dimensions
        if (baseImageWidth <= 0 || baseImageHeight <= 0)
        {
            return;
        }
        
        // Calculate the aspect ratio of the region (in actual pixels)
        var regionPixelWidth = regionWidth * imagePixelWidth;
        var regionPixelHeight = regionHeight * imagePixelHeight;
        var regionAspect = regionPixelWidth / regionPixelHeight;
        
        // Calculate scale factor to enlarge the region to fill the container
        double scale;
        if (regionAspect > containerAspect)
        {
            // Region is wider than container - fit to width
            scale = containerWidth / (regionWidth * baseImageWidth);
        }
        else
        {
            // Region is taller than container - fit to height
            scale = containerHeight / (regionHeight * baseImageHeight);
        }
        
        // Calculate the region position in the base image coordinate space
        // The region is at (regionX, regionY) normalized, which maps to:
        var regionLeftInImage = regionX * baseImageWidth;
        var regionTopInImage = regionY * baseImageHeight;
        var regionDisplayWidth = regionWidth * baseImageWidth;
        var regionDisplayHeight = regionHeight * baseImageHeight;
        
        // Calculate the center of the region in the base image (relative to image top-left, not container)
        var regionCenterInImageX = regionLeftInImage + regionDisplayWidth / 2;
        var regionCenterInImageY = regionTopInImage + regionDisplayHeight / 2;
        
        // After scaling around origin (0,0), the image offset and region center both scale
        var scaledImageOffsetX = baseImageOffsetX * scale;
        var scaledImageOffsetY = baseImageOffsetY * scale;
        var scaledRegionCenterX = regionCenterInImageX * scale + scaledImageOffsetX;
        var scaledRegionCenterY = regionCenterInImageY * scale + scaledImageOffsetY;
        
        // Translate to put the scaled region center at the container center
        var translateX = (containerWidth / 2) - scaledRegionCenterX;
        var translateY = (containerHeight / 2) - scaledRegionCenterY;
        
        // Apply the combined transform
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new ScaleTransform(scale, scale));
        transformGroup.Children.Add(new TranslateTransform(translateX, translateY));
        
        zoomedImage.RenderTransform = transformGroup;
        zoomedImage.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        
        // Don't use clip - the border's ClipToBounds handles clipping
        zoomedImage.Clip = null;
    }
    
    /// <summary>
    /// Get the bounds of the image within its container
    /// </summary>
    private static Rect GetImageBoundsInContainer(Image image)
    {
        var containerBounds = image.Bounds;
        if (image.Source is not Avalonia.Media.Imaging.Bitmap bitmap)
        {
            return containerBounds;
        }
        
        var imageAspect = (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
        var containerAspect = containerBounds.Width / containerBounds.Height;
        
        double displayWidth, displayHeight;
        
        if (imageAspect > containerAspect)
        {
            // Image is wider than container
            displayWidth = containerBounds.Width;
            displayHeight = displayWidth / imageAspect;
        }
        else
        {
            // Image is taller than container
            displayHeight = containerBounds.Height;
            displayWidth = displayHeight * imageAspect;
        }
        
        var x = (containerBounds.Width - displayWidth) / 2;
        var y = (containerBounds.Height - displayHeight) / 2;
        
        return new Rect(x, y, displayWidth, displayHeight);
    }
    
    /// <summary>
    /// Handle pointer pressed on overview image for zoom region dragging
    /// </summary>
    private void OnOverviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm)
        {
            return;
        }
        
        var position = e.GetPosition(sender as Control);
        var image = vm.IsOverviewOnLeft ? _overviewImage : _overviewImageRight;
        if (image is null)
        {
            return;
        }
        
        var imageBounds = GetImageBoundsInContainer(image);
        
        // Convert to normalized coordinates
        var normalizedX = (position.X - imageBounds.X) / imageBounds.Width;
        var normalizedY = (position.Y - imageBounds.Y) / imageBounds.Height;
        
        // Clamp to valid range
        normalizedX = Math.Max(0, Math.Min(1, normalizedX));
        normalizedY = Math.Max(0, Math.Min(1, normalizedY));
        
        // In guided mode, check if a panel was clicked
        if (vm.IsGuidedMode && vm.CurrentPagePanels is not null)
        {
            for (var i = 0; i < vm.CurrentPagePanels.Panels.Count; i++)
            {
                var panel = vm.CurrentPagePanels.Panels[i];
                if (normalizedX >= panel.X && normalizedX <= panel.X + panel.Width &&
                    normalizedY >= panel.Y && normalizedY <= panel.Y + panel.Height)
                {
                    vm.SelectPanelCommand.Execute(i);
                    return;
                }
            }
        }
        
        // In zoomed mode, start dragging zoom region
        if (vm.ReadingMode == ReadingMode.Zoomed)
        {
            _isDraggingZoomRegion = true;
            _zoomRegionDragStart = position;
            
            // Move zoom region center to click position
            vm.ZoomRegion.CenterX = normalizedX;
            vm.ZoomRegion.CenterY = normalizedY;
            vm.MoveZoomRegion(0, 0); // Trigger update
        }
    }
    
    /// <summary>
    /// Handle pointer moved on overview image for zoom region dragging
    /// </summary>
    private void OnOverviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm || !_isDraggingZoomRegion)
        {
            return;
        }
        
        if (vm.ReadingMode != ReadingMode.Zoomed)
        {
            return;
        }
        
        var position = e.GetPosition(sender as Control);
        var image = vm.IsOverviewOnLeft ? _overviewImage : _overviewImageRight;
        if (image is null)
        {
            return;
        }
        
        var imageBounds = GetImageBoundsInContainer(image);
        
        // Convert to normalized coordinates
        var normalizedX = (position.X - imageBounds.X) / imageBounds.Width;
        var normalizedY = (position.Y - imageBounds.Y) / imageBounds.Height;
        
        // Clamp to valid range
        normalizedX = Math.Max(0, Math.Min(1, normalizedX));
        normalizedY = Math.Max(0, Math.Min(1, normalizedY));
        
        // Update zoom region center
        vm.ZoomRegion.CenterX = normalizedX;
        vm.ZoomRegion.CenterY = normalizedY;
        vm.MoveZoomRegion(0, 0); // Trigger update
    }
    
    /// <summary>
    /// Handle pointer released on overview image
    /// </summary>
    private void OnOverviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDraggingZoomRegion = false;
    }
    
    /// <summary>
    /// Handle pointer pressed on zoomed area for navigation
    /// </summary>
    private void OnZoomedAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Currently just storing the start point for potential swipe
        if (DataContext is ReaderViewModel vm)
        {
            _swipeStartPoint = e.GetPosition(sender as Control);
            _swipeStartTime = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Handle pointer moved on zoomed area
    /// </summary>
    private void OnZoomedAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        // Could be used for panning within the zoomed area
    }
    
    /// <summary>
    /// Handle pointer released on zoomed area for tap/swipe handling
    /// </summary>
    private void OnZoomedAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm || !_swipeStartPoint.HasValue)
        {
            return;
        }
        
        var position = e.GetPosition(sender as Control);
        var elapsed = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
        var deltaX = position.X - _swipeStartPoint.Value.X;
        var deltaY = position.Y - _swipeStartPoint.Value.Y;
        
        // Check for swipe
        if (elapsed < SwipeMaxTimeMs && 
            Math.Abs(deltaX) > SwipeThreshold && 
            Math.Abs(deltaY) < SwipeMaxVerticalDeviation)
        {
            if (vm.IsGuidedMode)
            {
                if (deltaX > 0)
                {
                    vm.GoToPreviousPanelCommand.Execute(null);
                }
                else
                {
                    vm.GoToNextPanelCommand.Execute(null);
                }
            }
            else
            {
                if (deltaX > 0)
                {
                    vm.GoToPreviousPageCommand.Execute(null);
                }
                else
                {
                    vm.GoToNextPageCommand.Execute(null);
                }
            }
        }
        else
        {
            // Tap - toggle controls
            vm.ToggleControlsCommand.Execute(null);
        }
        
        _swipeStartPoint = null;
    }
    
    #endregion
}
