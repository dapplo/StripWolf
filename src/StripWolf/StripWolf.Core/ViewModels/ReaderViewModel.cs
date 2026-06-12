// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Core.Models;
using StripWolf.Core.Resources;
using StripWolf.Core.Services;

namespace StripWolf.Core.ViewModels;

/// <summary>
/// View model for the comic reader page
/// </summary>
public partial class ReaderViewModel : ViewModelBase
{
    private readonly LibraryService _libraryService;
    private readonly ComicReaderService _comicReaderService;
    private readonly KomgaSyncService _komgaSyncService;
    private readonly KomgaApiServiceFactory _komgaApiServiceFactory;
    private readonly PanelDetectionService _panelDetectionService;
    private readonly SettingsService _settingsService;
    private readonly EpubShadowConversionService _epubShadowConversionService;
    private readonly DispatcherTimer _periodicSyncTimer;
    private string? _readerFilePath;
    private CancellationTokenSource? _saveProgressCts;

    [ObservableProperty]
    private int _comicId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvanceOrShowEndChoices))]
    [NotifyPropertyChangedFor(nameof(CanViewSeriesOnKomgaOption))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    [NotifyPropertyChangedFor(nameof(IsFirstPage))]
    [NotifyPropertyChangedFor(nameof(IsLastPage))]
    [NotifyPropertyChangedFor(nameof(MaxSliderValue))]
    [NotifyPropertyChangedFor(nameof(ComicCoverPath))]
    [NotifyPropertyChangedFor(nameof(ComicTitleDisplay))]
    [NotifyPropertyChangedFor(nameof(ComicNumberDisplay))]
    [NotifyPropertyChangedFor(nameof(ComicPageCountDisplay))]
    [NotifyPropertyChangedFor(nameof(HasKomgaSeriesLink))]
    [NotifyPropertyChangedFor(nameof(IsFavorite))]
    [NotifyPropertyChangedFor(nameof(KomgaSyncStatus))]
    [NotifyPropertyChangedFor(nameof(KomgaSyncPromptMessage))]
    [NotifyPropertyChangedFor(nameof(FormattedFileSize))]
    [NotifyPropertyChangedFor(nameof(ComicFormatDisplay))]
    private Comic? _comic;

    public string? KomgaSyncStatus => Comic?.KomgaSyncStatus;

    private int _currentPage;

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_isInitializingComic && value != _currentPage)
            {
                return;
            }
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(HasPreviousPage));
                OnPropertyChanged(nameof(HasNextPage));
                OnPropertyChanged(nameof(PageDisplay));
                OnPropertyChanged(nameof(IsFirstPage));
                OnPropertyChanged(nameof(IsLastPage));
                OnCurrentPageChanged(value);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(IsLastPage))]
    [NotifyPropertyChangedFor(nameof(CanAdvanceOrShowEndChoices))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    private bool _hasPendingEpubConversion;

    [ObservableProperty]
    private string? _readerStatusMessage;

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

    partial void OnStretchModeChanged(StretchMode value)
    {
        // Reset manual zoom when switching fit modes
        ZoomLevel = 1.0;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TwoPageModeIcon))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    [NotifyPropertyChangedFor(nameof(MaxSliderValue))]
    private bool _isTwoPageMode;

    private bool _isLoadingPage;
    private bool _isInitializingComic;
    private int _lastLoadedPageIndex = -1;
    private bool _shouldSelectLastPanel;
    private long _epubConversionUpdateVersion;
    private CancellationTokenSource? _pageLoadingCts;

    // Static semaphore to limit concurrent bitmap decodes globally in the reader
    // This prevents memory spikes during rapid page flipping
    private static readonly SemaphoreSlim GlobalDecodeSemaphore = new(2, 2);

    // Pre-decoded bitmap cache for instant page display without loading bar
    private readonly Dictionary<int, Bitmap> _bitmapPrefetchCache = new();
    private readonly object _bitmapPrefetchLock = new();
    private const int MaxPrefetchedBitmaps = 3; // Reverted from 2 for better speed

    public bool HasPreviousPage => CanMoveWithinBounds(-GetNavigationDirectionSign());
    public bool HasNextPage =>
        CanMoveWithinBounds(GetNavigationDirectionSign()) ||
        (GetNavigationDirectionSign() > 0 && Comic is not null && HasPendingEpubConversion && CurrentPage >= Comic.PageCount - 1);
    public bool IsFirstPage => Comic is null || Comic.PageCount <= 0 || CurrentPage == GetReadingStartPageIndex();
    public bool IsLastPage => Comic is not null && Comic.PageCount > 0 && !HasPendingEpubConversion && CurrentPage == GetReadingEndPageIndex();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthorsDisplay))]
    [NotifyPropertyChangedFor(nameof(SeriesDisplay))]
    [NotifyPropertyChangedFor(nameof(ComicInfoPublisher))]
    [NotifyPropertyChangedFor(nameof(ComicInfoGenre))]
    [NotifyPropertyChangedFor(nameof(ComicInfoLanguageIso))]
    [NotifyPropertyChangedFor(nameof(ComicInfoTags))]
    [NotifyPropertyChangedFor(nameof(ComicInfoSummary))]
    private ComicInfo? _comicInfo;

    [ObservableProperty]
    private bool _isInfoPanelVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextSeriesComic))]
    [NotifyPropertyChangedFor(nameof(NextSeriesComicTitle))]
    private Comic? _nextSeriesComic;

    [ObservableProperty]
    private bool _isEndOfComicOptionsVisible;

    [ObservableProperty]
    private bool _showKomgaSyncLocationPrompt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KomgaSyncPromptMessage))]
    private int _pendingKomgaSyncPage = -1;

    [ObservableProperty]
    private bool _pendingKomgaSyncCompleted;

    [ObservableProperty]
    private DateTime? _pendingKomgaSyncLastModified;

    public string FormattedFileSize => Comic is null ? "" : FormatBytes(Comic.FileSize);
    public string ComicFormatDisplay => Comic?.FormatDisplay ?? string.Empty;
    public string SourceDisplayName => Comic?.Source.ToString() ?? "Unknown";
    public bool IsFromKomga => Comic?.Source == ComicSource.Komga;
    public string Location => Comic?.FilePath ?? "";
    public string? ComicCoverPath => Comic?.CoverPath;
    public string ComicTitleDisplay => Comic?.Title ?? string.Empty;
    public string ComicNumberDisplay => Comic?.Number?.ToString() ?? string.Empty;
    public string ComicPageCountDisplay => Comic?.PageCount.ToString() ?? string.Empty;
    public bool CanAdvanceOrShowEndChoices => Comic is not null && (Comic.PageCount > 0 || HasPendingEpubConversion);
    public bool CanViewSeriesOnKomgaOption => !string.IsNullOrEmpty(Comic?.KomgaSeriesId);
    public bool HasKomgaSeriesLink => !string.IsNullOrEmpty(Comic?.KomgaSeriesId);
    public bool IsFavorite => Comic?.IsFavorite ?? false;
    public bool HasNextSeriesComic => NextSeriesComic is not null;
    public string NextSeriesComicTitle => NextSeriesComic?.Title ?? string.Empty;
    public string KomgaSyncPromptMessage => Comic is null || PendingKomgaSyncPage < 0
        ? string.Empty
        : string.Format(
            Loc.Instance.KomgaSyncPromptMessage,
            Comic.PageCount <= 0
                ? PendingKomgaSyncPage + 1
                : ToDisplayPageNumber(Math.Min(PendingKomgaSyncPage, Comic.PageCount - 1)),
            Comic.PageCount);

    public string AuthorsDisplay => ComicInfo?.GetAuthors() ?? Comic?.Authors ?? "Unknown";

    public string? SeriesDisplay => !string.IsNullOrEmpty(ComicInfo?.Series) ? ComicInfo.Series : Comic?.SeriesName;
    public string? ComicInfoPublisher => ComicInfo?.Publisher;
    public string? ComicInfoGenre => ComicInfo?.Genre;
    public string? ComicInfoLanguageIso => ComicInfo?.LanguageISO;
    public string? ComicInfoTags => ComicInfo?.Tags;
    public string? ComicInfoSummary => ComicInfo?.Summary;

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

    partial void OnReadingModeChanged(ReadingMode value)
    {
        // Reset zoom and stretch when switching modes
        ZoomLevel = 1.0;
        if (value == ReadingMode.Normal)
        {
            StretchMode = StretchMode.FitPage;
        }

        // Clear panel info when leaving guided mode
        if (value != ReadingMode.Guided)
        {
            CurrentPagePanels = null;
            CurrentPanel = null;
            CurrentPanelIndex = 0;
        }
    }

    partial void OnSelectedReadingDirectionModeOptionChanged(ReadingDirectionModeOption? value)
    {
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(PageDisplay));
        OnPropertyChanged(nameof(IsFirstPage));
        OnPropertyChanged(nameof(IsLastPage));
        OnPropertyChanged(nameof(EffectiveReadingDirectionMode));
        OnPropertyChanged(nameof(IsRightToLeftNavigation));
        OnPropertyChanged(nameof(PreviousPageButtonGlyph));
        OnPropertyChanged(nameof(NextPageButtonGlyph));
        if (value?.Value == ReadingDirectionMode.Automatic && Comic is not null)
        {
            _ = ResolveAutomaticReadingDirectionAsync();
        }
        if (ReadingMode == ReadingMode.Guided && Comic is not null)
        {
            _ = RefreshCurrentPagePanelsAsync();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewOnLeft))]
    [NotifyPropertyChangedFor(nameof(LeftColumnWidth))]
    [NotifyPropertyChangedFor(nameof(RightColumnWidth))]
    private Handedness _handedness = Handedness.RightHanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    [NotifyPropertyChangedFor(nameof(IsFirstPage))]
    [NotifyPropertyChangedFor(nameof(IsLastPage))]
    [NotifyPropertyChangedFor(nameof(EffectiveReadingDirectionMode))]
    [NotifyPropertyChangedFor(nameof(IsRightToLeftNavigation))]
    [NotifyPropertyChangedFor(nameof(PreviousPageButtonGlyph))]
    [NotifyPropertyChangedFor(nameof(NextPageButtonGlyph))]
    private ReadingDirectionModeOption? _selectedReadingDirectionModeOption;

    private ReadingDirectionMode? _detectedReadingDirectionMode;

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

    [ObservableProperty]
    private int _decodeWidth;

    [ObservableProperty]
    private int _decodeHeight;

    partial void OnDecodeWidthChanged(int value) => ClearBitmapPrefetchCache();
    partial void OnDecodeHeightChanged(int value) => ClearBitmapPrefetchCache();

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
        ReadingMode.Normal => "◫",
        ReadingMode.Zoomed => "⌕",
        ReadingMode.Guided => "▦",
        _ => "◫"
    };

    public string NextReadingModeIcon => ReadingMode switch
    {
        ReadingMode.Normal => "⌕",
        ReadingMode.Zoomed => "▦",
        ReadingMode.Guided => "◫",
        _ => "⌕"
    };

    public string PageDisplay
    {
        get
        {
            if (Comic is null) return "";
            if (Comic.PageCount <= 0) return HasPendingEpubConversion ? "Preparing..." : "0 / 0";
            var currentDisplayPage = ToDisplayPageNumber(CurrentPage);
            if (IsTwoPageMode)
            {
                var secondPageIndex = CurrentPage + GetNavigationDirectionSign();
                if (secondPageIndex >= 0 && secondPageIndex < Comic.PageCount)
                {
                    var secondDisplayPage = ToDisplayPageNumber(secondPageIndex);
                    return HasPendingEpubConversion
                        ? $"{currentDisplayPage}-{secondDisplayPage} / {Comic.PageCount}+"
                        : $"{currentDisplayPage}-{secondDisplayPage} / {Comic.PageCount}";
                }
            }

            return HasPendingEpubConversion
                ? $"{currentDisplayPage} / {Comic.PageCount}+"
                : $"{currentDisplayPage} / {Comic.PageCount}";
        }
    }

    public string ZoomDisplay => $"{ZoomLevel:P0}";

    public string StretchModeIcon => StretchMode switch
    {
        StretchMode.FitPage => "⤢",
        StretchMode.FitWidth => "↔",
        StretchMode.FitHeight => "↕",
        StretchMode.Original => "1:1",
        _ => "1:1"
    };

    public string TwoPageModeIcon => IsTwoPageMode ? "▭" : "◫";

    public int MaxSliderValue => Math.Max(0, Comic?.PageCount - 1 ?? 0);

    public bool IsGuidedReadingAvailable => _panelDetectionService.IsAvailable;

    public IReadOnlyList<ReadingDirectionModeOption> AvailableReadingDirectionModes =>
    [
        new(ReadingDirectionMode.Automatic, Loc.Instance.ReadingDirectionAutomatic),
        new(ReadingDirectionMode.LeftToRight, Loc.Instance.ReadingDirectionLeftToRight),
        new(ReadingDirectionMode.RightToLeft, Loc.Instance.ReadingDirectionRightToLeft),
        new(ReadingDirectionMode.LeftToRightReversedPages, Loc.Instance.ReadingDirectionLeftToRightReversedPages),
        new(ReadingDirectionMode.RightToLeftReversedPages, Loc.Instance.ReadingDirectionRightToLeftReversedPages)
    ];

    public ReadingDirectionMode EffectiveReadingDirectionMode => SelectedReadingDirectionModeOption?.Value switch
    {
        null => _detectedReadingDirectionMode ?? ReadingDirectionMode.LeftToRight,
        ReadingDirectionMode.Automatic => _detectedReadingDirectionMode ?? ReadingDirectionMode.LeftToRight,
        var mode => mode ?? ReadingDirectionMode.LeftToRight
    };

    public bool IsRightToLeftNavigation => GetNavigationDirectionSign() < 0;
    public string PreviousPageButtonGlyph => IsRightToLeftNavigation ? "❯" : "❮";
    public string NextPageButtonGlyph => IsRightToLeftNavigation ? "❮" : "❯";
    public int ReadingStartPageIndex => GetReadingStartPageIndex();
    public int ReadingEndPageIndex => GetReadingEndPageIndex();

    public bool IsDebug =>
#if DEBUG
        true;
#else
        false;
#endif

    private ReadingMode NormalizeReadingMode(ReadingMode mode)
    {
        if (!IsGuidedReadingAvailable && mode == ReadingMode.Guided)
        {
            return ReadingMode.Zoomed;
        }

        return mode;
    }

    private int GetNavigationDirectionSign() => EffectiveReadingDirectionMode switch
    {
        ReadingDirectionMode.RightToLeft => -1,
        ReadingDirectionMode.RightToLeftReversedPages => -1,
        _ => 1
    };

    private bool StartsAtBack() => EffectiveReadingDirectionMode switch
    {
        ReadingDirectionMode.RightToLeft => true,
        ReadingDirectionMode.LeftToRightReversedPages => true,
        _ => false
    };

    private int GetReadingStartPageIndex()
    {
        if (Comic is null || Comic.PageCount <= 0)
        {
            return 0;
        }

        return StartsAtBack() ? Comic.PageCount - 1 : 0;
    }

    private int GetReadingEndPageIndex()
    {
        if (Comic is null || Comic.PageCount <= 0)
        {
            return 0;
        }

        return StartsAtBack() ? 0 : Comic.PageCount - 1;
    }

    private bool CanMoveWithinBounds(int delta)
    {
        if (Comic is null || Comic.PageCount <= 0)
        {
            return false;
        }

        var target = CurrentPage + delta;
        return target >= 0 && target < Comic.PageCount;
    }

    private int ToDisplayPageNumber(int pageIndex)
    {
        if (Comic is null || Comic.PageCount <= 0)
        {
            return 0;
        }

        return StartsAtBack() ? Comic.PageCount - pageIndex : pageIndex + 1;
    }

    private bool IsMangaReadingDirection() => GetNavigationDirectionSign() < 0;

    private static ReadingDirectionMode? ParseDirectionValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();

        return normalized switch
        {
            "righttoleft" or "rtl" => ReadingDirectionMode.RightToLeft,
            "lefttoright" or "ltr" => ReadingDirectionMode.LeftToRight,
            _ => null
        };
    }

    private async Task RefreshCurrentPagePanelsAsync()
    {
        if (Comic is null)
        {
            return;
        }

        try
        {
            var pageData = await _comicReaderService.GetPageAsync(GetActiveReadPath(), CurrentPage);
            await DetectPanelsForCurrentPageAsync(pageData, CurrentPage);
        }
        catch
        {
            // Best-effort refresh when direction changes
        }
    }

    private async Task ResolveAutomaticReadingDirectionAsync()
    {
        _detectedReadingDirectionMode = null;
        var comic = Comic;
        if (comic is null || SelectedReadingDirectionModeOption?.Value != ReadingDirectionMode.Automatic)
        {
            return;
        }

        if (comic.Source == ComicSource.Komga && !string.IsNullOrEmpty(comic.KomgaSeriesId) && comic.KomgaServerId.HasValue)
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                var server = settings.Servers.FirstOrDefault(s => s.Id == comic.KomgaServerId.Value);
                if (server is not null)
                {
                    var komgaApiService = _komgaApiServiceFactory.GetForServer(server);
                    var series = await komgaApiService.GetSeriesAsync(comic.KomgaSeriesId);
                    _detectedReadingDirectionMode = ParseDirectionValue(series?.Metadata?.ReadingDirection);
                }
            }
            catch
            {
                // Ignore Komga direction detection errors
            }
        }

        if (_detectedReadingDirectionMode is null)
        {
            if (ComicInfo is null)
            {
                try
                {
                    ComicInfo = await _libraryService.GetComicInfoAsync(comic.FilePath);
                }
                catch
                {
                    // Metadata is optional
                }
            }

            _detectedReadingDirectionMode = ParseDirectionValue(ComicInfo?.PageProgressionDirection);
            if (_detectedReadingDirectionMode is null)
            {
                _detectedReadingDirectionMode = ComicInfo?.Manga switch
                {
                    YesNo.Yes => ReadingDirectionMode.RightToLeft,
                    YesNo.No => ReadingDirectionMode.LeftToRight,
                    _ => null
                };
            }
        }

        OnPropertyChanged(nameof(EffectiveReadingDirectionMode));
        OnPropertyChanged(nameof(IsRightToLeftNavigation));
        OnPropertyChanged(nameof(PreviousPageButtonGlyph));
        OnPropertyChanged(nameof(NextPageButtonGlyph));
    }

    /// <summary>
    /// Event raised when the reader should be closed
    /// </summary>
    public event EventHandler? CloseRequested;

    public ReaderViewModel(
        LibraryService libraryService,
        ComicReaderService comicReaderService,
        KomgaSyncService komgaSyncService,
        KomgaApiServiceFactory komgaApiServiceFactory,
        PanelDetectionService panelDetectionService,
        SettingsService settingsService,
        EpubShadowConversionService epubShadowConversionService)
    {
        _libraryService = libraryService;
        _comicReaderService = comicReaderService;
        _komgaSyncService = komgaSyncService;
        _komgaApiServiceFactory = komgaApiServiceFactory;
        _panelDetectionService = panelDetectionService;
        _settingsService = settingsService;
        _epubShadowConversionService = epubShadowConversionService;
        _epubShadowConversionService.ConversionStateChanged += OnEpubConversionStateChanged;

        _periodicSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _periodicSyncTimer.Tick += async (_, _) => await SyncProgressWithKomgaAsync();

        Title = "Reader";
    }

    private void ReleaseReaderResources()
    {
        _periodicSyncTimer.Stop();
        ClearKomgaSyncPrompt();
        if (Comic is not null && HasPendingEpubConversion)
        {
            _ = _epubShadowConversionService.StopReadingSessionAsync(Comic.Id);
        }

        _isLoadingPage = false;
        _isInitializingComic = false;

        var cts = Interlocked.Exchange(ref _saveProgressCts, null);
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { }
            cts.Dispose();
        }

        ClearAllCaches();
        _lastLoadedPageIndex = -1;
        _readerFilePath = null;
        HasPendingEpubConversion = false;
        ReaderStatusMessage = null;

        var oldCurrentPageImage = CurrentPageImage;
        CurrentPageImage = null;

        var oldLeftPageImage = LeftPageImage;
        LeftPageImage = null;

        var oldRightPageImage = RightPageImage;
        RightPageImage = null;

        oldCurrentPageImage?.Dispose();
        if (!ReferenceEquals(oldLeftPageImage, oldCurrentPageImage))
        {
            oldLeftPageImage?.Dispose();
        }
        if (!ReferenceEquals(oldRightPageImage, oldCurrentPageImage) &&
            !ReferenceEquals(oldRightPageImage, oldLeftPageImage))
        {
            oldRightPageImage?.Dispose();
        }

        CurrentPagePanels = null;
        CurrentPanel = null;
        CurrentPanelIndex = 0;
        IsDetectingPanels = false;
        _detectedReadingDirectionMode = null;
    }

    private void ClearAllCaches()
    {
        _panelDetectionService.ClearAllCache();
        _comicReaderService.ClearCache();
        ClearBitmapPrefetchCache();

        // Force ImageSharp to release its internal memory pools
        SixLabors.ImageSharp.Configuration.Default.MemoryAllocator.ReleaseRetainedResources();

        // Suggest a collection to the runtime
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private string GetActiveReadPath()
    {
        return _readerFilePath ?? Comic?.FilePath ?? string.Empty;
    }

    private async void OnEpubConversionStateChanged(object? sender, int comicId)
    {
        if (Comic?.Id != comicId)
        {
            return;
        }

        var updateVersion = Interlocked.Increment(ref _epubConversionUpdateVersion);
        var refreshedComic = await _libraryService.GetComicAsync(comicId);
        var state = await _libraryService.GetEpubConversionStateAsync(comicId);
        if (updateVersion != Interlocked.Read(ref _epubConversionUpdateVersion))
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Comic?.Id != comicId || updateVersion != Interlocked.Read(ref _epubConversionUpdateVersion))
            {
                return;
            }

            var previousReadPath = GetActiveReadPath();
            if (refreshedComic is not null)
            {
                Comic.FilePath = refreshedComic.FilePath;
                Comic.Format = refreshedComic.Format;
                Comic.PageCount = refreshedComic.PageCount;
                Comic.FileSize = refreshedComic.FileSize;
                Title = refreshedComic.Title;
            }

            ApplyEpubConversionState(state);
            InvalidateReaderSourceIfChanged(previousReadPath);
        });
    }

    private void ApplyEpubConversionState(EpubConversionState? state)
    {
        HasPendingEpubConversion = state is not null;
        _readerFilePath = state?.ShadowPath ?? Comic?.FilePath;
        if (Comic is not null && state is not null)
        {
            Comic.PageCount = state.ProducedPageCount;
            ReaderStatusMessage = state.Status switch
            {
                EpubConversionStatus.Failed => state.LastError,
                _ => null
            };
        }
        else
        {
            ReaderStatusMessage = null;
        }

        OnPropertyChanged(nameof(MaxSliderValue));
        OnPropertyChanged(nameof(PageDisplay));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(IsFirstPage));
        OnPropertyChanged(nameof(IsLastPage));
        OnPropertyChanged(nameof(CanAdvanceOrShowEndChoices));
    }

    private void InvalidateReaderSourceIfChanged(string previousReadPath)
    {
        var currentReadPath = GetActiveReadPath();
        if (string.Equals(previousReadPath, currentReadPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _comicReaderService.ClearCache();
        ClearBitmapPrefetchCache();
        _lastLoadedPageIndex = -1;
        _ = LoadPageAsync();
    }

    [RelayCommand]
    private async Task SaveCurrentPageAsync()
    {
        if (Comic is null) return;

        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var imagesDir = Path.Combine(appDir, "images");
            if (!Directory.Exists(imagesDir))
            {
                Directory.CreateDirectory(imagesDir);
            }

            var readPath = GetActiveReadPath();
            var pageData = await _comicReaderService.GetPageAsync(readPath, CurrentPage);
            var pageNames = await _comicReaderService.GetPageNamesAsync(readPath);
            var extension = ".jpg"; // Default
            if (pageNames.Count > CurrentPage)
            {
                extension = Path.GetExtension(pageNames[CurrentPage]);
            }

            var fileName = $"{Path.GetFileNameWithoutExtension(readPath)}_p{CurrentPage}{extension}";
            var filePath = Path.Combine(imagesDir, fileName);

            await File.WriteAllBytesAsync(filePath, pageData);

            // Optional: Provide some feedback, though not strictly requested
            System.Diagnostics.Debug.WriteLine($"Saved page to: {filePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save page: {ex.Message}");
        }
    }

    public async Task LoadComicAsync(int comicId)
    {
        ComicId = comicId;
        try
        {
            IsBusy = true;
            _isInitializingComic = true;
            ErrorMessage = null;
            IsInfoPanelVisible = false;
            IsEndOfComicOptionsVisible = false;
            NextSeriesComic = null;
            ComicInfo = null;
            Title = "Reader";
            ReaderStatusMessage = null;
            ClearKomgaSyncPrompt();

            // Clear previous comic data
            ReleaseReaderResources();
            Comic = null;
            CurrentPage = 0;

            // Load reading mode preferences
            var settings = _settingsService.LoadSettings();
            ReadingMode = NormalizeReadingMode(settings.PreferredReadingMode);
            Handedness = settings.Handedness;
            SelectedReadingDirectionModeOption = AvailableReadingDirectionModes.FirstOrDefault(o => o.Value == settings.PreferredReadingDirectionMode) ?? AvailableReadingDirectionModes[0];
            CompactOverview = settings.CompactOverview;
            ZoomRegion = new ZoomRegion { Size = settings.DefaultZoomRegionSize };
            IsFullScreen = settings.UseFullScreenWhenReading;

            Comic = await _libraryService.GetComicAsync(ComicId);
            if (Comic is not null)
            {
                // Save last opened comic info in settings
                settings.LastOpenedComicId = Comic.Id;
                settings.LastOpenedComicPath = Comic.FilePath;
                settings.WasInReader = true;
                _ = _settingsService.SaveSettingsAsync(settings);

                Title = Comic.Title;
                OnPropertyChanged(nameof(MaxSliderValue));
                OnPropertyChanged(nameof(PageDisplay));
                OnPropertyChanged(nameof(HasPreviousPage));
                OnPropertyChanged(nameof(HasNextPage));
                OnPropertyChanged(nameof(IsFirstPage));
                OnPropertyChanged(nameof(IsLastPage));
                OnPropertyChanged(nameof(CanAdvanceOrShowEndChoices));

                if (Comic.Format == ComicFormat.Epub)
                {
                    _epubShadowConversionService.StartReadingSession(Comic.Id);
                    var (readPath, state) = await _epubShadowConversionService.EnsurePagesAvailableAsync(Comic, Math.Max(0, Comic.CurrentPage));
                    _readerFilePath = readPath;
                    ApplyEpubConversionState(state);
                }

                if (Comic.Format == ComicFormat.Pdf)
                {
                    var (pageCount, fileSize) = await _comicReaderService.GetComicInfoAsync(Comic.FilePath);
                    Comic.PageCount = pageCount;
                    Comic.FileSize = fileSize;
                }
                else if (!HasPendingEpubConversion)
                {
                    _readerFilePath = Comic.FilePath;
                }

                Title = Comic.Title;
                await RefreshSeriesNavigationTargetsAsync();
                await ResolveAutomaticReadingDirectionAsync();

                if (Comic.PageCount <= 0)
                {
                    ErrorMessage = HasPendingEpubConversion
                        ? "Preparing the first readable page..."
                        : "This book could not be prepared for reading.";
                    return;
                }

                _isLoadingPage = true;
                // Ensure CurrentPage is within valid range (0 to PageCount-1)
                var validPage = Math.Max(0, Math.Min(Comic.CurrentPage, Comic.PageCount - 1));
                if (Comic.PageCount > 0 && validPage == 0 && !Comic.IsCompleted && StartsAtBack())
                {
                    _currentPage = Comic.PageCount - 1;
                }
                else
                {
                    _currentPage = Comic.PageCount > 0 ? validPage : 0;
                }
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(HasPreviousPage));
                OnPropertyChanged(nameof(HasNextPage));
                OnPropertyChanged(nameof(PageDisplay));
                OnPropertyChanged(nameof(IsFirstPage));
                OnPropertyChanged(nameof(IsLastPage));
                await LoadPageAsync();

                // Load the first page before syncing with Komga so server issues never block opening the reader.
                if (Comic.Source == ComicSource.Komga && !string.IsNullOrEmpty(Comic.KomgaId) && Comic.KomgaServerId.HasValue)
                {
                    _ = RunInitialKomgaSyncAsync();
                }

                // Mark as started reading
                await SaveProgressAsync(forceImmediate: true);

                _periodicSyncTimer.Start();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load comic: {ex.Message}";
        }
        finally
        {
            _isInitializingComic = false;
            IsBusy = false;
        }
    }

    public async Task SyncAndRefreshProgressAsync()
    {
        var comic = Comic;
        if (comic is null || comic.Source != ComicSource.Komga || string.IsNullOrEmpty(comic.KomgaId))
        {
            return;
        }

        var oldPage = CurrentPage;
        var oldCompleted = comic.IsCompleted;

        await _komgaSyncService.SyncComicReadProgressAsync(comic);
        if (!ReferenceEquals(Comic, comic))
        {
            return;
        }

        OnPropertyChanged(nameof(KomgaSyncStatus));

        // If the sync updated the comic's current page to something newer, jump to it
        if (comic.CurrentPage != oldPage || comic.IsCompleted != oldCompleted)
        {
            var syncedPage = Math.Max(0, Math.Min(comic.CurrentPage, comic.PageCount - 1));
            if (CurrentPage != oldPage)
            {
                PendingKomgaSyncPage = syncedPage;
                PendingKomgaSyncCompleted = comic.IsCompleted;
                PendingKomgaSyncLastModified = comic.ReadProgressLastModified;
                ShowKomgaSyncLocationPrompt = syncedPage != CurrentPage || comic.IsCompleted != oldCompleted;
                return;
            }

            await ApplyKomgaSyncedLocationAsync(syncedPage, comic.IsCompleted, comic.ReadProgressLastModified);
        }
    }

    private async Task RunInitialKomgaSyncAsync()
    {
        try
        {
            await SyncAndRefreshProgressAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReaderViewModel: Initial Komga sync failed: {ex.Message}");
        }
    }

    private async Task SyncProgressWithKomgaAsync()
    {
        if (Comic is null || Comic.Source != ComicSource.Komga || string.IsNullOrEmpty(Comic.KomgaId) || !Comic.KomgaServerId.HasValue)
        {
            return;
        }

        await _komgaSyncService.PushProgressToKomgaAsync(Comic);
        OnPropertyChanged(nameof(KomgaSyncStatus));
    }

    private async Task LoadPageAsync()
    {
        if (Comic is null)
        {
            return;
        }

        // Cancel any existing load
        _pageLoadingCts?.Cancel();
        _pageLoadingCts = new CancellationTokenSource();
        var ct = _pageLoadingCts.Token;

        // Small debounce to skip intermediate pages during very rapid scrolling (e.g. scroll wheel)
        // This prevents the semaphore from being flooded with requests that will be immediately cancelled
        try { await Task.Delay(50, ct); } catch (OperationCanceledException) { return; }

        _isLoadingPage = true;
        try
        {
            while (_lastLoadedPageIndex != CurrentPage)
            {
                if (ct.IsCancellationRequested) return;

                // Capture current index
                int pageIndex = CurrentPage;

                if (HasPendingEpubConversion)
                {
                    await EnsureEpubPagesAvailableAsync(pageIndex);
                    if (ct.IsCancellationRequested) return;
                }

                // Validate page index is within range
                if (Comic.PageCount == 0)
                {
                    ErrorMessage = HasPendingEpubConversion ? "Preparing readable pages..." : "Comic has no pages";
                    return;
                }

                // Only show the loading indicator when the bitmap is not already pre-decoded
                bool hasPrefetchedBitmap = !IsTwoPageMode && HasPrefetchedBitmap(pageIndex);

                if (!hasPrefetchedBitmap)
                {
                    IsBusy = true;
                }

                try
                {
                    // Store page data for panel detection in guided mode
                    byte[]? pageData = null;

                    if (IsTwoPageMode)
                    {
                        // Load two pages for two-page mode
                        var readPath = GetActiveReadPath();

                        // Load in parallel but with cancellation support
                        var leftTask = LoadBitmapAsync(readPath, pageIndex, ct);
                        var rightTask = pageIndex + 1 < Comic.PageCount
                            ? LoadBitmapAsync(readPath, pageIndex + 1, ct)
                            : Task.FromResult<Bitmap?>(null);

                        var newLeftBitmap = await leftTask;
                        var newRightBitmap = await rightTask;

                        if (ct.IsCancellationRequested)
                        {
                            newLeftBitmap?.Dispose();
                            newRightBitmap?.Dispose();
                            return;
                        }

                        var oldLeftBitmap = LeftPageImage;
                        LeftPageImage = newLeftBitmap;
                        oldLeftBitmap?.Dispose();

                        // Also update the single page image for consistency
                        CurrentPageImage = LeftPageImage;

                        var oldRightBitmap = RightPageImage;
                        RightPageImage = newRightBitmap;
                        oldRightBitmap?.Dispose();
                    }
                    else
                    {
                        // Single page mode - use pre-decoded bitmap from prefetch cache if available
                        var newBitmap = TakePrefetchedBitmap(pageIndex);
                        if (newBitmap is null)
                        {
                            if (ReadingMode == ReadingMode.Guided)
                            {
                                pageData = await _comicReaderService.GetPageAsync(GetActiveReadPath(), pageIndex);
                                if (ct.IsCancellationRequested) return;
                                newBitmap = await CreateBitmapFromPageDataAsync(pageData, ct);
                            }
                            else
                            {
                                newBitmap = await LoadBitmapAsync(GetActiveReadPath(), pageIndex, ct);
                            }
                        }
                        else if (ReadingMode == ReadingMode.Guided)
                        {
                            // Need raw data for panel detection even though bitmap was prefetched
                            pageData = await _comicReaderService.GetPageAsync(GetActiveReadPath(), pageIndex);
                        }

                        if (ct.IsCancellationRequested)
                        {
                            newBitmap?.Dispose();
                            return;
                        }

                        var oldBitmap = CurrentPageImage;
                        CurrentPageImage = newBitmap;
                        oldBitmap?.Dispose();
                    }

                    // If in guided mode, detect panels
                    if (ReadingMode == ReadingMode.Guided && pageData is not null)
                    {
                        await DetectPanelsForCurrentPageAsync(pageData, pageIndex);
                        if (ct.IsCancellationRequested) return;
                    }

                    // Pre-detect panels for next page in background if in guided mode
                    if (ReadingMode == ReadingMode.Guided && pageIndex + 1 < Comic.PageCount)
                    {
                        _ = PreDetectNextPagePanelsAsync(pageIndex + 1);
                    }

                    _lastLoadedPageIndex = pageIndex;

                    // Reset zoom when changing pages
                    ZoomLevel = 1.0;
                }
                catch (Exception ex)
                {
                    if (ex is not OperationCanceledException && !ct.IsCancellationRequested)
                    {
                        ErrorMessage = $"Failed to load page: {ex.Message}";
                    }
                    _lastLoadedPageIndex = pageIndex; // Prevent infinite loop on error
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }
        finally
        {
            // Only clear the loading flag if this is still the active load operation
            if (!ct.IsCancellationRequested)
            {
                _isLoadingPage = false;
            }
        }

        // Prefetch adjacent pages in the background so the next navigation is instant
        PrefetchAdjacentPages(CurrentPage);
    }

    private async Task EnsureEpubPagesAvailableAsync(int requestedPageIndex)
    {
        if (Comic is null || Comic.Format != ComicFormat.Epub)
        {
            return;
        }

        var (readPath, state) = await _epubShadowConversionService.EnsurePagesAvailableAsync(
            Comic,
            requestedPageIndex,
            IsTwoPageMode ? 1 : 0);

        _readerFilePath = readPath;
        ApplyEpubConversionState(state);
        ErrorMessage = state?.Status == EpubConversionStatus.Failed ? state.LastError : null;
    }

    private void OnCurrentPageChanged(int value)
    {
        if (Comic is not null && !_isLoadingPage)
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
        // Force a reload of the current page when switching modes to avoid black screen
        if (Comic is not null && !_isLoadingPage)
        {
            _lastLoadedPageIndex = -1;
            _ = LoadPageAsync();
        }
    }

    /// <summary>
    /// Event raised when a request is made to view a specific Komga series
    /// </summary>
    public event EventHandler<KomgaSeriesNavigationRequest>? ViewSeriesRequested;

    /// <summary>
    /// Event raised when another comic should be opened in the reader.
    /// </summary>
    public event EventHandler<int>? ComicOpenRequested;

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (Comic is null) return;
        await _libraryService.ToggleFavoriteAsync(Comic.Id);
        // Refresh local comic object
        var updated = await _libraryService.GetComicAsync(Comic.Id);
        if (updated != null)
        {
            Comic = updated;
            await RefreshSeriesNavigationTargetsAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleReadStatusAsync()
    {
        if (Comic is null) return;
        await _libraryService.ToggleReadStatusAsync(Comic.Id);
        // Close reader and go back as requested by user
        await GoBackAsync();
    }

    [RelayCommand]
    private async Task DeleteComicAsync()
    {
        if (Comic is null) return;
        // Close reader first to release file locks
        var comicToDelete = Comic;
        await GoBackAsync();
        await _libraryService.DeleteComicAsync(comicToDelete);
    }

    [RelayCommand]
    private async Task ViewSeriesOnKomga()
    {
        if (Comic is not null && !string.IsNullOrEmpty(Comic.KomgaSeriesId))
        {
            IsEndOfComicOptionsVisible = false;
            IsInfoPanelVisible = false;
            await SaveProgressAsync();
            ViewSeriesRequested?.Invoke(this, new KomgaSeriesNavigationRequest
            {
                SeriesId = Comic.KomgaSeriesId,
                ServerId = Comic.KomgaServerId
            });
        }
    }

    [RelayCommand]
    private Task GoToPreviousPageAsync()
    {
        if (!HasPreviousPage)
        {
            return Task.CompletedTask;
        }

        var step = IsTwoPageMode ? 2 : 1;
        var delta = -GetNavigationDirectionSign() * step;
        var targetPage = Math.Max(0, Math.Min(Comic!.PageCount - 1, CurrentPage + delta));
        if (targetPage == CurrentPage)
        {
            return Task.CompletedTask;
        }

        CurrentPage = targetPage;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task GoToNextPageAsync()
    {
        if (Comic is null)
        {
            return;
        }

        var step = IsTwoPageMode ? 2 : 1;
        var delta = GetNavigationDirectionSign() * step;
        if (delta > 0 && HasPendingEpubConversion && CurrentPage >= Comic.PageCount - 1)
        {
            ReaderStatusMessage = "Preparing next page...";
            await EnsureEpubPagesAvailableAsync(CurrentPage + 1);
            if (CurrentPage >= Comic.PageCount - 1)
            {
                return;
            }
        }

        if (!HasNextPage)
        {
            if (IsLastPage)
            {
                await ShowEndOfComicOptionsAsync();
            }

            return;
        }

        var targetPage = Math.Max(0, Math.Min(Comic.PageCount - 1, CurrentPage + delta));
        if (targetPage == CurrentPage)
        {
            if (IsLastPage)
            {
                await ShowEndOfComicOptionsAsync();
            }
            return;
        }

        CurrentPage = targetPage;
    }

    [RelayCommand]
    private async Task GoToPageAsync(int page)
    {
        if (Comic is null || page < 0)
        {
            return;
        }

        if (HasPendingEpubConversion && page >= Comic.PageCount)
        {
            ReaderStatusMessage = "Preparing requested page...";
            await EnsureEpubPagesAvailableAsync(page);
        }

        if (page >= Comic.PageCount)
        {
            return;
        }

        CurrentPage = page;
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
                if (SelectedReadingDirectionModeOption?.Value == ReadingDirectionMode.Automatic)
                {
                    await ResolveAutomaticReadingDirectionAsync();
                }
            }
            catch
            {
                // ComicInfo not available
            }
        }

        IsInfoPanelVisible = true;
    }

    [RelayCommand]
    private void DismissEndOfComicOptions()
    {
        IsEndOfComicOptionsVisible = false;
    }

    [RelayCommand]
    private async Task AcceptKomgaSyncLocationAsync()
    {
        if (Comic is null || PendingKomgaSyncPage < 0)
        {
            ClearKomgaSyncPrompt();
            return;
        }

        var syncedPage = PendingKomgaSyncPage;
        var syncedCompleted = PendingKomgaSyncCompleted;
        var syncedLastModified = PendingKomgaSyncLastModified;
        ClearKomgaSyncPrompt();

        await ApplyKomgaSyncedLocationAsync(syncedPage, syncedCompleted, syncedLastModified);
    }

    [RelayCommand]
    private async Task DismissKomgaSyncLocationPromptAsync()
    {
        ClearKomgaSyncPrompt();
        await SaveProgressAsync(forceImmediate: true);
    }

    [RelayCommand]
    private async Task OpenNextSeriesComicAsync()
    {
        if (NextSeriesComic is null)
        {
            return;
        }

        var nextComicId = NextSeriesComic.Id;
        IsEndOfComicOptionsVisible = false;
        IsInfoPanelVisible = false;
        _ = SaveProgressAsync(forceImmediate: true);
        ComicOpenRequested?.Invoke(this, nextComicId);
    }

    [RelayCommand]
    private async Task ReturnToLibraryAsync()
    {
        IsEndOfComicOptionsVisible = false;
        IsInfoPanelVisible = false;
        await GoBackAsync();
    }

    private async Task SaveProgressAsync(bool forceImmediate = false)
    {
        var comic = Comic;
        if (comic is null)
        {
            return;
        }
        var currentPage = CurrentPage;

        var oldCts = Interlocked.Exchange(ref _saveProgressCts, forceImmediate ? null : new CancellationTokenSource());
        if (oldCts is not null)
        {
            try
            {
                oldCts.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { }
            oldCts.Dispose();
        }

        try
        {
            if (!forceImmediate)
            {
                var cts = _saveProgressCts;
                if (cts is null) return;

                await Task.Delay(500, cts.Token);
            }

            await _libraryService.UpdateReadingProgressAsync(comic, currentPage);
            OnPropertyChanged(nameof(KomgaSyncStatus));
        }
        catch (OperationCanceledException)
        {
            // Gracefully ignore cancellation
        }
        catch (Exception ex)
        {
            // Silently fail - don't interrupt reading but log to debug
            System.Diagnostics.Debug.WriteLine($"ReaderViewModel: Failed to save progress: {ex.Message}");
        }
    }

    private async Task ApplyKomgaSyncedLocationAsync(int syncedPage, bool syncedCompleted, DateTime? syncedLastModified)
    {
        if (Comic is null)
        {
            return;
        }

        try
        {
            await _libraryService.UpdateReadingProgressAsync(Comic, syncedPage, syncedLastModified, syncedCompleted);

            _isLoadingPage = true;
            _currentPage = Math.Max(0, Math.Min(syncedPage, Comic.PageCount - 1));
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
            OnPropertyChanged(nameof(PageDisplay));
            OnPropertyChanged(nameof(IsFirstPage));
            OnPropertyChanged(nameof(IsLastPage));
            await LoadPageAsync();
        }
        finally
        {
            _isLoadingPage = false;
            OnPropertyChanged(nameof(KomgaSyncStatus));
        }
    }

    private void ClearKomgaSyncPrompt()
    {
        ShowKomgaSyncLocationPrompt = false;
        PendingKomgaSyncPage = -1;
        PendingKomgaSyncCompleted = false;
        PendingKomgaSyncLastModified = null;
    }

    #region Reading Mode Methods

    /// <summary>
    /// Cycle through reading modes (Normal -> Zoomed -> Guided -> Normal)
    /// </summary>
    [RelayCommand]
    private async Task CycleReadingModeAsync()
    {
        ReadingMode = NormalizeReadingMode(ReadingMode switch
        {
            ReadingMode.Normal => ReadingMode.Zoomed,
            ReadingMode.Zoomed when IsGuidedReadingAvailable => ReadingMode.Guided,
            ReadingMode.Zoomed => ReadingMode.Normal,
            ReadingMode.Guided => ReadingMode.Normal,
            _ => ReadingMode.Normal
        });

        // Disable two-page mode when switching to zoomed or guided
        if (!IsNormalMode && IsTwoPageMode)
        {
            IsTwoPageMode = false;
        }

        // If switching to guided mode, detect panels
        if (ReadingMode == ReadingMode.Guided && Comic is not null)
        {
            var pageData = await _comicReaderService.GetPageAsync(GetActiveReadPath(), CurrentPage);
            await DetectPanelsForCurrentPageAsync(pageData, CurrentPage);
        }

        // Notify properties that depend on reading mode
        OnPropertyChanged(nameof(HasPreviousPanel));
        OnPropertyChanged(nameof(HasNextPanel));
        OnPropertyChanged(nameof(CurrentPanelDisplay));
    }

    /// <summary>
    /// Set reading mode to Normal
    /// </summary>
    [RelayCommand]
    private void SetNormalMode()
    {
        ReadingMode = ReadingMode.Normal;

        // Disable two-page mode when switching to zoomed or guided
        if (!IsNormalMode && IsTwoPageMode)
        {
            IsTwoPageMode = false;
        }

        // Notify properties that depend on reading mode
        OnPropertyChanged(nameof(HasPreviousPanel));
        OnPropertyChanged(nameof(HasNextPanel));
        OnPropertyChanged(nameof(CurrentPanelDisplay));
    }

    /// <summary>
    /// Set reading mode to Zoomed
    /// </summary>
    [RelayCommand]
    private void SetZoomedMode()
    {
        ReadingMode = ReadingMode.Zoomed;

        // Disable two-page mode when switching to zoomed or guided
        if (!IsNormalMode && IsTwoPageMode)
        {
            IsTwoPageMode = false;
        }

        // Notify properties that depend on reading mode
        OnPropertyChanged(nameof(HasPreviousPanel));
        OnPropertyChanged(nameof(HasNextPanel));
        OnPropertyChanged(nameof(CurrentPanelDisplay));
    }

    /// <summary>
    /// Set reading mode to Guided
    /// </summary>
    [RelayCommand]
    private async Task SetGuidedModeAsync()
    {
        if (!IsGuidedReadingAvailable)
        {
            return;
        }

        ReadingMode = ReadingMode.Guided;

        // Disable two-page mode when switching to zoomed or guided
        if (!IsNormalMode && IsTwoPageMode)
        {
            IsTwoPageMode = false;
        }

        // If switching to guided mode, detect panels
        if (Comic is not null)
        {
            var pageData = await _comicReaderService.GetPageAsync(GetActiveReadPath(), CurrentPage);
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
        mode = NormalizeReadingMode(mode);

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
            var pageData = await _comicReaderService.GetPageAsync(GetActiveReadPath(), CurrentPage);
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
            var isManga = IsMangaReadingDirection();
            var result = await _panelDetectionService.DetectPanelsAsync(
                GetActiveReadPath(),
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
        var readPath = GetActiveReadPath();
        if (_panelDetectionService.IsCached(readPath, nextPageIndex))
        {
            return;
        }

        try
        {
            var isManga = IsMangaReadingDirection();
            var nextPageData = await _comicReaderService.GetPageAsync(readPath, nextPageIndex);
            await _panelDetectionService.DetectPanelsAsync(readPath, nextPageIndex, nextPageData, isManga);
        }
        catch
        {
            // Silently fail - this is just pre-caching
        }
    }

    private async Task<Bitmap?> LoadBitmapAsync(string filePath, int pageIndex, CancellationToken ct)
    {
        using var stream = RecyclableStreamManagerProvider.Manager.GetStream(nameof(ReaderViewModel));
        await _comicReaderService.CopyPageAsync(filePath, pageIndex, stream);

        if (ct.IsCancellationRequested) return null;
        stream.Position = 0;

        await GlobalDecodeSemaphore.WaitAsync(ct);
        try
        {
            if (ct.IsCancellationRequested) return null;
            return await Task.Run(() => new Bitmap(stream));
        }
        finally
        {
            GlobalDecodeSemaphore.Release();
        }
    }

    private async Task<Bitmap?> CreateBitmapFromPageDataAsync(byte[] pageData, CancellationToken ct)
    {
        await GlobalDecodeSemaphore.WaitAsync(ct);
        try
        {
            if (ct.IsCancellationRequested) return null;
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(pageData, writable: false);
                return new Bitmap(stream);
            });
        }
        finally
        {
            GlobalDecodeSemaphore.Release();
        }
    }

    #endregion

    #region Bitmap Prefetch Cache

    private bool HasPrefetchedBitmap(int pageIndex)
    {
        lock (_bitmapPrefetchLock)
        {
            return _bitmapPrefetchCache.ContainsKey(pageIndex);
        }
    }

    private Bitmap? TakePrefetchedBitmap(int pageIndex)
    {
        lock (_bitmapPrefetchLock)
        {
            if (_bitmapPrefetchCache.TryGetValue(pageIndex, out var bitmap))
            {
                _bitmapPrefetchCache.Remove(pageIndex);
                return bitmap;
            }
            return null;
        }
    }

    private void StorePrefetchedBitmap(int pageIndex, Bitmap bitmap)
    {
        lock (_bitmapPrefetchLock)
        {
            // Dispose existing entry if present
            if (_bitmapPrefetchCache.TryGetValue(pageIndex, out var existing))
            {
                existing.Dispose();
            }
            _bitmapPrefetchCache[pageIndex] = bitmap;

            // Evict entries furthest from the stored page when over limit
            while (_bitmapPrefetchCache.Count > MaxPrefetchedBitmaps)
            {
                var toRemove = _bitmapPrefetchCache.Keys
                    .MaxBy(k => Math.Abs(k - pageIndex))!;
                if (_bitmapPrefetchCache.TryGetValue(toRemove, out var toDispose))
                {
                    toDispose.Dispose();
                }
                _bitmapPrefetchCache.Remove(toRemove);
            }
        }
    }

    private void ClearBitmapPrefetchCache()
    {
        lock (_bitmapPrefetchLock)
        {
            foreach (var bitmap in _bitmapPrefetchCache.Values)
            {
                bitmap.Dispose();
            }
            _bitmapPrefetchCache.Clear();
        }
    }

    /// <summary>
    /// Pre-decode a page bitmap on a background thread so it is ready for instant display
    /// </summary>
    private async Task PrefetchPageBitmapAsync(int pageIndex)
    {
        if (Comic is null || pageIndex < 0 || pageIndex >= Comic.PageCount || IsTwoPageMode)
        {
            return;
        }

        // Skip if already prefetched
        lock (_bitmapPrefetchLock)
        {
            if (_bitmapPrefetchCache.ContainsKey(pageIndex))
            {
                return;
            }
        }

        try
        {
            var filePath = GetActiveReadPath();
            var bitmap = await LoadBitmapAsync(filePath, pageIndex, CancellationToken.None);

            if (bitmap is null) return;

            // Only cache if still reading the same comic
            if (GetActiveReadPath() == filePath)
            {
                StorePrefetchedBitmap(pageIndex, bitmap);
            }
            else
            {
                bitmap.Dispose();
            }
        }
        catch
        {
            // Silently fail - prefetching is best-effort
        }
    }

    /// <summary>
    /// Kick off background prefetch tasks for the pages immediately before and after the current one
    /// </summary>
    private void PrefetchAdjacentPages(int currentPage)
    {
        if (Comic is null || IsTwoPageMode)
        {
            return;
        }

        if (currentPage + 1 < Comic.PageCount)
        {
            _ = PrefetchPageBitmapAsync(currentPage + 1);
        }

        if (currentPage - 1 >= 0)
        {
            _ = PrefetchPageBitmapAsync(currentPage - 1);
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
        else if (IsLastPage)
        {
            await ShowEndOfComicOptionsAsync();
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
        // Reset fullscreen before leaving the reader
        IsFullScreen = false;

        // Fire and forget the Komga sync in the background so it doesn't delay UI closing
        _ = SyncProgressWithKomgaAsync();

        _ = SaveProgressAsync(forceImmediate: true);
        ReleaseReaderResources();
        CloseRequested?.Invoke(this, EventArgs.Empty);

        // Force a cleanup after leaving the reader to ensure high-res bitmaps are truly gone
        await Task.Delay(500); // Give UI time to detach
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private async Task RefreshSeriesNavigationTargetsAsync()
    {
        NextSeriesComic = Comic is null
            ? null
            : await _libraryService.GetNextComicInSeriesAsync(Comic.Id);
    }

    private async Task ShowEndOfComicOptionsAsync()
    {
        IsInfoPanelVisible = false;
        IsControlsVisible = true;
        await RefreshSeriesNavigationTargetsAsync();
        IsEndOfComicOptionsVisible = true;
    }
}
