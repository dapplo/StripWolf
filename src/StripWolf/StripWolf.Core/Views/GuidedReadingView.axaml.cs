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
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using StripWolf.Core.ViewModels;

namespace StripWolf.Core.Views;

public partial class GuidedReadingView : ZoomViewBase
{
    protected override Image? OverviewImageLeftControl => this.FindControl<Image>("OverviewImageLeft");
    protected override Canvas? OverviewCanvasLeftControl => this.FindControl<Canvas>("OverviewCanvasLeft");
    protected override Canvas? OverviewContainerLeftControl => this.FindControl<Canvas>("OverviewContainerLeft");
    protected override Image? OverviewImageRightControl => this.FindControl<Image>("OverviewImageRight");
    protected override Canvas? OverviewCanvasRightControl => this.FindControl<Canvas>("OverviewCanvasRight");
    protected override Canvas? OverviewContainerRightControl => this.FindControl<Canvas>("OverviewContainerRight");

    protected override Viewbox? ZoomedViewboxLeftControl => this.FindControl<Viewbox>("ZoomedViewboxLeft");
    protected override Canvas? ZoomedCanvasLeftControl => this.FindControl<Canvas>("ZoomedCanvasLeft");
    protected override Image? ZoomedAreaImageLeftControl => this.FindControl<Image>("ZoomedAreaImageLeft");

    protected override Viewbox? ZoomedViewboxRightControl => this.FindControl<Viewbox>("ZoomedViewboxRight");
    protected override Canvas? ZoomedCanvasRightControl => this.FindControl<Canvas>("ZoomedCanvasRight");
    protected override Image? ZoomedAreaImageRightControl => this.FindControl<Image>("ZoomedAreaImageRight");

    public GuidedReadingView()
    {
        InitializeComponent();
        InitializeZoomLogic();
    }

    private static readonly IImmutableSolidColorBrush CurrentPanelFill = new ImmutableSolidColorBrush(Colors.Yellow, 0.2);
    private static readonly IImmutableSolidColorBrush OutsidePanelFill = new ImmutableSolidColorBrush(Colors.Black, 0.45);
    private Rectangle? _zoomRect;
    private Avalonia.Controls.Shapes.Path? _outsidePanelShade;
    private readonly List<Rectangle> _panelRects = new();

    protected override void RedrawOverview()
    {
        if (DataContext is not ReaderViewModel vm) return;

        var canvas = vm.IsOverviewOnLeft ? OverviewCanvasLeftControl : OverviewCanvasRightControl;
        if (canvas == null) return;

        // Sync panel rectangles
        int panelCount = vm.CurrentPagePanels?.Panels.Count ?? 0;
        
        // Adjust pool size
        while (_panelRects.Count < panelCount)
        {
            var r = new Rectangle { IsHitTestVisible = false };
            _panelRects.Add(r);
            canvas.Children.Add(r);
        }
        while (_panelRects.Count > panelCount)
        {
            var r = _panelRects[_panelRects.Count - 1];
            canvas.Children.Remove(r);
            _panelRects.RemoveAt(_panelRects.Count - 1);
        }

        // Update panel rectangles
        if (vm.CurrentPagePanels != null)
        {
            for (int i = 0; i < panelCount; i++)
            {
                var panel = vm.CurrentPagePanels.Panels[i];
                var rect = _panelRects[i];
                bool isCurrent = panel == vm.CurrentPanel;
                
                rect.Stroke = isCurrent ? Brushes.Yellow : Brushes.Blue;
                rect.StrokeThickness = isCurrent ? 3 : 1;
                rect.Fill = isCurrent ? CurrentPanelFill : Brushes.Transparent;
                rect.Width = panel.Width * canvas.Width;
                rect.Height = panel.Height * canvas.Height;
                Canvas.SetLeft(rect, panel.X * canvas.Width);
                Canvas.SetTop(rect, panel.Y * canvas.Height);
            }
        }

        // Update Zoom rectangle
        var region = vm.ZoomRegion;
        if (_zoomRect == null || !canvas.Children.Contains(_zoomRect))
        {
            _zoomRect = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                IsHitTestVisible = false,
                ZIndex = 1000
            };
            canvas.Children.Add(_zoomRect);
        }

        _zoomRect.Width = _actualDisplayWidthNormalized * canvas.Width;
        _zoomRect.Height = _actualDisplayHeightNormalized * canvas.Height;
        Canvas.SetLeft(_zoomRect, (region.CenterX - _actualDisplayWidthNormalized / 2) * canvas.Width);
        Canvas.SetTop(_zoomRect, (region.CenterY - _actualDisplayHeightNormalized / 2) * canvas.Height);
    }

    // Room kept around the outermost panels, so their frames stay visible
    private const double OverviewCropPadding = 0.01;

    // A page with a single panel (e.g. a cover) is shown completely in the zoomed area, the overview would only repeat it
    protected override bool ShowOverview(ReaderViewModel vm) => vm.CurrentPagePanels?.Panels.Count != 1;

    protected override void RedrawZoomedArea(ReaderViewModel vm, Canvas canvas, Rect imageRect)
    {
        var panel = vm.CurrentPanel;
        if (!vm.DimOutsidePanel || panel == null)
        {
            if (_outsidePanelShade != null) _outsidePanelShade.IsVisible = false;
            return;
        }

        if (_outsidePanelShade == null)
        {
            _outsidePanelShade = new Avalonia.Controls.Shapes.Path { Fill = OutsidePanelFill, IsHitTestVisible = false };
        }
        // The zoomed canvas changes side with the handedness
        if (_outsidePanelShade.Parent != canvas)
        {
            (_outsidePanelShade.Parent as Canvas)?.Children.Remove(_outsidePanelShade);
            canvas.Children.Add(_outsidePanelShade);
        }

        // Even-odd fill of the whole canvas with the panel as hole: everything but the current panel gets darker
        var panelRect = new Rect(
            imageRect.X + panel.X * imageRect.Width,
            imageRect.Y + panel.Y * imageRect.Height,
            panel.Width * imageRect.Width,
            panel.Height * imageRect.Height);
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, canvas.Width, canvas.Height).Union(panelRect)));
        geometry.Children.Add(new RectangleGeometry(panelRect));
        _outsidePanelShade.Data = geometry;
        _outsidePanelShade.IsVisible = true;
    }

    protected override Rect GetOverviewCrop(ReaderViewModel vm)
    {
        var panels = vm.CurrentPagePanels?.Panels;
        if (panels == null || panels.Count == 0) return base.GetOverviewCrop(vm);

        // The panels cover the page content, everything outside them is page margin
        double left = Math.Max(0, panels.Min(p => p.X) - OverviewCropPadding);
        double top = Math.Max(0, panels.Min(p => p.Y) - OverviewCropPadding);
        double right = Math.Min(1, panels.Max(p => p.X + p.Width) + OverviewCropPadding);
        double bottom = Math.Min(1, panels.Max(p => p.Y + p.Height) + OverviewCropPadding);
        if (right - left <= 0 || bottom - top <= 0) return base.GetOverviewCrop(vm);
        return new Rect(left, top, right - left, bottom - top);
    }

    protected override bool HandleOverviewClick(ReaderViewModel vm, double x, double y)
    {
        if (vm.CurrentPagePanels == null) return false;

        for (int i = 0; i < vm.CurrentPagePanels.Panels.Count; i++)
        {
            var panel = vm.CurrentPagePanels.Panels[i];
            if (x >= panel.X && x <= panel.X + panel.Width && y >= panel.Y && y <= panel.Y + panel.Height)
            {
                vm.SelectPanelCommand.Execute(i);
                return true;
            }
        }
        return false;
    }

    // Note: the former OnPropertyChanged("Bounds") override was removed, ZoomViewBase already updates on SizeChanged.
}

