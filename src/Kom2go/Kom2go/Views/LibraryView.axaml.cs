using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Kom2go.ViewModels;

namespace Kom2go.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    protected override async void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        // Load comics when the view is displayed
        if (DataContext is LibraryViewModel viewModel)
        {
            try
            {
                await viewModel.LoadComicsCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load comics: {ex.Message}");
            }
        }
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        await OpenFilePickerAsync();
    }

    public async Task OpenFilePickerAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Comic File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Comic Files")
                {
                    Patterns = ["*.cbz", "*.cbr"]
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"]
                }
            ]
        });

        if (files.Count > 0)
        {
            var file = files[0];
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && DataContext is LibraryViewModel viewModel)
            {
                await viewModel.ImportFileCommand.ExecuteAsync(path);
            }
        }
    }
}
