using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kom2go.Models;
using Kom2go.Services;

namespace Kom2go.ViewModels;

/// <summary>
/// View model for the comic reader page
/// </summary>
public partial class ReaderViewModel : ViewModelBase
{
    private readonly LibraryService _libraryService;
    private readonly ComicReaderService _comicReaderService;
    private readonly KomgaApiService _komgaApiService;

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
    private Bitmap? _currentPageImage;

    [ObservableProperty]
    private bool _isControlsVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomDisplay))]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private bool _isFullScreen;

    [ObservableProperty]
    private StretchMode _stretchMode = StretchMode.FitPage;

    private bool _isLoadingPage;

    public bool HasPreviousPage => CurrentPage > 0;
    public bool HasNextPage => Comic is not null && CurrentPage < Comic.PageCount - 1;
    public string PageDisplay => Comic is null ? "" : $"{CurrentPage + 1} / {Comic.PageCount}";
    public string ZoomDisplay => $"{ZoomLevel:P0}";

    /// <summary>
    /// Event raised when the reader should be closed
    /// </summary>
    public event EventHandler? CloseRequested;

    public ReaderViewModel(
        LibraryService libraryService, 
        ComicReaderService comicReaderService,
        KomgaApiService komgaApiService)
    {
        _libraryService = libraryService;
        _comicReaderService = comicReaderService;
        _komgaApiService = komgaApiService;
        Title = "Reader";
    }

    public async Task LoadComicAsync(int comicId)
    {
        ComicId = comicId;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            
            Comic = await _libraryService.GetComicAsync(ComicId);
            if (Comic is not null)
            {
                Title = Comic.Title;
                
                // Sync with Komga if this is a Komga comic
                if (Comic.Source == ComicSource.Komga && !string.IsNullOrEmpty(Comic.KomgaId) && _komgaApiService.IsConfigured)
                {
                    await SyncReadProgressFromKomgaAsync();
                }
                
                _isLoadingPage = true;
                // Ensure CurrentPage is within valid range (0 to PageCount-1)
                var validPage = Math.Max(0, Math.Min(Comic.CurrentPage, Comic.PageCount - 1));
                CurrentPage = Comic.PageCount > 0 ? validPage : 0;
                _isLoadingPage = false;
                await LoadPageAsync();
                
                // Mark as started reading
                await SaveProgressAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load comic: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SyncReadProgressFromKomgaAsync()
    {
        if (Comic is null || string.IsNullOrEmpty(Comic.KomgaId) || !_komgaApiService.IsConfigured)
        {
            return;
        }

        try
        {
            var book = await _komgaApiService.GetBookAsync(Comic.KomgaId);
            if (book?.ReadProgress is not null)
            {
                // Komga uses 1-based page numbers
                var komgaPage = book.ReadProgress.Page - 1;
                // Ensure page is within valid range
                if (komgaPage >= 0 && komgaPage < Comic.PageCount && komgaPage > Comic.CurrentPage)
                {
                    Comic.CurrentPage = komgaPage;
                    Comic.IsCompleted = book.ReadProgress.Completed;
                }
            }
        }
        catch
        {
            // Failed to sync, continue with local progress
        }
    }

    private async Task LoadPageAsync()
    {
        if (Comic is null || _isLoadingPage)
        {
            return;
        }

        // Validate page index is within range
        if (Comic.PageCount == 0)
        {
            ErrorMessage = "Comic has no pages";
            return;
        }

        // Ensure CurrentPage is within valid bounds
        if (CurrentPage < 0 || CurrentPage >= Comic.PageCount)
        {
            CurrentPage = Math.Max(0, Math.Min(CurrentPage, Comic.PageCount - 1));
        }

        _isLoadingPage = true;
        IsBusy = true;
        try
        {
            var pageData = await _comicReaderService.GetPageAsync(Comic.FilePath, CurrentPage);
            using var stream = new MemoryStream(pageData);
            
            // Create new bitmap first, then dispose old one to avoid memory leak
            var newBitmap = new Bitmap(stream);
            var oldBitmap = CurrentPageImage;
            CurrentPageImage = newBitmap;
            oldBitmap?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load page: {ex.Message}";
        }
        finally
        {
            _isLoadingPage = false;
            IsBusy = false;
        }
    }

    partial void OnCurrentPageChanged(int value)
    {
        // Only trigger page load when not already loading (to avoid loops)
        if (!_isLoadingPage && Comic is not null)
        {
            _ = Task.Run(async () =>
            {
                await LoadPageAsync();
                await SaveProgressAsync();
            });
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
        if (ZoomLevel < 5.0)
        {
            ZoomLevel = Math.Min(5.0, ZoomLevel + 0.25);
        }
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomLevel > 0.25)
        {
            ZoomLevel = Math.Max(0.25, ZoomLevel - 0.25);
        }
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomLevel = 1.0;
    }

    /// <summary>
    /// Adjusts zoom level based on scroll delta (for mouse wheel)
    /// </summary>
    public void AdjustZoom(double delta)
    {
        if (delta > 0)
        {
            ZoomIn();
        }
        else if (delta < 0)
        {
            ZoomOut();
        }
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        IsFullScreen = !IsFullScreen;
    }

    [RelayCommand]
    private void CycleStretchMode()
    {
        StretchMode = StretchMode switch
        {
            StretchMode.FitPage => StretchMode.FitWidth,
            StretchMode.FitWidth => StretchMode.FitHeight,
            StretchMode.FitHeight => StretchMode.Original,
            StretchMode.Original => StretchMode.FitPage,
            _ => StretchMode.FitPage
        };
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
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Image stretch/fit modes for the reader
/// </summary>
public enum StretchMode
{
    FitPage,    // Fit entire page in view (maintain aspect ratio)
    FitWidth,   // Fit to width (may scroll vertically)
    FitHeight,  // Fit to height (may scroll horizontally)
    Original    // Original size (100%)
}
