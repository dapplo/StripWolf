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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.ComponentModel;
using StripWolf.Core.ViewModels;

namespace StripWolf.Core.Views;

public partial class ReaderView : UserControl
{
    private ReaderViewModel? _subscribedViewModel;

    public ReaderView()
    {
        InitializeComponent();
        
        // Handle keyboard navigation globally for the reader
        KeyDown += OnKeyDown;
        
        // Handle scroll wheel globally for page navigation
        PointerWheelChanged += OnPointerWheelChanged;

        // Update decode dimensions when size changes
        this.SizeChanged += (s, e) => UpdateDecodeDimensions();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from previous view model
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is ReaderViewModel vm)
        {
            _subscribedViewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.IsFullScreen) && sender is ReaderViewModel vm)
        {
            SetWindowFullScreen(vm.IsFullScreen);
        }
    }

    private void SetWindowFullScreen(bool fullScreen)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.WindowState = fullScreen ? WindowState.FullScreen : WindowState.Normal;
        }
    }

    private void UpdateDecodeDimensions()
    {
        if (DataContext is not ReaderViewModel vm) return;

        // Get the scaling factor (DPI)
        var topLevel = TopLevel.GetTopLevel(this);
        var scaling = topLevel?.RenderScaling ?? 1.0;

        // Calculate needed pixels (physical pixels)
        // We add a 20% buffer to avoid blurriness during small zooms
        vm.DecodeWidth = (int)(Bounds.Width * scaling * 1.2);
        vm.DecodeHeight = (int)(Bounds.Height * scaling * 1.2);
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
                if (vm.IsRightToLeftNavigation) ExecuteForward(vm);
                else ExecuteBackward(vm);
                e.Handled = true;
                break;
                
            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                if (vm.IsRightToLeftNavigation) ExecuteBackward(vm);
                else ExecuteForward(vm);
                e.Handled = true;
                break;
                
            case Key.Home:
                vm.GoToPageCommand.Execute(vm.ReadingStartPageIndex);
                e.Handled = true;
                break;
                
            case Key.End:
                vm.GoToPageCommand.Execute(vm.ReadingEndPageIndex);
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

    private static void ExecuteForward(ReaderViewModel vm)
    {
        if (vm.IsGuidedMode) vm.GoToNextPanelCommand.Execute(null);
        else vm.GoToNextPageCommand.Execute(null);
    }

    private static void ExecuteBackward(ReaderViewModel vm)
    {
        if (vm.IsGuidedMode) vm.GoToPreviousPanelCommand.Execute(null);
        else vm.GoToPreviousPageCommand.Execute(null);
    }
}
