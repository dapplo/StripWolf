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
using Avalonia.Threading;
using StripWolf.Core.ViewModels;

namespace StripWolf.Core.Views;

public partial class SettingsView : UserControl
{
    private SectionLayoutItemViewModel? _draggedSection;
    private SettingsViewModel? _subscribedViewModel;

    public SettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.KomgaSettingsRequested -= OnKomgaSettingsRequested;
            _subscribedViewModel = null;
        }
        if (DataContext is SettingsViewModel newViewModel)
        {
            _subscribedViewModel = newViewModel;
            newViewModel.KomgaSettingsRequested += OnKomgaSettingsRequested;
        }
        
        // Load settings when the view is displayed
        if (DataContext is SettingsViewModel viewModel)
        {
            try
            {
                viewModel.LoadServersCommand.Execute(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load servers: {ex.Message}");
            }
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // A request made before this view existed (e.g. the settings button on the Komga tab creates this view)
        ScrollToRequestedSection();
    }

    private void OnKomgaSettingsRequested(object? sender, EventArgs e)
    {
        // The settings tab may already be shown, in that case there is no Loaded event
        if (IsLoaded)
        {
            ScrollToRequestedSection();
        }
    }

    private void ScrollToRequestedSection()
    {
        // Wait until the layout is done, otherwise the position of the section isn't known yet
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is SettingsViewModel viewModel && viewModel.ConsumeKomgaSettingsRequest())
            {
                ScrollToSection(KomgaSettingsHeader);
            }
        }, DispatcherPriority.Background);
    }

    private void OnJumpToSectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string sectionName } && this.FindControl<Control>(sectionName) is { } section)
        {
            ScrollToSection(section);
        }
    }

    /// <summary>
    /// Scrolls the settings so the section header is at the top (as far as the ScrollViewer allows)
    /// </summary>
    private void ScrollToSection(Control section)
    {
        if (section.TranslatePoint(new Point(0, 0), SettingsContent) is not { } position)
        {
            return;
        }

        SettingsScroll.Offset = new Vector(SettingsScroll.Offset.X, Math.Max(0, position.Y - 8));
    }

    private async void OnSectionDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SectionLayoutItemViewModel section })
        {
            return;
        }

        _draggedSection = section;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(section.Key));

        // async void: an exception (a platform without drag and drop support, a failing settings save) would end the app
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsView: Dragging a section failed: {ex.Message}");
        }
        finally
        {
            _draggedSection = null;
        }
    }

    private void OnSectionDragOver(object? sender, DragEventArgs e)
    {
        if (_draggedSection is null ||
            sender is not Control { DataContext: SectionLayoutItemViewModel targetSection } ||
            ReferenceEquals(_draggedSection, targetSection))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private async void OnSectionDrop(object? sender, DragEventArgs e)
    {
        if (_draggedSection is null ||
            sender is not Control { DataContext: SectionLayoutItemViewModel targetSection } ||
            DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        try
        {
            await viewModel.MoveSectionAsync(_draggedSection, targetSection);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsView: Moving a section failed: {ex.Message}");
        }
    }
}
