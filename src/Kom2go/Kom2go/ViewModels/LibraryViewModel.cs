using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kom2go.Models;
using Kom2go.Services;

namespace Kom2go.ViewModels;

/// <summary>
/// View model for the library page
/// </summary>
public partial class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _libraryService;
    private readonly ComicReaderService _comicReaderService;

    [ObservableProperty]
    private ObservableCollection<Comic> _newComics = [];

    [ObservableProperty]
    private ObservableCollection<Comic> _inProgressComics = [];

    [ObservableProperty]
    private ObservableCollection<Comic> _completedComics = [];

    [ObservableProperty]
    private ObservableCollection<PendingImport> _pendingImports = [];

    [ObservableProperty]
    private Comic? _selectedComic;

    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Whether the app is running on desktop (not mobile)
    /// </summary>
    public bool IsDesktop => !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();

    /// <summary>
    /// Event raised when a comic should be opened in the reader
    /// </summary>
    public event EventHandler<int>? ComicOpenRequested;

    public LibraryViewModel(LibraryService libraryService, ComicReaderService comicReaderService)
    {
        _libraryService = libraryService;
        _comicReaderService = comicReaderService;
        Title = "Library";
    }

    [RelayCommand]
    private async Task LoadComicsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var newComics = await _libraryService.GetNewComicsAsync();
            NewComics.Clear();
            foreach (var comic in newComics)
            {
                NewComics.Add(comic);
            }

            var inProgress = await _libraryService.GetInProgressComicsAsync();
            InProgressComics.Clear();
            foreach (var comic in inProgress)
            {
                InProgressComics.Add(comic);
            }

            var completed = await _libraryService.GetCompletedComicsAsync();
            CompletedComics.Clear();
            foreach (var comic in completed)
            {
                CompletedComics.Add(comic);
            }
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadComicsAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private void OpenComicsDirectory()
    {
        try
        {
            var path = _libraryService.ComicsDirectory;
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", path);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", path);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", path);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to open directory: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportFileAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        await ImportFilesAsync([filePath]);
    }

    [RelayCommand]
    private async Task ImportFilesAsync(IList<string>? filePaths)
    {
        if (filePaths is null || filePaths.Count == 0)
        {
            return;
        }

        // Create pending import items for each file
        var pendingItems = new List<PendingImport>();
        foreach (var filePath in filePaths)
        {
            var pending = new PendingImport
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Status = "Waiting..."
            };
            pendingItems.Add(pending);
            PendingImports.Add(pending);
        }

        // Process files sequentially
        foreach (var pending in pendingItems)
        {
            pending.IsProcessing = true;
            pending.Status = ComicReaderService.GetComicFormat(pending.FilePath) == ComicFormat.Pdf 
                ? "Converting PDF..." 
                : "Importing...";

            try
            {
                var progress = new Progress<double>(p =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        pending.Progress = p;
                        pending.Status = $"Converting... {p:P0}";
                    });
                });

                var comic = await _libraryService.ImportLocalComicAsync(pending.FilePath, progress);
                
                pending.IsProcessing = false;
                pending.IsCompleted = true;
                pending.Status = "Completed";
                pending.Progress = 1.0;

                if (!NewComics.Any(c => c.Id == comic.Id))
                {
                    NewComics.Insert(0, comic);
                }

                // Remove completed item after a short delay
                await RemoveCompletedImportAfterDelayAsync(pending);
            }
            catch (Exception ex)
            {
                pending.IsProcessing = false;
                pending.IsFailed = true;
                pending.Status = "Failed";
                pending.ErrorMessage = ex.Message;
            }
        }
    }

    [RelayCommand]
    private void OpenComic(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        SelectedComic = comic;
        // Raise event to request opening the reader
        ComicOpenRequested?.Invoke(this, comic.Id);
    }

    [RelayCommand]
    private async Task DeleteComicAsync(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await _libraryService.DeleteComicAsync(comic);
            NewComics.Remove(comic);
            InProgressComics.Remove(comic);
            CompletedComics.Remove(comic);
        }, "Failed to delete comic");
    }

    [RelayCommand]
    private async Task ToggleReadStatusAsync(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await _libraryService.ToggleReadStatusAsync(comic.Id);
            // Refresh to update the sections
            await LoadComicsAsync();
        }, "Failed to update read status");
    }

    [RelayCommand]
    private void RemovePendingImport(PendingImport? pending)
    {
        if (pending is not null)
        {
            PendingImports.Remove(pending);
        }
    }

    private async Task RemoveCompletedImportAfterDelayAsync(PendingImport pending)
    {
        await Task.Delay(2000);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            PendingImports.Remove(pending);
        });
    }
}
