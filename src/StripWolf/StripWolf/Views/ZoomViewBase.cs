using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StripWolf.ViewModels;
using StripWolf.Models;

namespace StripWolf.Views;

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

    private bool _isDraggingOverview;
    private bool _isDrawingManualRegion;
    private Point _manualDrawStart;
    
    private bool _isPanningZoomArea;
    private Point _lastPointerPosition;
    
    private Point? _swipeStartPoint;
    private DateTime _swipeStartTime;
    private const double SwipeThreshold = 80;
    private const double SwipeMaxTimeMs = 500;

    private readonly Dictionary<long, Point> _touchPoints = new();
    private double _initialDistance = 0;
    private double _initialZoomRegionSize = 1.0;

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
        _isDraggingOverview = false;
        _isDrawingManualRegion = false;
        _isPanningZoomArea = false;
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
            e.PropertyName == nameof(ReaderViewModel.Handedness) ||
            e.PropertyName == nameof(ReaderViewModel.CompactOverview))
        {
            Dispatcher.UIThread.Post(UpdateZoomRegion, DispatcherPriority.Render);
        }
    }

    protected void UpdateZoomRegion()
    {
        if (DataContext is not ReaderViewModel vm) return;

        var region = vm.ZoomRegion;
        var image = vm.CurrentPageImage;
        if (image == null || image.Size.Width <= 0) return;

        // 1. Manage Overview Column Layout
        var overviewCanvas = vm.IsOverviewOnLeft ? OverviewContainerLeftControl : OverviewContainerRightControl;
        var overviewBorder = overviewCanvas?.Parent?.Parent as Control;
        var columnWrapper = overviewBorder?.Parent as Control;

        if (columnWrapper != null)
        {
            if (vm.CompactOverview)
            {
                double aspect = image.Size.Width / image.Size.Height;
                double availableHeight = Bounds.Height;
                if (availableHeight > 0)
                {
                    columnWrapper.Width = availableHeight * aspect;
                }
            }
            else
            {
                columnWrapper.ClearValue(WidthProperty);
            }
        }

        // 2. Set Canvas sizes to image pixels
        var overviewContainers = new[] { OverviewContainerLeftControl, OverviewContainerRightControl };
        var overviewCanvases = new[] { OverviewCanvasLeftControl, OverviewCanvasRightControl };
        for (int i = 0; i < 2; i++)
        {
            if (overviewContainers[i] != null) { overviewContainers[i]!.Width = image.Size.Width; overviewContainers[i]!.Height = image.Size.Height; }
            if (overviewCanvases[i] != null) { overviewCanvases[i]!.Width = image.Size.Width; overviewCanvases[i]!.Height = image.Size.Height; }
        }

        // 3. Robust Zoom Math using Fixed Virtual Viewport
        var viewbox = vm.IsOverviewOnLeft ? ZoomedViewboxRightControl : ZoomedViewboxLeftControl;
        var canvas = vm.IsOverviewOnLeft ? ZoomedCanvasRightControl : ZoomedCanvasLeftControl;
        var areaImage = vm.IsOverviewOnLeft ? ZoomedAreaImageRightControl : ZoomedAreaImageLeftControl;
        
        if (viewbox != null && canvas != null && areaImage != null)
        {
            // Use the parent border for more stable bounds
            var zoomBorder = viewbox.Parent as Control;
            double targetWidth = zoomBorder?.Bounds.Width ?? (Bounds.Width / 2);
            double targetHeight = zoomBorder?.Bounds.Height ?? Bounds.Height;
            
            if (targetWidth <= 0 || targetHeight <= 0) 
            {
                targetWidth = 1000;
                targetHeight = 1000;
            }

            double targetAspect = targetWidth / targetHeight;
            
            // Fixed virtual coordinate system for the canvas window
            canvas.Width = 2000;
            canvas.Height = 2000 / targetAspect;

            // Magnification: scale the image so the selected region fills the window
            double scaleX = canvas.Width / (region.Width * image.Size.Width);
            double scaleY = canvas.Height / (region.Height * image.Size.Height);
            double scale = Math.Min(scaleX, scaleY);

            double displayImgWidth = image.Size.Width * scale;
            double displayImgHeight = image.Size.Height * scale;

            areaImage.Width = displayImgWidth;
            areaImage.Height = displayImgHeight;

            // Position image so the requested region center is at canvas center
            Canvas.SetLeft(areaImage, (canvas.Width / 2) - (region.CenterX * displayImgWidth));
            Canvas.SetTop(areaImage, (canvas.Height / 2) - (region.CenterY * displayImgHeight));

            // Track what portion of the image is actually visible in the window for RedrawOverview
            _actualDisplayWidthNormalized = canvas.Width / displayImgWidth;
            _actualDisplayHeightNormalized = canvas.Height / displayImgHeight;
        }
        
        RedrawOverview();
    }

    protected abstract void RedrawOverview();

    private void OnOverviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;
        var canvas = sender as Canvas;
        if (canvas == null) return;

        var pos = e.GetPosition(canvas);
        double nx = pos.X / canvas.Width;
        double ny = pos.Y / canvas.Height;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            _isDrawingManualRegion = true;
            _manualDrawStart = pos;
            e.Pointer.Capture(canvas);
            return;
        }

        if (HandleOverviewClick(vm, nx, ny)) return;

        _isDraggingOverview = true;
        vm.ZoomRegion.CenterX = Math.Max(0, Math.Min(1, nx));
        vm.ZoomRegion.CenterY = Math.Max(0, Math.Min(1, ny));
        vm.MoveZoomRegion(0, 0);
        e.Pointer.Capture(canvas);
    }

    protected virtual bool HandleOverviewClick(ReaderViewModel vm, double x, double y) => false;

    private void OnOverviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;
        var canvas = sender as Canvas;
        if (canvas == null) return;

        var pos = e.GetPosition(canvas);
        double nx = Math.Max(0, Math.Min(1, pos.X / canvas.Width));
        double ny = Math.Max(0, Math.Min(1, pos.Y / canvas.Height));

        if (_isDrawingManualRegion)
        {
            double x1 = Math.Min(_manualDrawStart.X, pos.X) / canvas.Width;
            double y1 = Math.Min(_manualDrawStart.Y, pos.Y) / canvas.Height;
            double x2 = Math.Max(_manualDrawStart.X, pos.X) / canvas.Width;
            double y2 = Math.Max(_manualDrawStart.Y, pos.Y) / canvas.Height;

            vm.ZoomRegion.CenterX = (x1 + x2) / 2;
            vm.ZoomRegion.CenterY = (y1 + y2) / 2;
            vm.ZoomRegion.Width = Math.Max(ZoomRegion.MinSize, x2 - x1);
            vm.ZoomRegion.Height = Math.Max(ZoomRegion.MinSize, y2 - y1);
            vm.MoveZoomRegion(0, 0);
        }
        else if (_isDraggingOverview)
        {
            vm.ZoomRegion.CenterX = nx;
            vm.ZoomRegion.CenterY = ny;
            vm.MoveZoomRegion(0, 0);
        }
    }

    private void OnOverviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDraggingOverview = false;
        _isDrawingManualRegion = false;
        if (sender is Control c) e.Pointer.Capture(null);
    }

    private void OnZoomedAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;

        _lastPointerPosition = e.GetPosition(this);

        if (e.Pointer.Type == PointerType.Touch)
        {
            _touchPoints[e.Pointer.Id] = _lastPointerPosition;
            if (_touchPoints.Count == 2)
            {
                var points = _touchPoints.Values.ToArray();
                _initialDistance = GetDistance(points[0], points[1]);
                _initialZoomRegionSize = vm.ZoomRegion.Size;
                return;
            }
        }

        _swipeStartPoint = _lastPointerPosition;
        _swipeStartTime = DateTime.UtcNow;
        _isPanningZoomArea = true;
        
        if (sender is Control c) e.Pointer.Capture(c);
    }

    private void OnZoomedAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;
        var currentPosition = e.GetPosition(this);

        if (e.Pointer.Type == PointerType.Touch && _touchPoints.ContainsKey(e.Pointer.Id))
        {
            _touchPoints[e.Pointer.Id] = currentPosition;
            if (_touchPoints.Count == 2)
            {
                var points = _touchPoints.Values.ToArray();
                double currentDistance = GetDistance(points[0], points[1]);
                if (_initialDistance > 0)
                {
                    double scale = currentDistance / _initialDistance;
                    vm.ZoomRegion.Resize(_initialZoomRegionSize / scale - vm.ZoomRegion.Size);
                    vm.MoveZoomRegion(0, 0);
                }
                return;
            }
        }

        if (_isPanningZoomArea && vm.CurrentPageImage != null)
        {
            var delta = currentPosition - _lastPointerPosition;
            if (Math.Abs(delta.X) > 1 || Math.Abs(delta.Y) > 1)
            {
                var viewbox = vm.IsOverviewOnLeft ? ZoomedViewboxRightControl : ZoomedViewboxLeftControl;
                var zoomCanvas = vm.IsOverviewOnLeft ? ZoomedCanvasRightControl : ZoomedCanvasLeftControl;
                if (viewbox != null && zoomCanvas != null)
                {
                    double canvasToImageScale = vm.CurrentPageImage.Size.Width;
                    // Our canvas window is fixed at 2000 height/proportional width.
                    // The viewbox maps viewbox.Bounds to zoomCanvas.Width units.
                    double screenToCanvasScale = viewbox.Bounds.Width / zoomCanvas.Width;
                    
                    double normalizedDeltaX = delta.X / (screenToCanvasScale * (zoomCanvas.Width / _actualDisplayWidthNormalized));
                    double normalizedDeltaY = delta.Y / (screenToCanvasScale * (zoomCanvas.Width / _actualDisplayWidthNormalized));

                    vm.MoveZoomRegion(-normalizedDeltaX, -normalizedDeltaY);
                }
            }
        }
        _lastPointerPosition = currentPosition;
    }

    private void OnZoomedAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _touchPoints.Remove(e.Pointer.Id);
        if (_touchPoints.Count < 2) _initialDistance = 0;
        _isPanningZoomArea = false;
        if (sender is Control c) e.Pointer.Capture(null);

        if (DataContext is not ReaderViewModel vm || !_swipeStartPoint.HasValue || _touchPoints.Count > 0) return;
        
        var position = e.GetPosition(this);
        var elapsed = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
        var deltaX = position.X - _swipeStartPoint.Value.X;
        var deltaY = position.Y - _swipeStartPoint.Value.Y;
        
        if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) > SwipeThreshold && Math.Abs(deltaY) < SwipeThreshold)
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
        else if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) < 10 && Math.Abs(deltaY) < 10)
        {
            vm.ToggleControlsCommand.Execute(null);
        }
        _swipeStartPoint = null;
    }

    private double GetDistance(Point p1, Point p2) => Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
}
