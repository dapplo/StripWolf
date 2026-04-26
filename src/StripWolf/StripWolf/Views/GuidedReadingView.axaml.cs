using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using StripWolf.ViewModels;

namespace StripWolf.Views;

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

    private Rectangle? _zoomRect;
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
                rect.Fill = isCurrent ? new SolidColorBrush(Colors.Yellow, 0.2) : Brushes.Transparent;
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == "Bounds")
        {
            UpdateZoomRegion();
        }
    }
}
