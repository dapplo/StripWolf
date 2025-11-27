using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kom2go.ViewModels;

namespace Kom2go.Views;

public partial class ReaderView : UserControl
{
    private Image? _pageImage;
    private ScrollViewer? _imageScroller;

    public ReaderView()
    {
        InitializeComponent();
        
        // Handle keyboard navigation
        KeyDown += OnKeyDown;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Handle scroll wheel zoom when Ctrl is held
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is ReaderViewModel vm)
            {
                vm.AdjustZoom(e.Delta.Y);
                e.Handled = true;
            }
        }
    }

    private void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Placeholder for future touch/gesture handling
        // Avalonia handles pinch-to-zoom through gestures on supported platforms
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
            case Key.PageUp:
                vm.GoToPreviousPageCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                vm.GoToNextPageCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Home:
                vm.GoToPageCommand.Execute(0);
                e.Handled = true;
                break;
                
            case Key.End:
                if (vm.Comic is not null)
                {
                    vm.GoToPageCommand.Execute(vm.Comic.PageCount - 1);
                }
                e.Handled = true;
                break;
                
            case Key.Add:
            case Key.OemPlus:
                vm.ZoomInCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Subtract:
            case Key.OemMinus:
                vm.ZoomOutCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.D0:
            case Key.NumPad0:
                vm.ResetZoomCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.F:
            case Key.F11:
                vm.ToggleFullScreenCommand.Execute(null);
                ToggleWindowFullScreen();
                e.Handled = true;
                break;
                
            case Key.Escape:
                if (vm.IsFullScreen)
                {
                    vm.ToggleFullScreenCommand.Execute(null);
                    ToggleWindowFullScreen();
                }
                else
                {
                    vm.GoBackCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }

    private void ToggleWindowFullScreen()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
        {
            return;
        }

        if (DataContext is ReaderViewModel { IsFullScreen: true })
        {
            window.WindowState = WindowState.FullScreen;
            window.SystemDecorations = SystemDecorations.None;
        }
        else
        {
            window.WindowState = WindowState.Normal;
            window.SystemDecorations = SystemDecorations.Full;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // Cache control references to avoid repeated FindControl calls
        _pageImage = this.FindControl<Image>("PageImage");
        _imageScroller = this.FindControl<ScrollViewer>("ImageScroller");
        
        // Focus this control to receive keyboard events
        Focus();
        
        // Update image stretch based on stretch mode
        UpdateImageStretch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // Clear cached references
        _pageImage = null;
        _imageScroller = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is ReaderViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ReaderViewModel.StretchMode))
                {
                    UpdateImageStretch();
                }
            };
        }
    }

    private void UpdateImageStretch()
    {
        if (DataContext is not ReaderViewModel vm || _pageImage is null || _imageScroller is null)
        {
            return;
        }

        _pageImage.Stretch = vm.StretchMode switch
        {
            StretchMode.FitPage => Stretch.Uniform,
            StretchMode.FitWidth => Stretch.UniformToFill,
            StretchMode.FitHeight => Stretch.UniformToFill,
            StretchMode.Original => Stretch.None,
            _ => Stretch.Uniform
        };

        // Adjust scroll behavior based on stretch mode
        switch (vm.StretchMode)
        {
            case StretchMode.FitWidth:
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                break;
            case StretchMode.FitHeight:
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                break;
            default:
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                break;
        }
    }
}
