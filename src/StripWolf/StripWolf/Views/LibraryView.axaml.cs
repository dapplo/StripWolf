using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StripWolf.Services;
using StripWolf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace StripWolf.Views;

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
            Title = "Select Comic Files",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Comic Files")
                {
                    Patterns = ["*.cbz", "*.cbr"]
                },
                new FilePickerFileType("PDF Files")
                {
                    Patterns = ["*.pdf"]
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"]
                }
            ]
        });

        if (files.Count > 0 && DataContext is LibraryViewModel viewModel)
        {
            var paths = new List<string>();

            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(localPath))
                {
                    // Desktop: use the local path directly
                    paths.Add(localPath);
                }
                else
                {
                    // Android/other platforms: copy file to local storage
                    var copiedPath = await CopyFileToLocalStorageAsync(file);
                    if (!string.IsNullOrEmpty(copiedPath))
                    {
                        paths.Add(copiedPath);
                    }
                }
            }
            
            if (paths.Count > 0)
            {
                await viewModel.ImportFilesCommand.ExecuteAsync(paths);
            }
        }
    }

    /// <summary>
    /// Copies a file from a storage provider (e.g., Android content URI) to local app storage.
    /// </summary>
    private async Task<string?> CopyFileToLocalStorageAsync(IStorageFile file)
    {
        try
        {
            var libraryService = App.Services?.GetService<LibraryService>();
            if (libraryService is null)
            {
                return null;
            }

            var sanitizedName = LibraryService.SanitizeFileName(file.Name);
            var targetPath = Path.Combine(libraryService.ComicsDirectory, sanitizedName);
            
            // Ensure unique filename if file already exists
            var counter = 1;
            var baseName = Path.GetFileNameWithoutExtension(sanitizedName);
            var extension = Path.GetExtension(sanitizedName);
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(libraryService.ComicsDirectory, $"{baseName}_{counter}{extension}");
                counter++;
            }

            await using var sourceStream = await file.OpenReadAsync();
            await using var targetStream = File.Create(targetPath);
            await sourceStream.CopyToAsync(targetStream);
            
            return targetPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to copy file to local storage: {ex.Message}");
            return null;
        }
    }
}
