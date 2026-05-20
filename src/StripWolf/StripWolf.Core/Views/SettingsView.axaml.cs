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
using StripWolf.Core.ViewModels;

namespace StripWolf.Core.Views;

public partial class SettingsView : UserControl
{
    private SectionLayoutItemViewModel? _draggedSection;

    public SettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
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

    private async void OnSectionDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SectionLayoutItemViewModel section })
        {
            return;
        }

        _draggedSection = section;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(section.Key));

        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
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
        await viewModel.MoveSectionAsync(_draggedSection, targetSection);
    }
}
