using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Kom2go.ViewModels;

namespace Kom2go.Views;

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

    protected override void RedrawOverview()
    {
        if (DataContext is not ReaderViewModel vm) return;

        var canvas = vm.IsOverviewOnLeft ? OverviewCanvasLeftControl : OverviewCanvasRightControl;
        if (canvas == null) return;

        canvas.Children.Clear();

        var region = vm.ZoomRegion;

        var rect = new Rectangle
        {
            Stroke = Brushes.Red,
            StrokeThickness = 2,
            Width = region.Size * canvas.Width,
            Height = region.Size * canvas.Height
        };

        Canvas.SetLeft(rect, (region.CenterX - region.Size / 2) * canvas.Width);
        Canvas.SetTop(rect, (region.CenterY - region.Size / 2) * canvas.Height);
        canvas.Children.Add(rect);
    }
}
