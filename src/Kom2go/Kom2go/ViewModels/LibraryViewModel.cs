using System.Collections.ObjectModel;
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
    private ObservableCollection<Comic> _comics = [];

    [ObservableProperty]
    private ObservableCollection<Comic> _recentComics = [];

    [ObservableProperty]
    private ObservableCollection<Comic> _inProgressComics = [];

    [ObservableProperty]
    private Comic? _selectedComic;

    [ObservableProperty]
    private bool _isRefreshing;

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
            var comics = await _libraryService.GetAllComicsAsync();
            Comics.Clear();
            foreach (var comic in comics)
            {
                Comics.Add(comic);
            }

            var recent = await _libraryService.GetRecentComicsAsync();
            RecentComics.Clear();
            foreach (var comic in recent)
            {
                RecentComics.Add(comic);
            }

            var inProgress = await _libraryService.GetInProgressComicsAsync();
            InProgressComics.Clear();
            foreach (var comic in inProgress)
            {
                InProgressComics.Add(comic);
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
    private async Task ImportFileAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var comic = await _libraryService.ImportLocalComicAsync(filePath);
            if (!Comics.Any(c => c.Id == comic.Id))
            {
                Comics.Insert(0, comic);
            }
        }, "Failed to import comic file");
    }

    [RelayCommand]
    private void OpenComic(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        SelectedComic = comic;
        // Navigation will be handled by the view
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
            Comics.Remove(comic);
            RecentComics.Remove(comic);
            InProgressComics.Remove(comic);
        }, "Failed to delete comic");
    }
}
