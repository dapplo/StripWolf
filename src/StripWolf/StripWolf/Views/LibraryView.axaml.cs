using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StripWolf.Services;
using StripWolf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StripWolf.Models;

namespace StripWolf.Views;

public partial class LibraryView : UserControl, INotifyPropertyChanged
{
    private LibraryViewModel? _subscribedViewModel;
    private event PropertyChangedEventHandler? ProxyPropertyChanged;

    public LibraryView()
    {
        InitializeComponent();
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => ProxyPropertyChanged += value;
        remove => ProxyPropertyChanged -= value;
    }

    public ICommand? OpenComicCommand => (DataContext as LibraryViewModel)?.OpenComicCommand;

    public ICommand? ShowComicInfoCommand => (DataContext as LibraryViewModel)?.ShowComicInfoCommand;

    public ICommand? ToggleFavoriteCommand => (DataContext as LibraryViewModel)?.ToggleFavoriteCommand;

    public ICommand? ToggleReadStatusCommand => (DataContext as LibraryViewModel)?.ToggleReadStatusCommand;

    public ICommand? DeleteComicCommand => (DataContext as LibraryViewModel)?.DeleteComicCommand;

    public ICommand? UndoComicDeleteCommand => (DataContext as LibraryViewModel)?.UndoComicDeleteCommand;

    public ICommand? DeleteSeriesCommand => (DataContext as LibraryViewModel)?.DeleteSeriesCommand;

    public ICommand? RemovePendingImportCommand => (DataContext as LibraryViewModel)?.RemovePendingImportCommand;

    public ICommand? ConvertComicNowCommand => (DataContext as LibraryViewModel)?.ConvertComicNowCommand;

    public ICommand? ViewSeriesOnKomgaCommand => (DataContext as LibraryViewModel)?.ViewSeriesOnKomgaCommand;

    public ICommand? EditMetadataCommand => (DataContext as LibraryViewModel)?.EditMetadataCommand;

    public ICommand? SaveMetadataCommand => (DataContext as LibraryViewModel)?.SaveMetadataCommand;

    public ICommand? CancelMetadataEditCommand => (DataContext as LibraryViewModel)?.CancelMetadataEditCommand;

    public ICommand? CloseComicInfoCommand => (DataContext as LibraryViewModel)?.CloseComicInfoCommand;

    public bool IsEditingMetadata => (DataContext as LibraryViewModel)?.IsEditingMetadata ?? false;

    public ComicInfo? EditingComicInfo => (DataContext as LibraryViewModel)?.EditingComicInfo;

    public Comic? SelectedInfoComic => (DataContext as LibraryViewModel)?.SelectedInfoComic;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is LibraryViewModel vm)
        {
            if (vm.SelectedInfoComic != null)
            {
                vm.CloseComicInfoCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.ShowDeleteConfirmation)
            {
                vm.CancelDeleteCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    protected override async void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as LibraryViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RaiseProxyPropertyChanges();
        
        // Load comics when the view is displayed
        if (DataContext is LibraryViewModel viewModel)
        {
            try
            {
                await viewModel.EnsureComicsLoadedAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load comics: {ex.Message}");
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LibraryViewModel.SelectedInfoComic):
                OnPropertyChanged(nameof(SelectedInfoComic));
                break;
            case nameof(LibraryViewModel.IsEditingMetadata):
                OnPropertyChanged(nameof(IsEditingMetadata));
                break;
            case nameof(LibraryViewModel.EditingComicInfo):
                OnPropertyChanged(nameof(EditingComicInfo));
                break;
        }
    }

    private void RaiseProxyPropertyChanges()
    {
        OnPropertyChanged(nameof(OpenComicCommand));
        OnPropertyChanged(nameof(ShowComicInfoCommand));
        OnPropertyChanged(nameof(ToggleFavoriteCommand));
        OnPropertyChanged(nameof(ToggleReadStatusCommand));
        OnPropertyChanged(nameof(DeleteComicCommand));
        OnPropertyChanged(nameof(UndoComicDeleteCommand));
        OnPropertyChanged(nameof(DeleteSeriesCommand));
        OnPropertyChanged(nameof(RemovePendingImportCommand));
        OnPropertyChanged(nameof(ConvertComicNowCommand));
        OnPropertyChanged(nameof(ViewSeriesOnKomgaCommand));
        OnPropertyChanged(nameof(EditMetadataCommand));
        OnPropertyChanged(nameof(SaveMetadataCommand));
        OnPropertyChanged(nameof(CancelMetadataEditCommand));
        OnPropertyChanged(nameof(CloseComicInfoCommand));
        OnPropertyChanged(nameof(IsEditingMetadata));
        OnPropertyChanged(nameof(EditingComicInfo));
        OnPropertyChanged(nameof(SelectedInfoComic));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        ProxyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        await OpenFilePickerAsync();
    }

    private async void OnImportFolderClicked(object? sender, RoutedEventArgs e)
    {
        await OpenFolderPickerAsync();
    }

    private void OnComicInfoBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel && SelectedInfoComic is not null)
        {
            viewModel.CloseComicInfoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnViewSeriesOnKomgaClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel && SelectedInfoComic is not null)
        {
            viewModel.ViewSeriesOnKomgaCommand.Execute(SelectedInfoComic);
            e.Handled = true;
        }
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
                    Patterns = ["*.cbz", "*.cbr", "*.cb7", "*.cbt", "*.epub"]
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

    public async Task OpenFolderPickerAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Comic Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0 || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var localPath = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            var owner = topLevel as Window;
            if (owner is null)
            {
                await viewModel.ImportDirectoryCommand.ExecuteAsync(localPath);
                return;
            }

            var prompt = new DirectoryImportSeriesPromptWindow(
                LibraryService.GetDirectoryDisplayName(localPath),
                LibraryService.GetSuggestedSeriesNameFromDirectory(localPath));
            var promptResult = await prompt.ShowDialog<DirectoryImportSeriesPromptResult?>(owner);
            if (promptResult is null)
            {
                return;
            }

            await viewModel.ImportDirectoryWithOptionsAsync(
                localPath,
                promptResult.UseSeriesName ? promptResult.SeriesName : null,
                suppressAutomaticDirectoryFallback: !promptResult.UseSeriesName);
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
