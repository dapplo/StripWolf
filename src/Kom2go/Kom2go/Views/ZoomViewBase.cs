using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Kom2go.ViewModels;
using Kom2go.Models;

namespace Kom2go.Views;

public abstract class ZoomViewBase : UserControl
{
    protected abstract Image? OverviewImageLeftControl { get; }
    protected abstract Canvas? OverviewCanvasLeftControl { get; }
    protected abstract Canvas? OverviewContainerLeftControl { get; }
    protected abstract Image? OverviewImageRightControl { get; }
    protected abstract Canvas? OverviewCanvasRightControl { get; }
    protected abstract Canvas? OverviewContainerRightControl { get; }
    
    protected abstract Viewbox? ZoomedViewboxLeftControl { get; }
    protected abstract Canvas? ZoomedCanvasLeftControl { get; }
    protected abstract Image? ZoomedAreaImageLeftControl { get; }
    
    protected abstract Viewbox? ZoomedViewboxRightControl { get; }
    protected abstract Canvas? ZoomedCanvasRightControl { get; }
    protected abstract Image? ZoomedAreaImageRightControl { get; }

    private bool _isDraggingZoomRegion;
    private Point? _swipeStartPoint;
    private DateTime _swipeStartTime;
    private const double SwipeThreshold = 80;
    private const double SwipeMaxTimeMs = 500;

    // Manual Pinch tracking
    private readonly Dictionary<long, Point> _touchPoints = new();
    private double _initialDistance = 0;
    private double _initialZoomRegionSize = 1.0;

    // Track the actually displayed area for RedrawOverview
    protected double _actualDisplayWidthNormalized = 0.4;
    protected double _actualDisplayHeightNormalized = 0.4;

    public ZoomViewBase()
    {
        SizeChanged += (s, e) => UpdateZoomRegion();
    }

    protected void InitializeZoomLogic()
    {
        var canvases = new[] { OverviewCanvasLeftControl, OverviewCanvasRightControl };
        foreach (var canvas in canvases)
        {
            if (canvas != null)
            {
                canvas.PointerPressed += OnOverviewPointerPressed;
                canvas.PointerMoved += OnOverviewPointerMoved;
                canvas.PointerReleased += OnOverviewPointerReleased;
                canvas.PointerCaptureLost += OnPointerCaptureLost;
                canvas.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
            }
        }

        var viewboxes = new[] { ZoomedViewboxLeftControl, ZoomedViewboxRightControl };
        foreach (var viewbox in viewboxes)
        {
            if (viewbox != null)
            {
                viewbox.PointerPressed += OnZoomedAreaPointerPressed;
                viewbox.PointerMoved += OnZoomedAreaPointerMoved;
                viewbox.PointerReleased += OnZoomedAreaPointerReleased;
                viewbox.PointerCaptureLost += OnPointerCaptureLost;
                viewbox.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
            }
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDraggingZoomRegion = false;
        _touchPoints.Clear();
        _initialDistance = 0;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            if (e.Delta.Y == 0) return;

            if (e.Delta.Y > 0) vm.DecreaseZoomRegionSizeCommand.Execute(null);
            else vm.IncreaseZoomRegionSizeCommand.Execute(null);
        }
        else
        {
            if (e.Delta.Y == 0) return;
            e.Handled = true;
            
            if (vm.IsGuidedMode)
            {
                if (e.Delta.Y > 0 && vm.HasPreviousPanel) vm.GoToPreviousPanelCommand.Execute(null);
                else if (e.Delta.Y < 0 && vm.HasNextPanel) vm.GoToNextPanelCommand.Execute(null);
            }
            else
            {
                if (e.Delta.Y > 0 && vm.HasPreviousPage) vm.GoToPreviousPageCommand.Execute(null);
                else if (e.Delta.Y < 0 && vm.HasNextPage) vm.GoToNextPageCommand.Execute(null);
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ReaderViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateZoomRegion();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.ZoomRegion) || 
            e.PropertyName == nameof(ReaderViewModel.CurrentPageImage) ||
            e.PropertyName == nameof(ReaderViewModel.CurrentPanel) ||
            e.PropertyName == nameof(ReaderViewModel.Handedness))
        {
            UpdateZoomRegion();
        }
    }

    protected void UpdateZoomRegion()
    {
        if (DataContext is not ReaderViewModel vm) return;

        var region = vm.ZoomRegion;
        var image = vm.CurrentPageImage;
        if (image == null) return;

        var overviewContainers = new[] { OverviewContainerLeftControl, OverviewContainerRightControl };
        var overviewImages = new[] { OverviewImageLeftControl, OverviewImageRightControl };
        var overviewCanvases = new[] { OverviewCanvasLeftControl, OverviewCanvasRightControl };

        for (int i = 0; i < 2; i++)
        {
            if (overviewContainers[i] != null)
            {
                overviewContainers[i]!.Width = image.Size.Width;
                overviewContainers[i]!.Height = image.Size.Height;
            }
            if (overviewImages[i] != null)
            {
                overviewImages[i]!.Width = image.Size.Width;
                overviewImages[i]!.Height = image.Size.Height;
            }
            if (overviewCanvases[i] != null)
            {
                overviewCanvases[i]!.Width = image.Size.Width;
                overviewCanvases[i]!.Height = image.Size.Height;
            }
        }

        var viewbox = vm.IsOverviewOnLeft ? ZoomedViewboxRightControl : ZoomedViewboxLeftControl;
        var canvas = vm.IsOverviewOnLeft ? ZoomedCanvasRightControl : ZoomedCanvasLeftControl;
        var areaImage = vm.IsOverviewOnLeft ? ZoomedAreaImageRightControl : ZoomedAreaImageLeftControl;
        
        if (viewbox != null && canvas != null && areaImage != null)
        {
            double targetWidth = viewbox.Bounds.Width;
            double targetHeight = viewbox.Bounds.Height;
            
            if (targetWidth <= 0 || targetHeight <= 0)
            {
                targetWidth = Bounds.Width / 2;
                targetHeight = Bounds.Height;
            }

            if (targetWidth > 0 && targetHeight > 0)
            {
                double targetAspect = targetWidth / targetHeight;
                
                // Pixel size of the requested region
                double wantPixelWidth = region.Width * image.Size.Width;
                double wantPixelHeight = region.Height * image.Size.Height;
                
                // Ensure non-zero
                wantPixelWidth = Math.Max(1, wantPixelWidth);
                wantPixelHeight = Math.Max(1, wantPixelHeight);
                
                double wantAspect = wantPixelWidth / wantPixelHeight;
                double displayPixelWidth, displayPixelHeight;

                if (wantAspect < targetAspect)
                {
                    // Panel is narrower than the display area -> expand width to fill
                    displayPixelHeight = wantPixelHeight;
                    displayPixelWidth = wantPixelHeight * targetAspect;
                }
                else
                {
                    // Panel is wider than the display area -> expand height to fill
                    displayPixelWidth = wantPixelWidth;
                    displayPixelHeight = wantPixelWidth / targetAspect;
                }

                _actualDisplayWidthNormalized = displayPixelWidth / image.Size.Width;
                _actualDisplayHeightNormalized = displayPixelHeight / image.Size.Height;

                canvas.Width = displayPixelWidth;
                canvas.Height = displayPixelHeight;
                areaImage.Width = image.Size.Width;
                areaImage.Height = image.Size.Height;

                Canvas.SetLeft(areaImage, (displayPixelWidth / 2) - (region.CenterX * image.Size.Width));
                Canvas.SetTop(areaImage, (displayPixelHeight / 2) - (region.CenterY * image.Size.Height));
            }
        }
        
        RedrawOverview();
    }

    protected abstract void RedrawOverview();

    private void OnOverviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;
        
        var canvas = sender as Canvas;
        if (canvas == null) return;

        var position = e.GetPosition(canvas);
        
        var normalizedX = position.X / canvas.Width;
        var normalizedY = position.Y / canvas.Height;
        
        normalizedX = Math.Max(0, Math.Min(1, normalizedX));
        normalizedY = Math.Max(0, Math.Min(1, normalizedY));
        
        if (HandleOverviewClick(vm, normalizedX, normalizedY)) return;

        _isDraggingZoomRegion = true;
        vm.ZoomRegion.CenterX = normalizedX;
        vm.ZoomRegion.CenterY = normalizedY;
        vm.MoveZoomRegion(0, 0);
    }

    protected virtual bool HandleOverviewClick(ReaderViewModel vm, double x, double y) => false;

    private void OnOverviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm || !_isDraggingZoomRegion) return;
        
        var canvas = sender as Canvas;
        if (canvas == null) return;

        var position = e.GetPosition(canvas);
        
        var normalizedX = position.X / canvas.Width;
        var normalizedY = position.Y / canvas.Height;
        
        vm.ZoomRegion.CenterX = Math.Max(0, Math.Min(1, normalizedX));
        vm.ZoomRegion.CenterY = Math.Max(0, Math.Min(1, normalizedY));
        vm.MoveZoomRegion(0, 0);
    }

    private void OnOverviewPointerReleased(object? sender, PointerReleasedEventArgs e) => _isDraggingZoomRegion = false;

    private void OnZoomedAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;

        if (e.Pointer.Type == PointerType.Touch)
        {
            _touchPoints[e.Pointer.Id] = e.GetPosition(this);
            if (_touchPoints.Count == 2)
            {
                var points = _touchPoints.Values.ToArray();
                _initialDistance = GetDistance(points[0], points[1]);
                _initialZoomRegionSize = vm.ZoomRegion.Size;
                return;
            }
        }

        _swipeStartPoint = e.GetPosition(this);
        _swipeStartTime = DateTime.UtcNow;
    }

    private void OnZoomedAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;

        if (e.Pointer.Type == PointerType.Touch && _touchPoints.ContainsKey(e.Pointer.Id))
        {
            _touchPoints[e.Pointer.Id] = e.GetPosition(this);
            if (_touchPoints.Count == 2)
            {
                var points = _touchPoints.Values.ToArray();
                double currentDistance = GetDistance(points[0], points[1]);
                if (_initialDistance > 0)
                {
                    double scale = currentDistance / _initialDistance;
                    double targetSize = _initialZoomRegionSize / scale;
                    vm.ZoomRegion.Size = Math.Max(0.05, Math.Min(1.0, targetSize));
                    vm.MoveZoomRegion(0, 0);
                }
                return;
            }
        }
    }

    private void OnZoomedAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _touchPoints.Remove(e.Pointer.Id);
        if (_touchPoints.Count < 2)
        {
            _initialDistance = 0;
        }

        if (DataContext is not ReaderViewModel vm || !_swipeStartPoint.HasValue || _touchPoints.Count > 0) return;
        
        var position = e.GetPosition(this);
        var elapsed = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
        var deltaX = position.X - _swipeStartPoint.Value.X;
        
        if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) > SwipeThreshold)
        {
            if (deltaX > 0)
            {
                if (vm.IsGuidedMode) vm.GoToPreviousPanelCommand.Execute(null);
                else vm.GoToPreviousPageCommand.Execute(null);
            }
            else
            {
                if (vm.IsGuidedMode) vm.GoToNextPanelCommand.Execute(null);
                else vm.GoToNextPageCommand.Execute(null);
            }
        }
        else if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) < 10)
        {
            vm.ToggleControlsCommand.Execute(null);
        }
        _swipeStartPoint = null;
    }

    private double GetDistance(Point p1, Point p2)
    {
        return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }
}
