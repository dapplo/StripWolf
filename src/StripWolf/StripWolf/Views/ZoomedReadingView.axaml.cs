using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using StripWolf.ViewModels;

namespace StripWolf.Views;

public partial class ZoomedReadingView : ZoomViewBase
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

    public ZoomedReadingView()
    {
        InitializeComponent();
        InitializeZoomLogic();
    }

    private Rectangle? _overviewRect;

    protected override void RedrawOverview()
    {
        if (DataContext is not ReaderViewModel vm) return;

        var canvas = vm.IsOverviewOnLeft ? OverviewCanvasLeftControl : OverviewCanvasRightControl;
        if (canvas == null) return;

        var region = vm.ZoomRegion;
        double rectWidth = _actualDisplayWidthNormalized * canvas.Width;
        double rectHeight = _actualDisplayHeightNormalized * canvas.Height;
        double rectLeft = (region.CenterX - _actualDisplayWidthNormalized / 2) * canvas.Width;
        double rectTop = (region.CenterY - _actualDisplayHeightNormalized / 2) * canvas.Height;

        if (_overviewRect == null || !canvas.Children.Contains(_overviewRect))
        {
            canvas.Children.Clear();
            _overviewRect = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                IsHitTestVisible = false,
                ZIndex = 1000
            };
            canvas.Children.Add(_overviewRect);
        }

        _overviewRect.Width = rectWidth;
        _overviewRect.Height = rectHeight;
        Canvas.SetLeft(_overviewRect, rectLeft);
        Canvas.SetTop(_overviewRect, rectTop);
    }
}
