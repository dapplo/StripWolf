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
    private ObservableCollection<Comic> _favoriteComics = [];

    [ObservableProperty]
    private ObservableCollection<PendingImport> _pendingImports = [];


    [ObservableProperty]
    private Comic? _selectedComic;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Comic> _searchResults = [];

    [ObservableProperty]
    private bool _isSearching;

    private const int DeleteUndoTimeoutSeconds = 10; // 10 seconds

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

    partial void OnSearchTextChanged(string value)
    {
        // Trigger search when text changes
        _ = SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            IsSearching = false;
            SearchResults.Clear();
            return;
        }

        IsSearching = true;
        try
        {
            var results = await _libraryService.SearchComicsAsync(SearchText);
            SearchResults.Clear();
            foreach (var comic in results)
            {
                SearchResults.Add(comic);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        IsSearching = false;
        SearchResults.Clear();
    }

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    private string _deleteConfirmationPath = string.Empty;

    [ObservableProperty]
    private Comic? _comicPendingDeletion;

    [RelayCommand]
    private async Task LoadComicsAsync()
    {
        await ExecuteAsync(async () =>
        {
            // Clean up comics with missing files on first load
            await _libraryService.CleanupMissingFilesAsync();
            
            var favorites = await _libraryService.GetFavoriteComicsAsync();
            MergeComics(FavoriteComics, favorites);
            
            var newComicsData = await _libraryService.GetNewComicsAsync();
            MergeComics(NewComics, newComicsData);

            var inProgress = await _libraryService.GetInProgressComicsAsync();
            MergeComics(InProgressComics, inProgress);

            var completed = await _libraryService.GetCompletedComicsAsync();
            MergeComics(CompletedComics, completed);
        });
    }

    private void MergeComics(ObservableCollection<Comic> currentList, List<Comic> newList)
    {
        // Remove items no longer in the list (unless they are being deleted)
        var toRemove = currentList.Where(c => !c.IsDeleting && newList.All(n => n.Id != c.Id)).ToList();
        foreach (var item in toRemove) currentList.Remove(item);

        // Add or update items
        for (int i = 0; i < newList.Count; i++)
        {
            var newItem = newList[i];
            var existing = currentList.FirstOrDefault(c => c.Id == newItem.Id);

            if (existing == null)
            {
                // New item, check if it's already in the global deleting dictionary
                if (_deleteCancellationTokens.TryGetValue(newItem.Id, out var entry))
                {
                    newItem.IsDeleting = true;
                    newItem.DeletionSecondsRemaining = entry.Comic.DeletionSecondsRemaining;
                }
                currentList.Insert(i, newItem);
            }
            else
            {
                // Update properties of existing item but preserve deletion state
                if (!existing.IsDeleting)
                {
                    existing.CurrentPage = newItem.CurrentPage;
                    existing.IsCompleted = newItem.IsCompleted;
                    existing.IsFavorite = newItem.IsFavorite;
                    existing.LastReadDate = newItem.LastReadDate;
                }
                
                // Move to correct position if needed
                int currentIndex = currentList.IndexOf(existing);
                if (currentIndex != i)
                {
                    currentList.Move(currentIndex, i);
                }
            }
        }
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
            var format = ComicReaderService.GetComicFormat(pending.FilePath);
            pending.Status = format switch
            {
                ComicFormat.Pdf => "Converting PDF...",
                ComicFormat.Cb7 => "Converting CB7...",
                ComicFormat.Cbt => "Converting CBT...",
                _ => "Importing..."
            };

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

    private readonly Dictionary<int, (Comic Comic, CancellationTokenSource Cts)> _deleteCancellationTokens = new();

    [RelayCommand]
    private async Task DeleteComicAsync(Comic? comic)
    {
        if (comic is null || comic.IsDeleting)
        {
            return;
        }

        // Check if file is in application directory
        var appDir = _libraryService.ComicsDirectory;
        bool isInternal = comic.FilePath.StartsWith(appDir, StringComparison.OrdinalIgnoreCase);

        if (!isInternal)
        {
            ComicPendingDeletion = comic;
            DeleteConfirmationPath = comic.FilePath;
            ShowDeleteConfirmation = true;
            return;
        }

        await StartDeletionProcess(comic, true); // Internal files are always deleted physically
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (ComicPendingDeletion != null)
        {
            var comic = ComicPendingDeletion;
            ShowDeleteConfirmation = false;
            ComicPendingDeletion = null;
            await StartDeletionProcess(comic, true); // Permanent delete
        }
    }

    [RelayCommand]
    private async Task RemoveFromLibraryAsync()
    {
        if (ComicPendingDeletion != null)
        {
            var comic = ComicPendingDeletion;
            ShowDeleteConfirmation = false;
            ComicPendingDeletion = null;
            await StartDeletionProcess(comic, false); // Only remove from library
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
        ComicPendingDeletion = null;
    }

    private async Task StartDeletionProcess(Comic comic, bool deleteFile)
    {
        // Mark as deleting in UI
        comic.IsDeleting = true;
        comic.DeletionSecondsRemaining = DeleteUndoTimeoutSeconds;

        // Create cancellation token
        var cts = new CancellationTokenSource();
        _deleteCancellationTokens[comic.Id] = (comic, cts);

        // Start countdown
        _ = StartComicDeleteCountdownAsync(comic, cts, deleteFile);
    }

    private async Task StartComicDeleteCountdownAsync(Comic comic, CancellationTokenSource cts, bool deleteFile)
    {
        try
        {
            Debug.WriteLine($"Starting {(deleteFile ? "deletion" : "removal")} countdown for comic {comic.Id}: {comic.Title}");
            while (comic.DeletionSecondsRemaining > 0 && !cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                {
                    comic.DeletionSecondsRemaining--;
                }
            }

            // If not cancelled, perform action
            if (!cts.Token.IsCancellationRequested)
            {
                await PerformPermanentComicActionAsync(comic, deleteFile);
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"Action for comic {comic.Id} was cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in countdown for comic {comic.Id}: {ex.Message}");
        }
        finally
        {
            if (_deleteCancellationTokens.TryGetValue(comic.Id, out var entry) && entry.Cts == cts)
            {
                _deleteCancellationTokens.Remove(comic.Id);
            }
        }
    }

    private async Task PerformPermanentComicActionAsync(Comic comic, bool deleteFile)
    {
        try
        {
            if (deleteFile)
            {
                await _libraryService.DeleteComicAsync(comic);
            }
            else
            {
                // Just remove from database
                await _libraryService.RemoveComicFromLibraryAsync(comic);
            }
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                NewComics.Remove(comic);
                InProgressComics.Remove(comic);
                CompletedComics.Remove(comic);
                FavoriteComics.Remove(comic);
                SearchResults.Remove(comic);
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to perform action: {ex.Message}";
            comic.IsDeleting = false;
        }
    }

    [RelayCommand]
    private void UndoComicDelete(Comic? comic)
    {
        if (comic is null || !comic.IsDeleting)
        {
            return;
        }

        if (_deleteCancellationTokens.TryGetValue(comic.Id, out var entry))
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
            _deleteCancellationTokens.Remove(comic.Id);
        }

        comic.IsDeleting = false;
        comic.DeletionSecondsRemaining = 0;
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
            // Store old state for UI update
            var wasCompleted = comic.IsCompleted;
            
            // Save to database first (this updates the comic state in the database)
            await _libraryService.ToggleReadStatusAsync(comic.Id);
            
            // Update the local comic object to match what the database now has
            comic.IsCompleted = !wasCompleted;
            if (!comic.IsCompleted)
            {
                comic.CurrentPage = 0;
                comic.LastReadDate = null;
            }
            
            // Move comic between collections for immediate UI feedback
            if (wasCompleted)
            {
                // Was completed, now unread -> move to New Comics
                CompletedComics.Remove(comic);
                if (!NewComics.Contains(comic))
                {
                    NewComics.Insert(0, comic);
                }
            }
            else
            {
                // Was not completed, now completed -> move to Read
                NewComics.Remove(comic);
                InProgressComics.Remove(comic);
                if (!CompletedComics.Contains(comic))
                {
                    CompletedComics.Insert(0, comic);
                }
            }
        }, "Failed to update read status");
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            // Store old state for UI update
            var wasFavorite = comic.IsFavorite;
            
            // Save to database first
            await _libraryService.ToggleFavoriteAsync(comic.Id);
            
            // Update the local comic object
            comic.IsFavorite = !wasFavorite;
            
            // Update the favorites collection
            if (comic.IsFavorite)
            {
                if (!FavoriteComics.Contains(comic))
                {
                    FavoriteComics.Insert(0, comic);
                }
            }
            else
            {
                FavoriteComics.Remove(comic);
            }
        }, "Failed to update favorite status");
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

    /// <summary>
    /// Performs permanent deletion of all comics in the pending delete queue.
    /// Should be called when the application is closing.
    /// </summary>
    public async Task DeleteAllPendingComicsAsync()
    {
        // Cancel all pending countdowns and perform deletion immediately
        var tokens = _deleteCancellationTokens.ToList();
        
        foreach (var entry in tokens)
        {
            entry.Value.Cts.Cancel();
            entry.Value.Cts.Dispose();
            
            try
            {
                await _libraryService.DeleteComicAsync(entry.Value.Comic);
            }
            catch
            {
                // Ignore errors during app shutdown
            }
        }
        
        _deleteCancellationTokens.Clear();
    }
}
