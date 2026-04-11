using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Kom2go.ViewModels;

namespace Kom2go.Views;

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

    protected override void RedrawOverview()
    {
        if (DataContext is not ReaderViewModel vm) return;

        var canvas = vm.IsOverviewOnLeft ? OverviewCanvasLeftControl : OverviewCanvasRightControl;
        if (canvas == null) return;

        canvas.Children.Clear();

        // 1. Draw panels
        if (vm.CurrentPagePanels != null)
        {
            foreach (var panel in vm.CurrentPagePanels.Panels)
            {
                var rect = new Rectangle
                {
                    Stroke = panel == vm.CurrentPanel ? Brushes.Yellow : Brushes.Blue,
                    StrokeThickness = panel == vm.CurrentPanel ? 3 : 1,
                    Fill = panel == vm.CurrentPanel ? new SolidColorBrush(Colors.Yellow, 0.2) : Brushes.Transparent,
                    Width = panel.Width * canvas.Width,
                    Height = panel.Height * canvas.Height,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(rect, panel.X * canvas.Width);
                Canvas.SetTop(rect, panel.Y * canvas.Height);
                canvas.Children.Add(rect);
            }
        }

        // 2. Draw the red rectangle showing the actual visible area on the zoom side
        var region = vm.ZoomRegion;
        var zoomRect = new Rectangle
        {
            Stroke = Brushes.Red,
            StrokeThickness = 2,
            Width = _actualDisplayWidthNormalized * canvas.Width,
            Height = _actualDisplayHeightNormalized * canvas.Height,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(zoomRect, (region.CenterX - _actualDisplayWidthNormalized / 2) * canvas.Width);
        Canvas.SetTop(zoomRect, (region.CenterY - _actualDisplayHeightNormalized / 2) * canvas.Height);
        zoomRect.ZIndex = 1000;
        canvas.Children.Add(zoomRect);
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
