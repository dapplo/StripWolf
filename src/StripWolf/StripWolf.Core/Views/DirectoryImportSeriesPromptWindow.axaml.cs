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
using Avalonia.Interactivity;

namespace StripWolf.Core.Views;

public partial class DirectoryImportSeriesPromptWindow : Window
{
    private readonly string _rawDirectoryName;

    public DirectoryImportSeriesPromptWindow()
    {
        InitializeComponent();
        _rawDirectoryName = string.Empty;
        UpdateSeriesNameControls();
    }

    public DirectoryImportSeriesPromptWindow(string rawDirectoryName, string suggestedSeriesName)
        : this()
    {
        _rawDirectoryName = rawDirectoryName;

        DirectoryNameTextBlock.Text = rawDirectoryName;
        SeriesNameTextBox.Text = string.IsNullOrWhiteSpace(suggestedSeriesName)
            ? rawDirectoryName
            : suggestedSeriesName;
    }

    private void OnUseSeriesNameChanged(object? sender, RoutedEventArgs e)
    {
        UpdateSeriesNameControls();
    }

    private void OnResetSeriesNameClicked(object? sender, RoutedEventArgs e)
    {
        SeriesNameTextBox.Text = _rawDirectoryName;
        SeriesNameTextBox.Focus();
        SeriesNameTextBox.CaretIndex = SeriesNameTextBox.Text?.Length ?? 0;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        var useSeriesName = UseSeriesNameCheckBox.IsChecked == true;
        var seriesName = useSeriesName
            ? string.IsNullOrWhiteSpace(SeriesNameTextBox.Text) ? null : SeriesNameTextBox.Text.Trim()
            : null;

        Close(new DirectoryImportSeriesPromptResult(useSeriesName, seriesName));
    }

    private void UpdateSeriesNameControls()
    {
        var isEnabled = UseSeriesNameCheckBox.IsChecked == true;
        SeriesNameTextBox.IsEnabled = isEnabled;
    }
}

public sealed record DirectoryImportSeriesPromptResult(bool UseSeriesName, string? SeriesName);

