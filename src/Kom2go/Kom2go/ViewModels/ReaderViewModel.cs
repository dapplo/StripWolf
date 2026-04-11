using Avalonia.Media.Imaging;
using Avalonia.Controls;
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
    private readonly PanelDetectionService _panelDetectionService;
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private int _comicId;

    [ObservableProperty]
    private Comic? _comic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    [NotifyPropertyChangedFor(nameof(IsFirstPage))]
    [NotifyPropertyChangedFor(nameof(IsLastPage))]
    private int _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftColumnWidth))]
    [NotifyPropertyChangedFor(nameof(RightColumnWidth))]
    private Bitmap? _currentPageImage;

    [ObservableProperty]
    private Bitmap? _leftPageImage;

    [ObservableProperty]
    private Bitmap? _rightPageImage;

    [ObservableProperty]
    private bool _isControlsVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomDisplay))]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private bool _isFullScreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StretchModeIcon))]
    private StretchMode _stretchMode = StretchMode.FitPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TwoPageModeIcon))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    [NotifyPropertyChangedFor(nameof(MaxSliderValue))]
    private bool _isTwoPageMode;

    private bool _isLoadingPage;
    private int _lastLoadedPageIndex = -1;
    private bool _shouldSelectLastPanel;

    public bool HasPreviousPage => CurrentPage > 0;
    public bool HasNextPage => Comic is not null && CurrentPage < Comic.PageCount - 1;
    public bool IsFirstPage => CurrentPage == 0;
    public bool IsLastPage => Comic is not null && CurrentPage == Comic.PageCount - 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthorsDisplay))]
    private ComicInfo? _comicInfo;

    [ObservableProperty]
    private bool _isInfoPanelVisible;

    public string FormattedFileSize => Comic is null ? "" : FormatBytes(Comic.FileSize);
    public string SourceDisplayName => Comic?.Source.ToString() ?? "Unknown";
    public bool IsFromKomga => Comic?.Source == ComicSource.Komga;
    public string Location => Comic?.FilePath ?? "";

    public string AuthorsDisplay => ComicInfo?.GetAuthors() ?? Comic?.Authors ?? "Unknown";

    private static string FormatBytes(long bytes)
    {
        string[] suffix = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0) return "0 B";
        int i = (int)Math.Floor(Math.Log(bytes, 1024));
        if (i >= suffix.Length) i = suffix.Length - 1;
        return $"{bytes / Math.Pow(1024, i):0.##} {suffix[i]}";
    }
    
    // Reading mode properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingModeIcon))]
    [NotifyPropertyChangedFor(nameof(IsZoomedMode))]
    [NotifyPropertyChangedFor(nameof(IsGuidedMode))]
    [NotifyPropertyChangedFor(nameof(IsNormalMode))]
    [NotifyPropertyChangedFor(nameof(CanUseTwoPageMode))]
    [NotifyPropertyChangedFor(nameof(NextReadingModeIcon))]
    private ReadingMode _readingMode = ReadingMode.Normal;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewOnLeft))]
    [NotifyPropertyChangedFor(nameof(LeftColumnWidth))]
    [NotifyPropertyChangedFor(nameof(RightColumnWidth))]
    private Handedness _handedness = Handedness.RightHanded;
    
    [ObservableProperty]
    private ZoomRegion _zoomRegion = new();
    
    [ObservableProperty]
    private PagePanelInfo? _currentPagePanels;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousPanel))]
    [NotifyPropertyChangedFor(nameof(HasNextPanel))]
    [NotifyPropertyChangedFor(nameof(CurrentPanelDisplay))]
    private int _currentPanelIndex;
    
    [ObservableProperty]
    private ComicPanel? _currentPanel;
    
    partial void OnCurrentPanelChanged(ComicPanel? value)
    {
        if (value != null && ReadingMode == ReadingMode.Guided)
        {
            SetZoomRegionToPanel(value);
        }
    }
    
    [ObservableProperty]
    private bool _isDetectingPanels;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftColumnWidth))]
    [NotifyPropertyChangedFor(nameof(RightColumnWidth))]
    private bool _compactOverview;

    public GridLength LeftColumnWidth => IsOverviewOnLeft ? OverviewGridLength : ZoomGridLength;
    public GridLength RightColumnWidth => IsOverviewOnLeft ? ZoomGridLength : OverviewGridLength;

    private GridLength OverviewGridLength => CompactOverview ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
    private GridLength ZoomGridLength => new GridLength(1, GridUnitType.Star);

    public bool IsNormalMode => ReadingMode == ReadingMode.Normal;
    public bool IsZoomedMode => ReadingMode == ReadingMode.Zoomed;
    public bool IsGuidedMode => ReadingMode == ReadingMode.Guided;
    
    public bool CanUseTwoPageMode => ReadingMode == ReadingMode.Normal;
    public bool IsOverviewOnLeft => Handedness == Handedness.RightHanded;
    
    public bool HasPreviousPanel => CurrentPanelIndex > 0 || HasPreviousPage;
    public bool HasNextPanel => 
        (CurrentPagePanels is not null && CurrentPanelIndex < CurrentPagePanels.Panels.Count - 1) || 
        HasNextPage;
    
    public string CurrentPanelDisplay => 
        CurrentPagePanels is not null && CurrentPagePanels.Panels.Count > 0
            ? $"Panel {CurrentPanelIndex + 1}/{CurrentPagePanels.Panels.Count}"
            : "";
    
    public string ReadingModeIcon => ReadingMode switch
    {
        ReadingMode.Normal => "▯",
        ReadingMode.Zoomed => "🔍",
        ReadingMode.Guided => "⊞",
        _ => "▯"
    };

    public string NextReadingModeIcon => ReadingMode switch
    {
        ReadingMode.Normal => "🔍",
        ReadingMode.Zoomed => "⊞",
        ReadingMode.Guided => "▯",
        _ => "🔍"
    };
    
    public string PageDisplay
    {
        get
        {
            if (Comic is null) return "";
            if (IsTwoPageMode && CurrentPage + 1 < Comic.PageCount)
            {
                return $"{CurrentPage + 1}-{CurrentPage + 2} / {Comic.PageCount}";
            }
            return $"{CurrentPage + 1} / {Comic.PageCount}";
        }
    }
    
    public string ZoomDisplay => $"{ZoomLevel:P0}";

    public string StretchModeIcon => StretchMode switch
    {
        StretchMode.FitPage => "↔",
        StretchMode.FitWidth => "↕",
        StretchMode.FitHeight => "1:1",
        StretchMode.Original => "▣",
        _ => "▣"
    };

    public string TwoPageModeIcon => IsTwoPageMode ? "▯" : "📖";
    
    public int MaxSliderValue => Comic?.PageCount - 1 ?? 0;


    /// <summary>
    /// Event raised when the reader should be closed
    /// </summary>
    public event EventHandler? CloseRequested;

    public ReaderViewModel(
        LibraryService libraryService, 
        ComicReaderService comicReaderService,
        KomgaApiService komgaApiService,
        PanelDetectionService panelDetectionService,
        SettingsService settingsService)
    {
        _libraryService = libraryService;
        _comicReaderService = comicReaderService;
        _komgaApiService = komgaApiService;
        _panelDetectionService = panelDetectionService;
        _settingsService = settingsService;
        Title = "Reader";
    }

    public async Task LoadComicAsync(int comicId)
    {
        ComicId = comicId;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            
            // Load reading mode preferences
            var settings = _settingsService.LoadSettings();
            ReadingMode = settings.PreferredReadingMode;
            Handedness = settings.Handedness;
            CompactOverview = settings.CompactOverview;
            ZoomRegion = new ZoomRegion { Size = settings.DefaultZoomRegionSize };
            
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

        while (_lastLoadedPageIndex != CurrentPage)
        {
            // Capture current index
            int pageIndex = CurrentPage;

            // Validate page index is within range
            if (Comic.PageCount == 0)
            {
                ErrorMessage = "Comic has no pages";
                return;
            }

            _isLoadingPage = true;
            IsBusy = true;
            try
            {
                // Store page data for panel detection in guided mode
                byte[]? pageData = null;
                
                if (IsTwoPageMode)
                {
                    // Load two pages for two-page mode
                    var leftPageData = await _comicReaderService.GetPageAsync(Comic.FilePath, pageIndex);
                    using var leftStream = new MemoryStream(leftPageData);
                    var newLeftBitmap = new Bitmap(leftStream);
                    var oldLeftBitmap = LeftPageImage;
                    LeftPageImage = newLeftBitmap;
                    oldLeftBitmap?.Dispose();
                    
                    // Also update the single page image for consistency
                    CurrentPageImage = LeftPageImage;
                    pageData = leftPageData;
                    
                    // Load right page if available
                    if (pageIndex + 1 < Comic.PageCount)
                    {
                        var rightPageData = await _comicReaderService.GetPageAsync(Comic.FilePath, pageIndex + 1);
                        using var rightStream = new MemoryStream(rightPageData);
                        var newRightBitmap = new Bitmap(rightStream);
                        var oldRightBitmap = RightPageImage;
                        RightPageImage = newRightBitmap;
                        oldRightBitmap?.Dispose();
                    }
                    else
                    {
                        var oldRightBitmap = RightPageImage;
                        RightPageImage = null;
                        oldRightBitmap?.Dispose();
                    }
                }
                else
                {
                    // Single page mode
                    pageData = await _comicReaderService.GetPageAsync(Comic.FilePath, pageIndex);
                    using var stream = new MemoryStream(pageData);
                    
                    // Create new bitmap first, then dispose old one to avoid memory leak
                    var newBitmap = new Bitmap(stream);
                    var oldBitmap = CurrentPageImage;
                    CurrentPageImage = newBitmap;
                    oldBitmap?.Dispose();
                }
                
                // If in guided mode, detect panels
                if (ReadingMode == ReadingMode.Guided && pageData is not null)
                {
                    await DetectPanelsForCurrentPageAsync(pageData, pageIndex);
                }
                
                // Pre-detect panels for next page in background if in guided mode
                if (ReadingMode == ReadingMode.Guided && pageIndex + 1 < Comic.PageCount)
                {
                    _ = PreDetectNextPagePanelsAsync(pageIndex + 1);
                }

                _lastLoadedPageIndex = pageIndex;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load page: {ex.Message}";
                _lastLoadedPageIndex = pageIndex; // Prevent infinite loop on error
            }
            finally
            {
                _isLoadingPage = false;
                IsBusy = false;
            }
        }
    }

    partial void OnCurrentPageChanged(int value)
    {
        // Only trigger page load when not already loading (to avoid loops)
        if (!_isLoadingPage && Comic is not null)
        {
            _ = LoadAndSaveProgressAsync();
        }
    }
    
    private async Task LoadAndSaveProgressAsync()
    {
        await LoadPageAsync();
        await SaveProgressAsync();
    }

    partial void OnIsTwoPageModeChanged(bool value)
    {
        // Reload pages when switching modes
        if (Comic is not null && !_isLoadingPage)
        {
            _ = LoadPageAsync();
        }
    }

    [RelayCommand]
    private async Task GoToPreviousPageAsync()
    {
        if (!HasPreviousPage)
        {
            return;
        }

        // In two-page mode, go back 2 pages
        var step = IsTwoPageMode ? 2 : 1;
        CurrentPage = Math.Max(0, CurrentPage - step);
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

        // In two-page mode, advance 2 pages
        var step = IsTwoPageMode ? 2 : 1;
        if (Comic is not null)
        {
            CurrentPage = Math.Min(Comic.PageCount - 1, CurrentPage + step);
        }
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
    private void ToggleTwoPageMode()
    {
        // Two-page mode is not available in zoomed or guided modes
        if (!IsNormalMode)
        {
            return;
        }
        IsTwoPageMode = !IsTwoPageMode;
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

    [RelayCommand]
    private async Task ToggleInfoPanelAsync()
    {
        if (IsInfoPanelVisible)
        {
            IsInfoPanelVisible = false;
            return;
        }

        // Load ComicInfo if not already loaded
        if (ComicInfo is null && Comic is not null)
        {
            try
            {
                ComicInfo = await _libraryService.GetComicInfoAsync(Comic.FilePath);
            }
            catch
            {
                // ComicInfo not available
            }
        }

        IsInfoPanelVisible = true;
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
    
    #region Reading Mode Methods
    
    /// <summary>
    /// Cycle through reading modes (Normal -> Zoomed -> Guided -> Normal)
    /// </summary>
    [RelayCommand]
    private async Task CycleReadingModeAsync()
    {
        ReadingMode = ReadingMode switch
        {
            ReadingMode.Normal => ReadingMode.Zoomed,
            ReadingMode.Zoomed => ReadingMode.Guided,
            ReadingMode.Guided => ReadingMode.Normal,
            _ => ReadingMode.Normal
        };
        
        // Disable two-page mode when switching to zoomed or guided
        if (!IsNormalMode && IsTwoPageMode)
        {
            IsTwoPageMode = false;
        }
        
        // If switching to guided mode, detect panels
        if (ReadingMode == ReadingMode.Guided && Comic is not null)
        {
            var pageData = await _comicReaderService.GetPageAsync(Comic.FilePath, CurrentPage);
            await DetectPanelsForCurrentPageAsync(pageData, CurrentPage);
        }
        
        // Notify properties that depend on reading mode
        OnPropertyChanged(nameof(HasPreviousPanel));
        OnPropertyChanged(nameof(HasNextPanel));
        OnPropertyChanged(nameof(CurrentPanelDisplay));
    }
    
    /// <summary>
    /// Set reading mode directly
    /// </summary>
    [RelayCommand]
    private async Task SetReadingModeAsync(ReadingMode mode)
    {
        if (ReadingMode == mode)
        {
            return;
        }
        
        ReadingMode = mode;
        
        // Disable two-page mode when switching to zoomed or guided
        if (!IsNormalMode && IsTwoPageMode)
        {
            IsTwoPageMode = false;
        }
        
        // If switching to guided mode, detect panels
        if (mode == ReadingMode.Guided && Comic is not null)
        {
            var pageData = await _comicReaderService.GetPageAsync(Comic.FilePath, CurrentPage);
            await DetectPanelsForCurrentPageAsync(pageData, CurrentPage);
        }
    }
    
    /// <summary>
    /// Toggle handedness (swap overview and zoom areas)
    /// </summary>
    [RelayCommand]
    private void ToggleHandedness()
    {
        Handedness = Handedness == Handedness.RightHanded 
            ? Handedness.LeftHanded 
            : Handedness.RightHanded;
    }
    
    partial void OnReadingModeChanged(ReadingMode value)
    {
        // Clear panel info when leaving guided mode
        if (value != ReadingMode.Guided)
        {
            CurrentPagePanels = null;
            CurrentPanel = null;
            CurrentPanelIndex = 0;
        }
    }
    
    #endregion
    
    #region Panel Detection Methods
    
    /// <summary>
    /// Detect panels for the current page
    /// </summary>
    private async Task DetectPanelsForCurrentPageAsync(byte[] pageData, int pageIndex)
    {
        if (Comic is null)
        {
            return;
        }
        
        IsDetectingPanels = true;
        try
        {
            var isManga = ComicInfo?.Manga == YesNo.Yes;
            var result = await _panelDetectionService.DetectPanelsAsync(
                Comic.FilePath, 
                pageIndex, 
                pageData,
                isManga);
            
            // Only apply if the page hasn't changed since we started detection
            if (pageIndex == CurrentPage)
            {
                CurrentPagePanels = result;
                
                // Reset to correct panel
                if (_shouldSelectLastPanel && CurrentPagePanels.Panels.Count > 0)
                {
                    CurrentPanelIndex = CurrentPagePanels.Panels.Count - 1;
                }
                else
                {
                    CurrentPanelIndex = 0;
                }
                _shouldSelectLastPanel = false;

                if (CurrentPagePanels.Panels.Count > 0)
                {
                    CurrentPanel = CurrentPagePanels.Panels[CurrentPanelIndex];
                }
                else
                {
                    CurrentPanel = null;
                }
                
                OnPropertyChanged(nameof(HasPreviousPanel));
                OnPropertyChanged(nameof(HasNextPanel));
                OnPropertyChanged(nameof(CurrentPanelDisplay));
            }
        }
        finally
        {
            // Only reset IsDetectingPanels if we are still on that page
            if (pageIndex == CurrentPage)
            {
                IsDetectingPanels = false;
            }
        }
    }
    
    /// <summary>
    /// Pre-detect panels for the next page in background
    /// </summary>
    private async Task PreDetectNextPagePanelsAsync(int nextPageIndex)
    {
        if (Comic is null || nextPageIndex >= Comic.PageCount)
        {
            return;
        }
        
        // Check if already cached
        if (_panelDetectionService.IsCached(Comic.FilePath, nextPageIndex))
        {
            return;
        }
        
        try
        {
            var isManga = ComicInfo?.Manga == YesNo.Yes;
            var nextPageData = await _comicReaderService.GetPageAsync(Comic.FilePath, nextPageIndex);
            await _panelDetectionService.DetectPanelsAsync(Comic.FilePath, nextPageIndex, nextPageData, isManga);
        }
        catch
        {
            // Silently fail - this is just pre-caching
        }
    }
    
    #endregion
    
    #region Panel Navigation Methods
    
    /// <summary>
    /// Navigate to the next panel (or next page if at last panel)
    /// </summary>
    [RelayCommand]
    private async Task GoToNextPanelAsync()
    {
        if (CurrentPagePanels is null || CurrentPagePanels.Panels.Count == 0)
        {
            // No panels, just go to next page
            await GoToNextPageAsync();
            return;
        }
        
        if (CurrentPanelIndex < CurrentPagePanels.Panels.Count - 1)
        {
            // Go to next panel on same page
            CurrentPanelIndex++;
            CurrentPanel = CurrentPagePanels.Panels[CurrentPanelIndex];
            OnPropertyChanged(nameof(HasPreviousPanel));
            OnPropertyChanged(nameof(HasNextPanel));
            OnPropertyChanged(nameof(CurrentPanelDisplay));
        }
        else if (HasNextPage)
        {
            // Go to first panel of next page
            await GoToNextPageAsync();
            CurrentPanelIndex = 0;
            if (CurrentPagePanels?.Panels.Count > 0)
            {
                CurrentPanel = CurrentPagePanels.Panels[0];
            }
        }
    }
    
    /// <summary>
    /// Navigate to the previous panel (or previous page if at first panel)
    /// </summary>
    [RelayCommand]
    private async Task GoToPreviousPanelAsync()
    {
        if (CurrentPagePanels is null || CurrentPagePanels.Panels.Count == 0)
        {
            // No panels, just go to previous page
            _shouldSelectLastPanel = true;
            await GoToPreviousPageAsync();
            return;
        }
        
        if (CurrentPanelIndex > 0)
        {
            // Go to previous panel on same page
            CurrentPanelIndex--;
            CurrentPanel = CurrentPagePanels.Panels[CurrentPanelIndex];
            OnPropertyChanged(nameof(HasPreviousPanel));
            OnPropertyChanged(nameof(HasNextPanel));
            OnPropertyChanged(nameof(CurrentPanelDisplay));
        }
        else if (HasPreviousPage)
        {
            // Go to last panel of previous page
            _shouldSelectLastPanel = true;
            await GoToPreviousPageAsync();
        }
    }
    
    /// <summary>
    /// Select a specific panel by index
    /// </summary>
    [RelayCommand]
    private void SelectPanel(int panelIndex)
    {
        if (CurrentPagePanels is null || panelIndex < 0 || panelIndex >= CurrentPagePanels.Panels.Count)
        {
            return;
        }
        
        CurrentPanelIndex = panelIndex;
        CurrentPanel = CurrentPagePanels.Panels[panelIndex];
        OnPropertyChanged(nameof(HasPreviousPanel));
        OnPropertyChanged(nameof(HasNextPanel));
        OnPropertyChanged(nameof(CurrentPanelDisplay));
    }
    
    #endregion
    
    #region Zoom Region Methods
    
    /// <summary>
    /// Move the zoom region by delta amounts
    /// </summary>
    public void MoveZoomRegion(double deltaX, double deltaY)
    {
        ZoomRegion.Move(deltaX, deltaY);
        OnPropertyChanged(nameof(ZoomRegion));
    }
    
    /// <summary>
    /// Resize the zoom region
    /// </summary>
    [RelayCommand]
    private void ResizeZoomRegion(double sizeDelta)
    {
        ZoomRegion.Resize(sizeDelta);
        OnPropertyChanged(nameof(ZoomRegion));
    }
    
    /// <summary>
    /// Increase zoom region size
    /// </summary>
    [RelayCommand]
    private void IncreaseZoomRegionSize()
    {
        ZoomRegion.Resize(0.05);
        OnPropertyChanged(nameof(ZoomRegion));
    }
    
    /// <summary>
    /// Decrease zoom region size
    /// </summary>
    [RelayCommand]
    private void DecreaseZoomRegionSize()
    {
        ZoomRegion.Resize(-0.05);
        OnPropertyChanged(nameof(ZoomRegion));
    }
    
    /// <summary>
    /// Reset zoom region to center with default size
    /// </summary>
    [RelayCommand]
    private void ResetZoomRegion()
    {
        ZoomRegion = new ZoomRegion();
        OnPropertyChanged(nameof(ZoomRegion));
    }
    
    /// <summary>
    /// Set zoom region to match a panel's bounds
    /// </summary>
    public void SetZoomRegionToPanel(ComicPanel panel)
    {
        ZoomRegion = new ZoomRegion
        {
            CenterX = panel.X + panel.Width / 2,
            CenterY = panel.Y + panel.Height / 2,
            Width = panel.Width,
            Height = panel.Height
        };
        OnPropertyChanged(nameof(ZoomRegion));
    }
    
    #endregion

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
