using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kom2go.Models;
using Kom2go.Services;

namespace Kom2go.ViewModels;

/// <summary>
/// View model for the library page
/// </summary>
public partial class LibraryViewModel : BaseViewModel
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
    private async Task ImportFileAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a comic file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/x-cbz", "application/x-cbr", "application/zip", "application/x-rar-compressed" } },
                    { DevicePlatform.WinUI, new[] { ".cbz", ".cbr" } },
                })
            });

            if (result is not null)
            {
                var comic = await _libraryService.ImportLocalComicAsync(result.FullPath);
                if (!Comics.Any(c => c.Id == comic.Id))
                {
                    Comics.Insert(0, comic);
                }
            }
        }, "Failed to import comic file");
    }

    [RelayCommand]
    private async Task OpenComicAsync(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"reader?id={comic.Id}");
    }

    [RelayCommand]
    private async Task DeleteComicAsync(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Comic",
            $"Are you sure you want to delete '{comic.Title}'?",
            "Delete",
            "Cancel");

        if (confirm)
        {
            await ExecuteAsync(async () =>
            {
                await _libraryService.DeleteComicAsync(comic);
                Comics.Remove(comic);
                RecentComics.Remove(comic);
                InProgressComics.Remove(comic);
            }, "Failed to delete comic");
        }
    }

    [RelayCommand]
    private async Task ViewComicDetailsAsync(Comic? comic)
    {
        if (comic is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"details?id={comic.Id}");
    }
}
