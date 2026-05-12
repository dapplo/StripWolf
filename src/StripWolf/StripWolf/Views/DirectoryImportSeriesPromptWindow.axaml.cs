using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StripWolf.Views;

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
