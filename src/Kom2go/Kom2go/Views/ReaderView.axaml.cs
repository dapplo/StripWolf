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
    private Viewbox? _imageViewbox;

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

    private void OnLeftZonePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ReaderViewModel vm && vm.HasPreviousPage)
        {
            vm.GoToPreviousPageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRightZonePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ReaderViewModel vm && vm.HasNextPage)
        {
            vm.GoToNextPageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCenterZonePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ReaderViewModel vm)
        {
            vm.ToggleControlsCommand.Execute(null);
            e.Handled = true;
        }
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
                
            case Key.D2:
            case Key.NumPad2:
                // Toggle two-page mode with "2" key
                vm.ToggleTwoPageModeCommand.Execute(null);
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
        _imageViewbox = this.FindControl<Viewbox>("ImageViewbox");
        
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
        _imageViewbox = null;
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
        if (DataContext is not ReaderViewModel vm || _imageViewbox is null || _imageScroller is null)
        {
            return;
        }

        // Cache bounds to avoid repeated property access
        var scrollerBounds = _imageScroller.Bounds;
        var scrollerWidth = scrollerBounds.Width;
        var scrollerHeight = scrollerBounds.Height;

        // Configure the Viewbox stretch to match the desired mode
        switch (vm.StretchMode)
        {
            case StretchMode.FitPage:
                // Fit the entire page within the viewport (both width and height)
                _imageViewbox.Stretch = Stretch.Uniform;
                _imageViewbox.StretchDirection = StretchDirection.Both;
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                _imageViewbox.MaxWidth = double.PositiveInfinity;
                _imageViewbox.MaxHeight = double.PositiveInfinity;
                break;
                
            case StretchMode.FitWidth:
                // Fit to viewport width, allow vertical scrolling
                _imageViewbox.Stretch = Stretch.Uniform;
                _imageViewbox.StretchDirection = StretchDirection.Both;
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                // Force width-based sizing - set viewbox width constraint
                _imageViewbox.MaxWidth = scrollerWidth > 0 ? scrollerWidth : double.PositiveInfinity;
                _imageViewbox.MaxHeight = double.PositiveInfinity;
                break;
                
            case StretchMode.FitHeight:
                // Fit to viewport height, allow horizontal scrolling
                _imageViewbox.Stretch = Stretch.Uniform;
                _imageViewbox.StretchDirection = StretchDirection.Both;
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                // Force height-based sizing - set viewbox height constraint
                _imageViewbox.MaxWidth = double.PositiveInfinity;
                _imageViewbox.MaxHeight = scrollerHeight > 0 ? scrollerHeight : double.PositiveInfinity;
                break;
                
            case StretchMode.Original:
                // Original size (100%), allow scrolling in both directions
                _imageViewbox.Stretch = Stretch.None;
                _imageViewbox.StretchDirection = StretchDirection.DownOnly;
                _imageScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                _imageScroller.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                _imageViewbox.MaxWidth = double.PositiveInfinity;
                _imageViewbox.MaxHeight = double.PositiveInfinity;
                break;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Re-apply stretch mode when size changes
        UpdateImageStretch();
    }
}
