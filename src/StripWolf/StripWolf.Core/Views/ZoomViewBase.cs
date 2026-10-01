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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StripWolf.Core.ViewModels;
using StripWolf.Core.Models;

namespace StripWolf.Core.Views;

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
    private double _swipeStartCenterX;
    private double _swipeStartCenterY;
    private const double SwipeThreshold = 80;
    private const double SwipeMaxTimeMs = 500;
    private const double TapMaxMovement = 10;
    // A drag that actually moved the zoom region by more than this (normalized) is a pan, not a page swipe
    private const double PanMovementEpsilon = 0.002;

    private readonly Dictionary<long, (Point Position, IPointer Pointer)> _touchPoints = new();
    private double _initialDistance = 0;
    private double _initialZoomRegionSize = 1.0;
    private bool _isPinching;

    // Inertia/Flick fields
    private Vector _panVelocity;
    private DateTime _lastPanTime;
    private Point _lastPanPosition;
    private DispatcherTimer? _inertiaTimer;
    private Vector _inertiaVelocity;
    private DateTime _lastInertiaTick;

    private ReaderViewModel? _subscribedViewModel;
    private bool _zoomRegionUpdatePending;

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

    private void ResetGestureState()
    {
        _isDraggingOverview = false;
        _isDrawingManualRegion = false;
        _isPanningZoomArea = false;
        _isPinching = false;
        _swipeStartPoint = null;
        _touchPoints.Clear();
        _initialDistance = 0;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Only drop the pointer that lost capture. Releasing all other pointers here (as before) raised
        // re-entrant capture-lost events and could break an ongoing pinch.
        _touchPoints.Remove(e.Pointer.Id);
        if (_touchPoints.Count < 2)
        {
            _initialDistance = 0;
        }

        if (_touchPoints.Count == 0)
        {
            _isDraggingOverview = false;
            _isDrawingManualRegion = false;
            _isPanningZoomArea = false;
            _isPinching = false;
            _swipeStartPoint = null;
        }
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

        // Unsubscribe from the previous view model, otherwise every DataContext change adds another
        // handler (and keeps this view alive through the view model).
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is ReaderViewModel vm)
        {
            _subscribedViewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateZoomRegion();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                // Updates are skipped while hidden, so catch up now
                ScheduleZoomRegionUpdate();
            }
            else
            {
                StopInertia();
                ResetGestureState();
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopInertia();
        ResetGestureState();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.ZoomRegion) || 
            e.PropertyName == nameof(ReaderViewModel.CurrentPageImage) ||
            e.PropertyName == nameof(ReaderViewModel.CurrentPanel) ||
            e.PropertyName == nameof(ReaderViewModel.CurrentPagePanels) ||
            e.PropertyName == nameof(ReaderViewModel.Handedness) ||
            e.PropertyName == nameof(ReaderViewModel.CompactOverview))
        {
            ScheduleZoomRegionUpdate();
        }
    }

    /// <summary>
    /// Coalesce update requests: a pan can raise dozens of ZoomRegion changes per frame, we only need one layout update.
    /// </summary>
    private void ScheduleZoomRegionUpdate()
    {
        if (_zoomRegionUpdatePending || !IsVisible) return;
        _zoomRegionUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _zoomRegionUpdatePending = false;
            UpdateZoomRegion();
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// Convert a movement in screen (DIP) coordinates on the zoomed area into a normalized image delta.
    /// </summary>
    private bool TryScreenDeltaToNormalized(ReaderViewModel vm, Vector delta, out double normalizedX, out double normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;
        var viewbox = vm.IsOverviewOnLeft ? ZoomedViewboxRightControl : ZoomedViewboxLeftControl;
        var zoomCanvas = vm.IsOverviewOnLeft ? ZoomedCanvasRightControl : ZoomedCanvasLeftControl;
        if (viewbox == null || zoomCanvas == null || zoomCanvas.Width <= 0 || zoomCanvas.Height <= 0 ||
            viewbox.Bounds.Width <= 0 || viewbox.Bounds.Height <= 0 ||
            _actualDisplayWidthNormalized <= 0 || _actualDisplayHeightNormalized <= 0)
        {
            return false;
        }

        // Viewbox uses Stretch=Fill, so X and Y can have different scale factors
        double screenToCanvasScaleX = viewbox.Bounds.Width / zoomCanvas.Width;
        double screenToCanvasScaleY = viewbox.Bounds.Height / zoomCanvas.Height;

        // Displayed image size in canvas units
        double displayImgWidth = zoomCanvas.Width / _actualDisplayWidthNormalized;
        double displayImgHeight = zoomCanvas.Height / _actualDisplayHeightNormalized;

        normalizedX = delta.X / (screenToCanvasScaleX * displayImgWidth);
        // Previously the Y delta was divided by the image *width*, so vertical panning was too fast for portrait pages
        normalizedY = delta.Y / (screenToCanvasScaleY * displayImgHeight);
        return true;
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
            e.Handled = true;
            return;
        }

        if (HandleOverviewClick(vm, nx, ny))
        {
            e.Handled = true;
            return;
        }

        _isDraggingOverview = true;
        vm.ZoomRegion.CenterX = Math.Max(0, Math.Min(1, nx));
        vm.ZoomRegion.CenterY = Math.Max(0, Math.Min(1, ny));
        vm.MoveZoomRegion(0, 0);
        e.Pointer.Capture(canvas);
        e.Handled = true;
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
            e.Handled = true;
        }
        else if (_isDraggingOverview)
        {
            vm.ZoomRegion.CenterX = nx;
            vm.ZoomRegion.CenterY = ny;
            vm.MoveZoomRegion(0, 0);
            e.Handled = true;
        }
    }

    private void OnOverviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingOverview || _isDrawingManualRegion)
        {
            _isDraggingOverview = false;
            _isDrawingManualRegion = false;
            if (sender is Control c) e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnZoomedAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;

        // Stop any current inertia
        StopInertia();

        _lastPointerPosition = e.GetPosition(this);
        _lastPanPosition = _lastPointerPosition;
        _lastPanTime = DateTime.UtcNow;
        _panVelocity = default;

        if (e.Pointer.Type == PointerType.Touch)
        {
            // The primary pointer is the first finger on the screen: nothing else can be down.
            // Drop anything left over from a gesture where we never saw the release (touch cancel, etc.),
            // otherwise every following single-finger touch is treated as the second finger of a pinch.
            if (e.Pointer.IsPrimary)
            {
                ResetGestureState();
            }

            _touchPoints[e.Pointer.Id] = (_lastPointerPosition, e.Pointer);
            if (_touchPoints.Count == 2)
            {
                var points = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(_touchPoints.Values, v => v.Position));
                _initialDistance = GetDistance(points[0], points[1]);
                _initialZoomRegionSize = vm.ZoomRegion.Size;
                _isPinching = true;
                _isPanningZoomArea = false;
                _swipeStartPoint = null;
                if (sender is Control controlPressed)
                {
                    foreach (var tp in _touchPoints.Values)
                    {
                        tp.Pointer.Capture(controlPressed);
                    }
                }
                e.Handled = true;
                return;
            }

            if (_touchPoints.Count > 2)
            {
                // Ignore additional fingers
                e.Handled = true;
                return;
            }
        }

        _swipeStartPoint = _lastPointerPosition;
        _swipeStartTime = DateTime.UtcNow;
        _swipeStartCenterX = vm.ZoomRegion.CenterX;
        _swipeStartCenterY = vm.ZoomRegion.CenterY;
        _isPanningZoomArea = true;
        
        if (sender is Control c)
        {
            e.Pointer.Capture(c);
            e.Handled = true;
        }
    }

    private void OnZoomedAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;
        var currentPosition = e.GetPosition(this);
        var now = DateTime.UtcNow;

        if (e.Pointer.Type == PointerType.Touch && _touchPoints.ContainsKey(e.Pointer.Id))
        {
            _touchPoints[e.Pointer.Id] = (currentPosition, e.Pointer);
            if (_touchPoints.Count >= 2)
            {
                if (_touchPoints.Count == 2)
                {
                    var points = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(_touchPoints.Values, v => v.Position));
                    double currentDistance = GetDistance(points[0], points[1]);
                    if (_initialDistance > 10 && currentDistance > 0)
                    {
                        double scale = currentDistance / _initialDistance;
                        vm.ZoomRegion.SetSize(_initialZoomRegionSize / scale);
                        vm.MoveZoomRegion(0, 0);
                    }
                }
                e.Handled = true;
                return;
            }
        }

        if (_isPanningZoomArea && vm.CurrentPageImage != null)
        {
            var delta = currentPosition - _lastPointerPosition;
            
            // Calculate instantaneous velocity
            var elapsed = (now - _lastPanTime).TotalSeconds;
            if (elapsed > 0)
            {
                var instantaneousVelocity = (currentPosition - _lastPanPosition) / elapsed;
                // Simple EMA (Exponential Moving Average) to smooth velocity
                _panVelocity = _panVelocity * 0.4 + instantaneousVelocity * 0.6;
            }
            _lastPanTime = now;
            _lastPanPosition = currentPosition;

            if (Math.Abs(delta.X) > 0.1 || Math.Abs(delta.Y) > 0.1)
            {
                if (TryScreenDeltaToNormalized(vm, delta, out var normalizedDeltaX, out var normalizedDeltaY))
                {
                    vm.MoveZoomRegion(-normalizedDeltaX, -normalizedDeltaY);
                    e.Handled = true;
                }
            }
        }
        _lastPointerPosition = currentPosition;
    }

    private void OnZoomedAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _touchPoints.Remove(e.Pointer.Id);
        bool wasPinching = _isPinching;
        if (_touchPoints.Count < 2) _initialDistance = 0;
        if (_touchPoints.Count == 0) _isPinching = false;
        
        bool wasPanning = _isPanningZoomArea;
        _isPanningZoomArea = false;

        // Take a copy before releasing the capture: Capture(null) synchronously raises PointerCaptureLost,
        // whose handler clears the gesture state (_swipeStartPoint), which disabled swipe/tap detection below.
        var swipeStartPoint = _swipeStartPoint;
        _swipeStartPoint = null;
        e.Pointer.Capture(null);

        if (wasPinching)
        {
            return;
        }

        if (DataContext is not ReaderViewModel vm || !swipeStartPoint.HasValue || _touchPoints.Count > 0)
        {
            return;
        }
        
        var position = e.GetPosition(this);
        var elapsed = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
        var deltaX = position.X - swipeStartPoint.Value.X;
        var deltaY = position.Y - swipeStartPoint.Value.Y;

        // If the drag actually moved the zoom region it was a pan. Only when the region could not move any further
        // (it is at the edge of the page, or shows the whole width) a fast horizontal drag is a page/panel swipe.
        // Before, every fast pan also flipped the page.
        bool regionMoved = Math.Abs(vm.ZoomRegion.CenterX - _swipeStartCenterX) > PanMovementEpsilon ||
                           Math.Abs(vm.ZoomRegion.CenterY - _swipeStartCenterY) > PanMovementEpsilon;

        // In guided mode a swipe always means "next/previous panel" (the new panel resets the region anyway).
        bool allowSwipe = vm.IsGuidedMode || !regionMoved;

        if (allowSwipe && elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) > SwipeThreshold && Math.Abs(deltaY) < SwipeThreshold)
        {
            // Swiping to the left (finger moves right-to-left) means "forward" for left-to-right reading,
            // mirrored for right-to-left reading, consistent with the normal reading view.
            bool forward = deltaX < 0;
            if (vm.IsRightToLeftNavigation) forward = !forward;

            if (forward)
            {
                if (vm.IsGuidedMode) vm.GoToNextPanelCommand.Execute(null);
                else vm.GoToNextPageCommand.Execute(null);
            }
            else
            {
                if (vm.IsGuidedMode) vm.GoToPreviousPanelCommand.Execute(null);
                else vm.GoToPreviousPageCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) < TapMaxMovement && Math.Abs(deltaY) < TapMaxMovement)
        {
            vm.ToggleControlsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (wasPanning && _panVelocity.Length > 100)
        {
            StartInertia(_panVelocity);
        }
    }

    private void StopInertia()
    {
        _inertiaTimer?.Stop();
        _inertiaVelocity = default;
    }

    /// <summary>
    /// Flick/inertia scrolling, driven by a DispatcherTimer on the UI thread
    /// (previously a Task.Run loop which marshalled every frame back to the UI thread).
    /// </summary>
    private void StartInertia(Vector initialVelocity)
    {
        _inertiaVelocity = initialVelocity;
        _lastInertiaTick = DateTime.UtcNow;
        if (_inertiaTimer == null)
        {
            _inertiaTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _inertiaTimer.Tick += OnInertiaTick;
        }
        _inertiaTimer.Start();
    }

    private void OnInertiaTick(object? sender, EventArgs e)
    {
        const double frictionPerFrame = 0.95; // deceleration per 16ms frame
        var now = DateTime.UtcNow;
        double frameSeconds = Math.Clamp((now - _lastInertiaTick).TotalSeconds, 0.001, 0.1);
        _lastInertiaTick = now;

        if (_inertiaVelocity.Length <= 20 || DataContext is not ReaderViewModel vm || !IsVisible)
        {
            StopInertia();
            return;
        }

        var frameDelta = _inertiaVelocity * frameSeconds;
        if (!TryScreenDeltaToNormalized(vm, frameDelta, out var normalizedDeltaX, out var normalizedDeltaY))
        {
            StopInertia();
            return;
        }

        vm.MoveZoomRegion(-normalizedDeltaX, -normalizedDeltaY);
        _inertiaVelocity *= Math.Pow(frictionPerFrame, frameSeconds / 0.016);
    }

    private double GetDistance(Point p1, Point p2) => Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
}
