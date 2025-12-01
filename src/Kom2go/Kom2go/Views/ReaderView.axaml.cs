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
    private Viewbox? _imageViewbox;
    
    // Swipe gesture tracking
    private Point? _swipeStartPoint;
    private DateTime _swipeStartTime;
    private const double SwipeThreshold = 80; // Minimum distance for a swipe (in pixels)
    private const double SwipeMaxTimeMs = 500; // Maximum time in milliseconds for a swipe
    private const double SwipeMaxVerticalDeviation = 100; // Maximum vertical deviation allowed
    
    // Pinch zoom tracking (for multi-touch)
    private readonly Dictionary<long, Point> _activePointers = new();
    private double _initialPinchDistance;
    private double _initialZoomLevel;

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
                vm.AdjustZoom(e.Delta.Y);
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
        _imageViewbox = this.FindControl<Viewbox>("ImageViewbox");
        
        // Focus this control to receive keyboard events
        Focus();
        
        // Update image stretch based on stretch mode
        UpdateImageStretch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // Clear cached references
        _pageImage = null;
        _imageScroller = null;
        _imageViewbox = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is ReaderViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ReaderViewModel.StretchMode))
                {
                    UpdateImageStretch();
                }
            };
        }
    }

    private void UpdateImageStretch()
    {
        if (DataContext is not ReaderViewModel vm || _imageViewbox is null || _imageScroller is null)
        {
            return;
        }

        // Cache bounds to avoid repeated property access
        var scrollerBounds = _imageScroller.Bounds;
        var scrollerWidth = scrollerBounds.Width;
        var scrollerHeight = scrollerBounds.Height;

        // Configure the Viewbox stretch to match the desired mode
        switch (vm.StretchMode)
        {
            case StretchMode.FitPage:
                // Fit the entire page within the viewport (both width and height)
                _imageViewbox.Stretch = Stretch.Uniform;
                _imageViewbox.StretchDirection = StretchDirection.Both;
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                _imageViewbox.MaxWidth = double.PositiveInfinity;
                _imageViewbox.MaxHeight = double.PositiveInfinity;
                break;
                
            case StretchMode.FitWidth:
                // Fit to viewport width, allow vertical scrolling
                _imageViewbox.Stretch = Stretch.Uniform;
                _imageViewbox.StretchDirection = StretchDirection.Both;
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                // Force width-based sizing - set viewbox width constraint
                _imageViewbox.MaxWidth = scrollerWidth > 0 ? scrollerWidth : double.PositiveInfinity;
                _imageViewbox.MaxHeight = double.PositiveInfinity;
                break;
                
            case StretchMode.FitHeight:
                // Fit to viewport height, allow horizontal scrolling
                _imageViewbox.Stretch = Stretch.Uniform;
                _imageViewbox.StretchDirection = StretchDirection.Both;
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                // Force height-based sizing - set viewbox height constraint
                _imageViewbox.MaxWidth = double.PositiveInfinity;
                _imageViewbox.MaxHeight = scrollerHeight > 0 ? scrollerHeight : double.PositiveInfinity;
                break;
                
            case StretchMode.Original:
                // Original size (100%), allow scrolling in both directions
                _imageViewbox.Stretch = Stretch.None;
                _imageViewbox.StretchDirection = StretchDirection.DownOnly;
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                _imageViewbox.MaxWidth = double.PositiveInfinity;
                _imageViewbox.MaxHeight = double.PositiveInfinity;
                break;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Re-apply stretch mode when size changes
        UpdateImageStretch();
    }
    
    #region Gesture Handling (Swipe and Pinch)
    
    /// <summary>
    /// Handle pointer pressed for swipe and pinch gesture tracking
    /// </summary>
    private void OnGesturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        var pointerId = (long)e.Pointer.Id;
        var position = pointer.Position;
        
        _activePointers[pointerId] = position;
        
        if (_activePointers.Count == 1)
        {
            // Single touch - start tracking for potential swipe
            _swipeStartPoint = position;
            _swipeStartTime = DateTime.UtcNow;
        }
        else if (_activePointers.Count == 2 && DataContext is ReaderViewModel vm)
        {
            // Two touches - start tracking for pinch zoom
            var points = _activePointers.Values.ToArray();
            _initialPinchDistance = GetDistance(points[0], points[1]);
            _initialZoomLevel = vm.ZoomLevel;
        }
    }
    
    /// <summary>
    /// Handle pointer moved for pinch zoom detection
    /// </summary>
    private void OnGesturePointerMoved(object? sender, PointerEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        var pointerId = (long)e.Pointer.Id;
        var position = pointer.Position;
        
        if (_activePointers.ContainsKey(pointerId))
        {
            _activePointers[pointerId] = position;
            
            // Handle pinch zoom with two fingers
            if (_activePointers.Count == 2 && DataContext is ReaderViewModel vm && _initialPinchDistance > 0)
            {
                var points = _activePointers.Values.ToArray();
                var currentDistance = GetDistance(points[0], points[1]);
                
                // Calculate scale factor
                var scale = currentDistance / _initialPinchDistance;
                var newZoom = _initialZoomLevel * scale;
                
                // Clamp zoom level between 0.25 and 5.0
                newZoom = Math.Max(0.25, Math.Min(5.0, newZoom));
                
                vm.ZoomLevel = newZoom;
            }
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
        if (_activePointers.Count == 1 && _swipeStartPoint.HasValue && DataContext is ReaderViewModel vm)
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
            if (!wasSwipe && _activePointers.Count == 1)
            {
                HandleTap(position, vm);
                e.Handled = true;
            }
        }
        
        // Clean up tracking
        _activePointers.Remove(pointerId);
        
        if (_activePointers.Count == 0)
        {
            _swipeStartPoint = null;
            _initialPinchDistance = 0;
        }
        else if (_activePointers.Count == 1)
        {
            // Reset for potential new swipe
            _swipeStartPoint = _activePointers.Values.First();
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
