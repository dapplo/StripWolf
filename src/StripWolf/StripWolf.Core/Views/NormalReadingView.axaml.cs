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
using StripWolf.Core.ViewModels;

namespace StripWolf.Core.Views;

public partial class NormalReadingView : UserControl
{
    private ScrollViewer? _imageScroller;
    private Grid? _imageContainer;
    private Image? _pageImage;
    private Image? _leftPageImage;
    private Image? _rightPageImage;

    // Gesture tracking
    private Point? _swipeStartPoint;
    private DateTime _swipeStartTime;
    private Vector _swipeStartOffset;
    private const double SwipeThreshold = 80;
    private const double SwipeMaxTimeMs = 500;
    private const double SwipeMaxVerticalDeviation = 100;
    private const double TapMaxMovement = 10;

    // Manual Pinch tracking
    private readonly Dictionary<long, (Point Position, IPointer Pointer)> _touchPoints = new();
    private double _initialDistance = 0;
    private double _initialZoom = 1.0;
    private bool _isPinching;

    private ReaderViewModel? _subscribedViewModel;

    public NormalReadingView()
    {
        InitializeComponent();
        _imageScroller = this.FindControl<ScrollViewer>("ImageScroller");
        _imageContainer = this.FindControl<Grid>("ImageContainer");
        _pageImage = this.FindControl<Image>("PageImage");
        _leftPageImage = this.FindControl<Image>("LeftPageImage");
        _rightPageImage = this.FindControl<Image>("RightPageImage");

        if (_imageScroller != null)
        {
            // Use Tunneling strategy to intercept the wheel event before the ScrollViewer processes it
            _imageScroller.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
            _imageScroller.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            _imageScroller.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
            _imageScroller.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
            // PointerCaptureLost is a *direct* routed event: a tunnel handler (as used before) is never invoked.
            // It is only raised on the element which had the capture, which is the ScrollViewer while pinching.
            _imageScroller.PointerCaptureLost += OnPointerCaptureLost;
            _imageScroller.PropertyChanged += (s, e) =>
            {
                if (e.Property == ScrollViewer.ViewportProperty) UpdateImageSize();
            };
        }
        this.SizeChanged += (s, e) => UpdateImageSize();
    }

    private void EnsureControls()
    {
        _imageScroller ??= this.FindControl<ScrollViewer>("ImageScroller");
        _imageContainer ??= this.FindControl<Grid>("ImageContainer");
        _pageImage ??= this.FindControl<Image>("PageImage");
        _leftPageImage ??= this.FindControl<Image>("LeftPageImage");
        _rightPageImage ??= this.FindControl<Image>("RightPageImage");
    }

    private void ResetTouchState()
    {
        _touchPoints.Clear();
        _initialDistance = 0;
        _isPinching = false;
        _swipeStartPoint = null;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Only forget the pointer which lost the capture, the other finger of a pinch might still be down.
        _touchPoints.Remove(e.Pointer.Id);
        if (_touchPoints.Count < 2)
        {
            _initialDistance = 0;
        }
        if (_touchPoints.Count == 0)
        {
            _isPinching = false;
            _swipeStartPoint = null;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            ResetTouchState();
            if (IsVisible)
            {
                UpdateImageSize();
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from the previous view model, before this a new lambda was added on every DataContext change
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is ReaderViewModel vm)
        {
            _subscribedViewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateImageSize();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ReaderViewModel.CurrentPageImage) ||
            args.PropertyName == nameof(ReaderViewModel.ZoomLevel) ||
            args.PropertyName == nameof(ReaderViewModel.StretchMode) ||
            args.PropertyName == nameof(ReaderViewModel.IsTwoPageMode) ||
            args.PropertyName == nameof(ReaderViewModel.LeftPageImage) ||
            args.PropertyName == nameof(ReaderViewModel.RightPageImage))
        {
            UpdateImageSize();

            // Reset scroll position when switching stretch modes or loading new pages
            if (args.PropertyName == nameof(ReaderViewModel.StretchMode) ||
                args.PropertyName == nameof(ReaderViewModel.CurrentPageImage))
            {
                if (_imageScroller != null && sender is ReaderViewModel vm && !vm.IsReplacingPageResolution)
                {
                    _imageScroller.Offset = new Vector(0, 0);
                }
            }
        }
    }

    private void UpdateImageSize()
    {
        EnsureControls();
        if (DataContext is not ReaderViewModel vm || _imageScroller == null || !IsVisible) return;
        
        var bitmap = vm.CurrentPageImage;
        if (bitmap == null) return;

        // Use Viewport if available, otherwise fallback to Bounds
        double availableWidth = _imageScroller.Viewport.Width > 0 ? _imageScroller.Viewport.Width : _imageScroller.Bounds.Width;
        double availableHeight = _imageScroller.Viewport.Height > 0 ? _imageScroller.Viewport.Height : _imageScroller.Bounds.Height;

        // Work with the size the page has at its original resolution, the bitmap might have been decoded smaller
        var pageSize = vm.GetDisplayPageSize(bitmap);
        double contentWidth = pageSize.Width;
        double contentHeight = pageSize.Height;
        Size? rightPageSize = null;

        if (vm.IsTwoPageMode)
        {
            if (vm.RightPageImage != null)
            {
                rightPageSize = vm.GetDisplayPageSize(vm.RightPageImage);
                // We assume pages have similar heights for fitting logic
                contentWidth = pageSize.Width + rightPageSize.Value.Width + 4; // 4 is margin
                contentHeight = Math.Max(pageSize.Height, rightPageSize.Value.Height);
            }
        }

        if (contentWidth <= 0 || contentHeight <= 0) return;

        double scale = 1.0;
        if (availableWidth > 0 && availableHeight > 0)
        {
            switch (vm.StretchMode)
            {
                case StretchMode.FitPage:
                    scale = Math.Min(availableWidth / contentWidth, availableHeight / contentHeight);
                    break;
                case StretchMode.FitWidth:
                    scale = availableWidth / contentWidth;
                    break;
                case StretchMode.FitHeight:
                    scale = availableHeight / contentHeight;
                    break;
                case StretchMode.Original:
                    scale = 1.0;
                    break;
            }
        }

        double finalScale = scale * vm.ZoomLevel;

        if (!vm.IsTwoPageMode)
        {
            if (_pageImage != null)
            {
                _pageImage.Width = pageSize.Width * finalScale;
                _pageImage.Height = pageSize.Height * finalScale;
            }
            // Clear sizes for unused controls to avoid layout ghosting
            if (_leftPageImage != null) { _leftPageImage.Width = 0; _leftPageImage.Height = 0; }
            if (_rightPageImage != null) { _rightPageImage.Width = 0; _rightPageImage.Height = 0; }
        }
        else
        {
            if (_leftPageImage != null)
            {
                _leftPageImage.Width = pageSize.Width * finalScale;
                _leftPageImage.Height = pageSize.Height * finalScale;
            }
            if (_rightPageImage != null)
            {
                if (rightPageSize.HasValue)
                {
                    _rightPageImage.Width = rightPageSize.Value.Width * finalScale;
                    _rightPageImage.Height = rightPageSize.Value.Height * finalScale;
                }
                else
                {
                    _rightPageImage.Width = 0;
                    _rightPageImage.Height = 0;
                }
            }
            // Clear sizes for unused controls
            if (_pageImage != null) { _pageImage.Width = 0; _pageImage.Height = 0; }
        }
    }

    private bool CanScrollVertically(bool down)
    {
        if (_imageScroller == null) return false;
        double maxOffset = _imageScroller.Extent.Height - _imageScroller.Viewport.Height;
        if (maxOffset <= 1) return false;
        return down ? _imageScroller.Offset.Y < maxOffset - 1 : _imageScroller.Offset.Y > 1;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            if (e.Delta.Y == 0) return;

            var position = e.GetPosition(_imageScroller);
            ZoomAtPoint(vm, e.Delta.Y > 0, position);
        }
        else
        {
            if (e.Delta.Y == 0) return;

            // When the page is taller than the viewport (fit width, zoomed in, original size) let the
            // ScrollViewer scroll first, and only flip the page once the top/bottom has been reached.
            // Before, the wheel always flipped the page, so a zoomed or fit-width page could not be scrolled.
            if (CanScrollVertically(down: e.Delta.Y < 0))
            {
                return;
            }

            e.Handled = true;

            if (e.Delta.Y > 0 && vm.HasPreviousPage)
            {
                vm.GoToPreviousPageCommand.Execute(null);
            }
            else if (e.Delta.Y < 0)
            {
                // Always route "next" through the command: at the last page it shows the end-of-comic options
                vm.GoToNextPageCommand.Execute(null);
            }
        }
    }

    private void ZoomAtPoint(ReaderViewModel vm, bool zoomIn, Point center)
    {
        if (_imageScroller == null) return;

        double oldZoom = vm.ZoomLevel;
        vm.AdjustZoom(zoomIn ? 1 : -1);
        double newZoom = vm.ZoomLevel;

        if (Math.Abs(oldZoom - newZoom) < 0.01) return;

        ApplyZoomAroundPoint(oldZoom, newZoom, center);
    }

    /// <summary>
    /// Keep the content under <paramref name="center"/> (in scroller coordinates) at the same place after a zoom change.
    /// </summary>
    private void ApplyZoomAroundPoint(double oldZoom, double newZoom, Point center)
    {
        if (_imageScroller == null || oldZoom <= 0) return;

        var scrollOffset = _imageScroller.Offset;
        var relativeX = (center.X + scrollOffset.X) / oldZoom;
        var relativeY = (center.Y + scrollOffset.Y) / oldZoom;

        UpdateImageSize();
        // Make sure Extent reflects the new image size before clamping the offset,
        // otherwise the offset is clamped to the old (smaller) extent and the zoom jumps to the top/left.
        _imageScroller.UpdateLayout();

        var newOffsetX = relativeX * newZoom - center.X;
        var newOffsetY = relativeY * newZoom - center.Y;

        _imageScroller.Offset = new Vector(
            Math.Max(0, Math.Min(_imageScroller.Extent.Width - _imageScroller.Viewport.Width, newOffsetX)),
            Math.Max(0, Math.Min(_imageScroller.Extent.Height - _imageScroller.Viewport.Height, newOffsetY))
        );
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm || _imageScroller == null) return;

        if (e.Pointer.Type == PointerType.Touch)
        {
            // The primary pointer is the first finger touching the screen, so no other finger can be down.
            // Clear whatever is left from earlier gestures: when the ScrollGestureRecognizer took over a finger,
            // or a touch was cancelled by the OS, the release was never seen here. The stale entry made every
            // following single-finger touch count as the 2nd finger of a pinch: zoom got "stuck" and swipe/tap
            // navigation stopped working.
            if (e.Pointer.IsPrimary)
            {
                ResetTouchState();
            }

            var position = e.GetPosition(_imageScroller);
            _touchPoints[e.Pointer.Id] = (position, e.Pointer);
            if (_touchPoints.Count >= 2)
            {
                // Multi-touch detected: stop the ScrollViewer from taking over
                e.Handled = true;
                _isPinching = true;
                _swipeStartPoint = null;
                
                if (_touchPoints.Count == 2)
                {
                    var points = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(_touchPoints.Values, v => v.Position));
                    _initialDistance = GetDistance(points[0], points[1]);
                    _initialZoom = vm.ZoomLevel;
                    // Capture both pointers to this control to prevent gesture recognizer from winning
                    foreach (var tp in _touchPoints.Values)
                    {
                        tp.Pointer.Capture(_imageScroller);
                    }
                }
                return;
            }
        }

        _swipeStartPoint = e.GetPosition(_imageScroller);
        _swipeStartTime = DateTime.UtcNow;
        _swipeStartOffset = _imageScroller.Offset;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm || _imageScroller == null) return;

        if (e.Pointer.Type == PointerType.Touch && _touchPoints.ContainsKey(e.Pointer.Id))
        {
            var position = e.GetPosition(_imageScroller);
            _touchPoints[e.Pointer.Id] = (position, e.Pointer);
            if (_touchPoints.Count >= 2)
            {
                // Ensure the event is consumed so ScrollViewer doesn't pan
                e.Handled = true;

                if (_touchPoints.Count == 2)
                {
                    // Update pinch
                    var points = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(_touchPoints.Values, v => v.Position));
                    double currentDistance = GetDistance(points[0], points[1]);
                    if (_initialDistance > 10) // Minimum distance threshold
                    {
                        double scale = currentDistance / _initialDistance;
                        double targetZoom = _initialZoom * scale;
                        
                        var center = new Point((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2);
                        
                        double oldZoom = vm.ZoomLevel;
                        // Minimum zoom is 1.0 to prevent the page from becoming smaller than the screen space
                        vm.ZoomLevel = Math.Max(1.0, Math.Min(5.0, targetZoom));
                        
                        if (Math.Abs(oldZoom - vm.ZoomLevel) > 0.0001)
                        {
                            ApplyZoomAroundPoint(oldZoom, vm.ZoomLevel, center);
                        }
                    }
                }
                return;
            }
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _touchPoints.Remove(e.Pointer.Id);
        bool wasPinching = _isPinching;
        if (_touchPoints.Count < 2)
        {
            _initialDistance = 0;
            if (wasPinching)
            {
                // Only release captures we took ourselves (pinch), don't interfere with the ScrollViewer
                e.Pointer.Capture(null);
            }
        }
        if (_touchPoints.Count == 0)
        {
            _isPinching = false;
        }

        if (wasPinching)
        {
            _swipeStartPoint = null;
            return;
        }

        if (DataContext is not ReaderViewModel vm || _imageScroller == null || !_swipeStartPoint.HasValue || _touchPoints.Count > 0)
        {
            _swipeStartPoint = null;
            return;
        }

        var position = e.GetPosition(_imageScroller);
        var elapsed = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
        var deltaX = position.X - _swipeStartPoint.Value.X;
        var deltaY = position.Y - _swipeStartPoint.Value.Y;
        _swipeStartPoint = null;

        // If the ScrollViewer actually scrolled horizontally during this drag, it was a pan of a zoomed page,
        // not a swipe. Before, a quick pan of a zoomed page also flipped the page.
        bool scrolledHorizontally = Math.Abs(_imageScroller.Offset.X - _swipeStartOffset.X) > 1;

        if (!scrolledHorizontally && elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) > SwipeThreshold && Math.Abs(deltaY) < SwipeMaxVerticalDeviation)
        {
            if (deltaX > 0)
            {
                if (vm.IsRightToLeftNavigation) vm.GoToNextPageCommand.Execute(null);
                else vm.GoToPreviousPageCommand.Execute(null);
            }
            else
            {
                if (vm.IsRightToLeftNavigation) vm.GoToPreviousPageCommand.Execute(null);
                else vm.GoToNextPageCommand.Execute(null);
            }
        }
        else if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) < TapMaxMovement && Math.Abs(deltaY) < TapMaxMovement)
        {
            double width = _imageScroller.Bounds.Width;
            if (position.X < width * 0.25)
            {
                if (vm.IsRightToLeftNavigation) vm.GoToNextPageCommand.Execute(null);
                else vm.GoToPreviousPageCommand.Execute(null);
            }
            else if (position.X > width * 0.75)
            {
                if (vm.IsRightToLeftNavigation) vm.GoToPreviousPageCommand.Execute(null);
                else vm.GoToNextPageCommand.Execute(null);
            }
            else vm.ToggleControlsCommand.Execute(null);
        }
    }

    private double GetDistance(Point p1, Point p2)
    {
        return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }
}
