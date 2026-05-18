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
    private const double SwipeThreshold = 80;
    private const double SwipeMaxTimeMs = 500;
    private const double SwipeMaxVerticalDeviation = 100;

    // Manual Pinch tracking
    private readonly Dictionary<long, Point> _touchPoints = new();
    private double _initialDistance = 0;
    private double _initialZoom = 1.0;

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

            _imageScroller.PointerPressed += OnPointerPressed;
            _imageScroller.PointerMoved += OnPointerMoved;
            _imageScroller.PointerReleased += OnPointerReleased;
            _imageScroller.PointerCaptureLost += OnPointerCaptureLost;
            _imageScroller.PropertyChanged += (s, e) =>
            {
                if (e.Property == ScrollViewer.ViewportProperty) UpdateImageSize();
            };
        }
    }

    private void EnsureControls()
    {
        _imageScroller ??= this.FindControl<ScrollViewer>("ImageScroller");
        _imageContainer ??= this.FindControl<Grid>("ImageContainer");
        _pageImage ??= this.FindControl<Image>("PageImage");
        _leftPageImage ??= this.FindControl<Image>("LeftPageImage");
        _rightPageImage ??= this.FindControl<Image>("RightPageImage");
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _touchPoints.Clear();
        _initialDistance = 0;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ReaderViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
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
                        if (_imageScroller != null)
                        {
                            _imageScroller.Offset = new Vector(0, 0);
                        }
                    }
                }
            };
            UpdateImageSize();
        }
    }

    private void UpdateImageSize()
    {
        EnsureControls();
        if (DataContext is not ReaderViewModel vm || _imageScroller == null) return;
        
        var bitmap = vm.CurrentPageImage;
        if (bitmap == null) return;

        // Use Viewport if available, otherwise fallback to Bounds
        double availableWidth = _imageScroller.Viewport.Width > 0 ? _imageScroller.Viewport.Width : _imageScroller.Bounds.Width;
        double availableHeight = _imageScroller.Viewport.Height > 0 ? _imageScroller.Viewport.Height : _imageScroller.Bounds.Height;

        double contentWidth = bitmap.Size.Width;
        double contentHeight = bitmap.Size.Height;

        if (vm.IsTwoPageMode)
        {
            if (vm.RightPageImage != null)
            {
                // We assume pages have similar heights for fitting logic
                contentWidth = bitmap.Size.Width + vm.RightPageImage.Size.Width + 4; // 4 is margin
                contentHeight = Math.Max(bitmap.Size.Height, vm.RightPageImage.Size.Height);
            }
        }

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
                _pageImage.Width = bitmap.Size.Width * finalScale;
                _pageImage.Height = bitmap.Size.Height * finalScale;
            }
            // Clear sizes for unused controls to avoid layout ghosting
            if (_leftPageImage != null) { _leftPageImage.Width = 0; _leftPageImage.Height = 0; }
            if (_rightPageImage != null) { _rightPageImage.Width = 0; _rightPageImage.Height = 0; }
        }
        else
        {
            if (_leftPageImage != null)
            {
                _leftPageImage.Width = bitmap.Size.Width * finalScale;
                _leftPageImage.Height = bitmap.Size.Height * finalScale;
            }
            if (_rightPageImage != null)
            {
                if (vm.RightPageImage != null)
                {
                    _rightPageImage.Width = vm.RightPageImage.Size.Width * finalScale;
                    _rightPageImage.Height = vm.RightPageImage.Size.Height * finalScale;
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
            e.Handled = true;

            if (e.Delta.Y > 0 && vm.HasPreviousPage)
            {
                vm.GoToPreviousPageCommand.Execute(null);
            }
            else if (e.Delta.Y < 0 && vm.HasNextPage)
            {
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

        var scrollOffset = _imageScroller.Offset;
        var relativeX = (center.X + scrollOffset.X) / oldZoom;
        var relativeY = (center.Y + scrollOffset.Y) / oldZoom;

        UpdateImageSize();
        UpdateLayout();

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
            _touchPoints[e.Pointer.Id] = e.GetPosition(_imageScroller);
            if (_touchPoints.Count >= 2)
            {
                // Multi-touch detected: stop the ScrollViewer from taking over
                e.Handled = true;
                
                if (_touchPoints.Count == 2)
                {
                    var points = _touchPoints.Values.ToArray();
                    _initialDistance = GetDistance(points[0], points[1]);
                    _initialZoom = vm.ZoomLevel;
                    // Capture the pointer to this control to prevent gesture recognizer from winning
                    e.Pointer.Capture(_imageScroller);
                }
                return;
            }
        }

        var position = e.GetPosition(_imageScroller);
        _swipeStartPoint = position;
        _swipeStartTime = DateTime.UtcNow;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm || _imageScroller == null) return;

        if (e.Pointer.Type == PointerType.Touch && _touchPoints.ContainsKey(e.Pointer.Id))
        {
            _touchPoints[e.Pointer.Id] = e.GetPosition(_imageScroller);
            if (_touchPoints.Count >= 2)
            {
                // Ensure the event is consumed so ScrollViewer doesn't pan
                e.Handled = true;

                if (_touchPoints.Count == 2)
                {
                    // Update pinch
                    var points = _touchPoints.Values.ToArray();
                    double currentDistance = GetDistance(points[0], points[1]);
                    if (_initialDistance > 10) // Minimum distance threshold
                    {
                        double scale = currentDistance / _initialDistance;
                        double targetZoom = _initialZoom * scale;
                        
                        var center = new Point((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2);
                        
                        double oldZoom = vm.ZoomLevel;
                        vm.ZoomLevel = Math.Max(0.5, Math.Min(5.0, targetZoom));
                        
                        if (Math.Abs(oldZoom - vm.ZoomLevel) > 0.0001)
                        {
                            var scrollOffset = _imageScroller.Offset;
                            var relativeX = (center.X + scrollOffset.X) / oldZoom;
                            var relativeY = (center.Y + scrollOffset.Y) / oldZoom;

                            UpdateImageSize();

                            var newOffsetX = relativeX * vm.ZoomLevel - center.X;
                            var newOffsetY = relativeY * vm.ZoomLevel - center.Y;

                            _imageScroller.Offset = new Vector(
                                Math.Max(0, Math.Min(_imageScroller.Extent.Width - _imageScroller.Viewport.Width, newOffsetX)),
                                Math.Max(0, Math.Min(_imageScroller.Extent.Height - _imageScroller.Viewport.Height, newOffsetY))
                            );
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
        if (_touchPoints.Count < 2)
        {
            _initialDistance = 0;
        }

        if (DataContext is not ReaderViewModel vm || !_swipeStartPoint.HasValue || _touchPoints.Count > 0) return;

        var position = e.GetPosition(_imageScroller);
        var elapsed = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
        var deltaX = position.X - _swipeStartPoint.Value.X;
        var deltaY = position.Y - _swipeStartPoint.Value.Y;

        if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) > SwipeThreshold && Math.Abs(deltaY) < SwipeMaxVerticalDeviation)
        {
            if (deltaX > 0) vm.GoToPreviousPageCommand.Execute(null);
            else vm.GoToNextPageCommand.Execute(null);
        }
        else if (elapsed < SwipeMaxTimeMs && Math.Abs(deltaX) < 10 && Math.Abs(deltaY) < 10)
        {
            double width = Bounds.Width;
            if (position.X < width * 0.25) vm.GoToPreviousPageCommand.Execute(null);
            else if (position.X > width * 0.75) vm.GoToNextPageCommand.Execute(null);
            else vm.ToggleControlsCommand.Execute(null);
        }

        _swipeStartPoint = null;
    }

    private double GetDistance(Point p1, Point p2)
    {
        return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }
}

