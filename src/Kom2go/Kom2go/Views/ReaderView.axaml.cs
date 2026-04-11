using Avalonia.Controls;
using Avalonia.Input;
using Kom2go.ViewModels;

namespace Kom2go.Views;

public partial class ReaderView : UserControl
{
    public ReaderView()
    {
        InitializeComponent();
        
        // Handle keyboard navigation globally for the reader
        KeyDown += OnKeyDown;
        
        // Handle scroll wheel globally for page navigation
        PointerWheelChanged += OnPointerWheelChanged;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ReaderViewModel vm) return;

        // Only handle wheel if not Ctrl (zoom)
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Delta.Y > 0)
            {
                if (vm.IsGuidedMode) vm.GoToPreviousPanelCommand.Execute(null);
                else vm.GoToPreviousPageCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Delta.Y < 0)
            {
                if (vm.IsGuidedMode) vm.GoToNextPanelCommand.Execute(null);
                else vm.GoToNextPageCommand.Execute(null);
                e.Handled = true;
            }
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
                if (vm.IsGuidedMode) vm.GoToPreviousPanelCommand.Execute(null);
                else vm.GoToPreviousPageCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                if (vm.IsGuidedMode) vm.GoToNextPanelCommand.Execute(null);
                else vm.GoToNextPageCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Home:
                vm.GoToPageCommand.Execute(0);
                e.Handled = true;
                break;
                
            case Key.End:
                if (vm.Comic is not null) vm.GoToPageCommand.Execute(vm.Comic.PageCount - 1);
                e.Handled = true;
                break;
                
            case Key.M:
                vm.CycleReadingModeCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.I:
                vm.ToggleInfoPanelCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.F:
                vm.ToggleFullScreenCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (vm.IsFullScreen) vm.ToggleFullScreenCommand.Execute(null);
                else vm.GoBackCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
