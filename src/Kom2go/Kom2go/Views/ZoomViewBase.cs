using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Kom2go.ViewModels;

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
                // Tunneling event to intercept before children/scrollers
                canvas.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
            }
        }

        var viewboxes = new[] { ZoomedViewboxLeftControl, ZoomedViewboxRightControl };
        foreach (var viewbox in viewboxes)
        {
            if (viewbox != null)
            {
                viewbox.PointerPressed += OnZoomedAreaPointerPressed;
                viewbox.PointerReleased += OnZoomedAreaPointerReleased;
                // Tunneling event to intercept before children/scrollers
                viewbox.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
            }
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            if (e.Delta.Y == 0) return;

            // Zooming (resizing the rectangle)
            if (e.Delta.Y > 0) vm.DecreaseZoomRegionSizeCommand.Execute(null);
            else vm.IncreaseZoomRegionSizeCommand.Execute(null);
        }
        else
        {
            if (e.Delta.Y == 0) return;
            e.Handled = true;
            
            // Navigation
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

        // Update Overview containers to match bitmap size
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
            double regionWidth = image.Size.Width * region.Size;
            double regionHeight = image.Size.Height * region.Size;
            
            canvas.Width = regionWidth;
            canvas.Height = regionHeight;
            areaImage.Width = image.Size.Width;
            areaImage.Height = image.Size.Height;

            Canvas.SetLeft(areaImage, (regionWidth / 2) - (region.CenterX * image.Size.Width));
            Canvas.SetTop(areaImage, (regionHeight / 2) - (region.CenterY * image.Size.Height));
        }
        
        RedrawOverview();
    }

    protected abstract void RedrawOverview();

    private void OnOverviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;
        
        var canvas = sender as Canvas;
        if (canvas == null) return;

        // Position is now naturally in bitmap DIPs because the canvas size matches the bitmap size
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
        _swipeStartPoint = e.GetPosition(this);
        _swipeStartTime = DateTime.UtcNow;
    }

    private void OnZoomedAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm || !_swipeStartPoint.HasValue) return;
        
        var position = e.GetPosition(this);
        var elapsed = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
        var deltaX = position.X - _swipeStartPoint.Value.X;
        
        if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) > SwipeThreshold)
        {
            if (deltaX > 0) vm.GoToPreviousPageCommand.Execute(null);
            else vm.GoToNextPageCommand.Execute(null);
        }
        else if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) < 10)
        {
            vm.ToggleControlsCommand.Execute(null);
        }
        _swipeStartPoint = null;
    }

    protected Rect GetImageContentBounds(Image image)
    {
        // Now simple because we set Width/Height explicitly to match bitmap
        return new Rect(0, 0, image.Width, image.Height);
    }
}
