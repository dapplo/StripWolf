using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kom2go.Models;
using Kom2go.Services;

namespace Kom2go.ViewModels;

/// <summary>
/// View model for the comic reader page
/// </summary>
[QueryProperty(nameof(ComicId), "id")]
public partial class ReaderViewModel : BaseViewModel
{
    private readonly LibraryService _libraryService;
    private readonly ComicReaderService _comicReaderService;

    [ObservableProperty]
    private int _comicId;

    [ObservableProperty]
    private Comic? _comic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    private int _currentPage;

    [ObservableProperty]
    private ImageSource? _currentPageImage;

    [ObservableProperty]
    private bool _isControlsVisible = true;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    public bool HasPreviousPage => CurrentPage > 0;
    public bool HasNextPage => Comic is not null && CurrentPage < Comic.PageCount - 1;
    public string PageDisplay => Comic is null ? "" : $"{CurrentPage + 1} / {Comic.PageCount}";

    public ReaderViewModel(LibraryService libraryService, ComicReaderService comicReaderService)
    {
        _libraryService = libraryService;
        _comicReaderService = comicReaderService;
        Title = "Reader";
    }

    partial void OnComicIdChanged(int value)
    {
        _ = LoadComicAsync();
    }

    [RelayCommand]
    private async Task LoadComicAsync()
    {
        if (ComicId <= 0)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            Comic = await _libraryService.GetComicAsync(ComicId);
            if (Comic is not null)
            {
                Title = Comic.Title;
                CurrentPage = Comic.CurrentPage;
                await LoadPageAsync();
            }
        }, "Failed to load comic");
    }

    private async Task LoadPageAsync()
    {
        if (Comic is null)
        {
            return;
        }

        try
        {
            var pageData = await _comicReaderService.GetPageAsync(Comic.FilePath, CurrentPage);
            CurrentPageImage = ImageSource.FromStream(() => new MemoryStream(pageData));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load page: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task GoToPreviousPageAsync()
    {
        if (!HasPreviousPage)
        {
            return;
        }

        CurrentPage--;
        await LoadPageAsync();
        await SaveProgressAsync();
    }

    [RelayCommand]
    private async Task GoToNextPageAsync()
    {
        if (!HasNextPage)
        {
            return;
        }

        CurrentPage++;
        await LoadPageAsync();
        await SaveProgressAsync();
    }

    [RelayCommand]
    private async Task GoToPageAsync(int page)
    {
        if (Comic is null || page < 0 || page >= Comic.PageCount)
        {
            return;
        }

        CurrentPage = page;
        await LoadPageAsync();
        await SaveProgressAsync();
    }

    [RelayCommand]
    private void ToggleControls()
    {
        IsControlsVisible = !IsControlsVisible;
    }

    [RelayCommand]
    private void ZoomIn()
    {
        if (ZoomLevel < 3.0)
        {
            ZoomLevel += 0.25;
        }
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomLevel > 0.5)
        {
            ZoomLevel -= 0.25;
        }
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomLevel = 1.0;
    }

    private async Task SaveProgressAsync()
    {
        if (Comic is null)
        {
            return;
        }

        try
        {
            await _libraryService.UpdateReadingProgressAsync(Comic, CurrentPage);
        }
        catch
        {
            // Silently fail - don't interrupt reading
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await SaveProgressAsync();
        await Shell.Current.GoToAsync("..");
    }
}
