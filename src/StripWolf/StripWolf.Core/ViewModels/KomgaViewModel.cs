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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Core.Controls;
using StripWolf.Core.Data;
using StripWolf.Core.Models;
using StripWolf.Core.Models.Komga;
using StripWolf.Core.Services;
using StripWolf.Core.Resources;
using System.Diagnostics.CodeAnalysis;

namespace StripWolf.Core.ViewModels;

/// <summary>
/// View model for browsing Komga content
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
public partial class KomgaViewModel : ViewModelBase
{
    private readonly KomgaApiServiceFactory _komgaApiServiceFactory;
    private readonly KomgaApiService _inactiveKomgaApiService = new();
    private KomgaApiService _komgaApiService;
    private readonly LibraryService _libraryService;
    private readonly ImportQueueService _importQueueService;
    private readonly SettingsService _settingsService;
    private readonly DatabaseService _databaseService;
    private readonly IExternalLinkService _externalLinkService;

    public event EventHandler? ConfigureServerRequested;
    
    private KomgaServer? _activeServer;
    private CancellationTokenSource? _loadingCts;
    private bool _isApplyingSectionLayout;
    private int _savedSeriesPage;
    private DateTime _lastLoadTime = DateTime.MinValue;
    
    // Cache timestamps for smart lists
    private DateTime _readListsCacheTime;
    private DateTime _keepReadingCacheTime;
    private DateTime _onDeckCacheTime;
    private DateTime _recentBooksCacheTime;
    private DateTime _recentSeriesCacheTime;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    private readonly HashSet<string> _downloadedKomgaIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _downloadedKomgaHashes = new(StringComparer.OrdinalIgnoreCase);
    private bool _downloadedComicsCacheLoaded;
    private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);

    private void InvalidateDownloadedComicsCache()
    {
        _downloadedComicsCacheLoaded = false;
    }

    private async Task EnsureDownloadedComicsCacheLoadedAsync()
    {
        if (_downloadedComicsCacheLoaded)
        {
            return;
        }

        await _cacheSemaphore.WaitAsync();
        try
        {
            if (_downloadedComicsCacheLoaded)
            {
                return;
            }

            _downloadedKomgaIds.Clear();
            _downloadedKomgaHashes.Clear();

            if (_activeServer is not null)
            {
                var allComics = await _databaseService.GetComicsAsync();
                var serverComics = allComics
                    .Where(c => c.KomgaServerId == _activeServer.Id || c.KomgaServerId == null)
                    .ToList();
                foreach (var comic in serverComics)
                {
                    if (!string.IsNullOrEmpty(comic.KomgaId))
                    {
                        _downloadedKomgaIds.Add(comic.KomgaId);
                    }
                    if (!string.IsNullOrEmpty(comic.KomgaHash))
                    {
                        _downloadedKomgaHashes.Add(comic.KomgaHash);
                    }
                }
            }

            _downloadedComicsCacheLoaded = true;
        }
        finally
        {
            _cacheSemaphore.Release();
        }
    }

    /// <summary>
    /// Cancels any ongoing loading, schedules deferred disposal of the old CTS,
    /// creates a fresh CTS and returns its token.
    /// </summary>
    private CancellationToken ResetLoadingCancellation()
    {
        _lastLoadTime = DateTime.UtcNow;
        var oldCts = _loadingCts;
        _loadingCts = new CancellationTokenSource();
        if (oldCts is not null)
        {
            oldCts.Cancel();
            _ = Task.Delay(500).ContinueWith(_ => oldCts.Dispose(), TaskScheduler.Default);
        }
        return _loadingCts.Token;
    }

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private ObservableCollection<KomgaServer> _configuredServers = [];

    [ObservableProperty]
    private KomgaServer? _selectedServer;

    [ObservableProperty]
    private ObservableCollection<KomgaLibrary> _libraries = [];

    [ObservableProperty]
    private ObservableCollection<KomgaSeriesDisplay> _series = [];

    [ObservableProperty]
    private ObservableCollection<KomgaBookDisplay> _books = [];

    [ObservableProperty]
    private KomgaLibrary? _selectedLibrary;

    [ObservableProperty]
    private KomgaSeries? _selectedSeries;

    [ObservableProperty]
    private bool _isSelectedSeriesQueuedForDownload;

    [ObservableProperty]
    private bool _isSelectedSeriesDownloading;

    [ObservableProperty]
    private KomgaBook? _selectedBook;

    [ObservableProperty]
    private KomgaBookDisplay? _selectedInfoBookDisplay;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _downloadingBookName = string.Empty;

    [ObservableProperty]
    private int _queuedDownloadsCount;

    [ObservableProperty]
    private bool _isDownloadQueueActive;

    [ObservableProperty]
    private ObservableCollection<KomgaDownloadQueueItem> _downloadQueueItems = [];

    private readonly HashSet<string> _downloadPendingBookIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, KomgaDownloadQueueItem> _downloadItemsByBookId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, KomgaPostDownloadWorkItem> _postDownloadWorkItemsByBookId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _downloadCancellationTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _cancelRequestedBookIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pauseRequestedBookIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<KomgaPostDownloadWorkItem> _postDownloadQueue = new();
    private readonly object _postDownloadQueueLock = new();
    private bool _isProcessingQueue;
    private bool _isProcessingPostDownloadQueue;
    private bool _downloadQueueStateRefreshPending;
    [ObservableProperty]
    private bool _isDownloadQueuePaused;
    [ObservableProperty]
    private double _downloadThrottleParallel = 1;
    private int _maxParallelDownloads = 1;
    private int _searchLimit = 10;
    private int _smartListSize = 10;
    private const int MaxDownloadRetryCount = 3;
    private readonly HashSet<int> _offlineServerIds = [];
    private DateTime _lastDownloadConnectionProbeUtc = DateTime.MinValue;
    private bool _lastDownloadConnectionProbeResult = true;
    private int? _lastDownloadConnectionProbeServerId;
    private static readonly TimeSpan DownloadConnectionProbeInterval = TimeSpan.FromSeconds(3);
    private int? _recentFailedServerId;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<KomgaSeriesDisplay> _searchSeriesResults = [];

    [ObservableProperty]
    private ObservableCollection<KomgaBookDisplay> _searchBookResults = [];

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<KomgaReadListDisplay> _readLists = [];

    [ObservableProperty]
    private KomgaReadList? _selectedReadList;

    [ObservableProperty]
    private ObservableCollection<KomgaReadList> _availableReadLists = [];

    [ObservableProperty]
    private KomgaBookDisplay? _bookPendingReadListSelection;

    [ObservableProperty]
    private KomgaSeriesDisplay? _seriesPendingDownloadSelection;

    [ObservableProperty]
    private bool _isLoadingReadListSelection;

    [ObservableProperty]
    private bool _hasMoreReadLists = true;

    private int _currentPage;
    // Token of the navigation for which a series / read list books page request is in flight, prevents duplicate
    // page requests when the view asks for more items on every scroll event
    private CancellationToken? _seriesPageLoadingFor;
    private CancellationToken? _readListBooksPageLoadingFor;
    private Task? _initializeTask;

    // Thumbnails are displayed ~150-200 DIP wide, 300 pixels is sharp enough for HiDPI screens
    private const int ThumbnailDecodeWidth = 300;
    // Limits parallel thumbnail requests and decodes, a fast scroll realizes a lot of cards at once
    private readonly SemaphoreSlim _thumbnailLoadSemaphore = new(4, 4);

    /// <summary>
    /// True while a further page is loaded because the user scrolled near the end of the list
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMore;
    
    [ObservableProperty]
    private bool _hasMoreSeries = true;
    
    [ObservableProperty]
    private bool _hasMoreBooks = true;
    
    private int _currentReadListPage;
    
    [ObservableProperty]
    private string? _selectedSeriesPrefix = "0-9";

    public List<string> AvailablePrefixes { get; } = 
        ["0-9", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"];

    partial void OnSelectedSeriesPrefixChanged(string? value)
    {
        _ = SelectPrefixAsync(value);
    }

    partial void OnSelectedSeriesChanged(KomgaSeries? value)
    {
        UpdateSelectedSeriesDownloadState(value?.Id);
    }

    [RelayCommand]
    private async Task SelectPrefixAsync(string? prefix)
    {
        // Cancel any ongoing loading
        ResetLoadingCancellation();

        SelectedSeriesPrefix = prefix;

        if (SelectedLibrary != null)
        {
            _currentPage = 0;
            HasMoreSeries = true;
            ClearAndReleaseThumbnails(Series);
            await LoadSeriesAsync();
        }
    }

    // Smart Lists (Keep Reading, On Deck, Recently Added)
    [ObservableProperty]
    private ObservableCollection<KomgaBookDisplay> _keepReadingBooks = [];
    
    [ObservableProperty]
    private ObservableCollection<KomgaBookDisplay> _onDeckBooks = [];
    
    [ObservableProperty]
    private ObservableCollection<KomgaBookDisplay> _recentlyAddedBooks = [];
    
    [ObservableProperty]
    private ObservableCollection<KomgaSeriesDisplay> _recentlyAddedSeries = [];
    
    [ObservableProperty]
    private bool _hasKeepReading;
    
    [ObservableProperty]
    private bool _hasOnDeck;
    
    [ObservableProperty]
    private bool _hasRecentBooks;
    
    [ObservableProperty]
    private bool _hasRecentSeries;

    public SectionLayoutItemViewModel KeepReadingSection { get; } = new(KomgaSectionKeys.KeepReading);
    public SectionLayoutItemViewModel OnDeckSection { get; } = new(KomgaSectionKeys.OnDeck);
    public SectionLayoutItemViewModel RecentBooksSection { get; } = new(KomgaSectionKeys.RecentlyAddedBooks);
    public SectionLayoutItemViewModel RecentSeriesSection { get; } = new(KomgaSectionKeys.RecentlyAddedSeries);
    public SectionLayoutItemViewModel LibrariesSection { get; } = new(KomgaSectionKeys.Libraries);
    public SectionLayoutItemViewModel ReadListsSection { get; } = new(KomgaSectionKeys.ReadLists);

    private bool _suppressServerSelectionChanged;

    // Sorting settings
    public enum BookSortOrder
    {
        Number,
        Title
    }

    [ObservableProperty]
    private BookSortOrder _selectedBookSortOrder = BookSortOrder.Number;

    [ObservableProperty]
    private bool _isSortDescending;

    partial void OnSelectedBookSortOrderChanged(BookSortOrder value)
    {
        ApplyLocalSorting();
    }

    partial void OnIsSortDescendingChanged(bool value)
    {
        ApplyLocalSorting();
    }

    private void ApplyLocalSorting()
    {
        if (Books.Count <= 1) return;

        var sorted = SelectedBookSortOrder switch
        {
            BookSortOrder.Number => IsSortDescending 
                ? Books.OrderByDescending(b => b.Book.Metadata?.NumberSort ?? b.Book.Number).ToList()
                : Books.OrderBy(b => b.Book.Metadata?.NumberSort ?? b.Book.Number).ToList(),
            BookSortOrder.Title => IsSortDescending
                ? Books.OrderByDescending(b => b.Book.Metadata?.Title ?? b.Book.Name).ToList()
                : Books.OrderBy(b => b.Book.Metadata?.Title ?? b.Book.Name).ToList(),
            _ => Books.ToList()
        };

        for (int i = 0; i < sorted.Count; i++)
        {
            var currentIndex = Books.IndexOf(sorted[i]);
            if (currentIndex != i)
            {
                Books.Move(currentIndex, i);
            }
        }
    }

    private IEnumerable<ObservableCollection<KomgaBookDisplay>> GetBookCollections()
    {
        yield return SearchBookResults;
        yield return KeepReadingBooks;
        yield return OnDeckBooks;
        yield return RecentlyAddedBooks;
        yield return Books;
    }

    private IEnumerable<KomgaBookDisplay> GetTrackedBookDisplays(string bookId)
    {
        return GetBookCollections().SelectMany(collection => collection.Where(book => string.Equals(book.Id, bookId, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateTrackedBookDisplays(string bookId, Action<KomgaBookDisplay> update)
    {
        foreach (var bookDisplay in GetTrackedBookDisplays(bookId))
        {
            update(bookDisplay);
        }

        if (SelectedInfoBookDisplay is not null && string.Equals(SelectedInfoBookDisplay.Id, bookId, StringComparison.OrdinalIgnoreCase))
        {
            update(SelectedInfoBookDisplay);
        }

        if (BookPendingReadListSelection is not null && string.Equals(BookPendingReadListSelection.Id, bookId, StringComparison.OrdinalIgnoreCase))
        {
            update(BookPendingReadListSelection);
        }
    }

    private void SetDownloadState(string bookId, Action<KomgaBookDisplay> update)
    {
        UpdateTrackedBookDisplays(bookId, update);
    }

    private IEnumerable<ObservableCollection<KomgaSeriesDisplay>> GetSeriesCollections()
    {
        yield return SearchSeriesResults;
        yield return RecentlyAddedSeries;
        yield return Series;
    }

    private IEnumerable<KomgaSeriesDisplay> GetTrackedSeriesDisplays(string seriesId)
    {
        return GetSeriesCollections().SelectMany(collection => collection.Where(series => string.Equals(series.Id, seriesId, StringComparison.OrdinalIgnoreCase)));
    }

    private void ApplySeriesDownloadState(KomgaSeriesDisplay seriesDisplay)
    {
        var matchingQueueItems = DownloadQueueItems.Where(item => string.Equals(item.BookDisplay.Book.SeriesId, seriesDisplay.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        var matchingPostDownloadItems = _postDownloadWorkItemsByBookId.Values
            .Where(item => string.Equals(item.DownloadedFile.Book.SeriesId, seriesDisplay.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        seriesDisplay.IsQueuedForDownload = matchingQueueItems.Any(item => item.IsQueued) ||
                                            matchingPostDownloadItems.Any(item => !item.PendingImport.IsProcessing &&
                                                                                  !item.PendingImport.IsCompleted &&
                                                                                  !item.PendingImport.IsFailed);
        seriesDisplay.IsDownloading = matchingQueueItems.Any(item => item.IsDownloading || item.IsCancelling) ||
                                      matchingPostDownloadItems.Any(item => item.PendingImport.IsProcessing);
    }

    private void RefreshSeriesDownloadState(string? seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            return;
        }

        foreach (var seriesDisplay in GetTrackedSeriesDisplays(seriesId))
        {
            ApplySeriesDownloadState(seriesDisplay);
        }

        if (SeriesPendingDownloadSelection is not null &&
            string.Equals(SeriesPendingDownloadSelection.Id, seriesId, StringComparison.OrdinalIgnoreCase))
        {
            ApplySeriesDownloadState(SeriesPendingDownloadSelection);
        }

        UpdateSelectedSeriesDownloadState(seriesId);
    }

    private void UpdateSelectedSeriesDownloadState(string? seriesId)
    {
        if (SelectedSeries is null || string.IsNullOrWhiteSpace(seriesId) || !string.Equals(SelectedSeries.Id, seriesId, StringComparison.OrdinalIgnoreCase))
        {
            if (SelectedSeries is null || string.IsNullOrWhiteSpace(seriesId))
            {
                IsSelectedSeriesQueuedForDownload = false;
                IsSelectedSeriesDownloading = false;
            }

            return;
        }

        var matchingQueueItems = DownloadQueueItems.Where(item => string.Equals(item.BookDisplay.Book.SeriesId, seriesId, StringComparison.OrdinalIgnoreCase)).ToList();
        var matchingPostDownloadItems = _postDownloadWorkItemsByBookId.Values
            .Where(item => string.Equals(item.DownloadedFile.Book.SeriesId, seriesId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        IsSelectedSeriesQueuedForDownload = matchingQueueItems.Any(item => item.IsQueued) ||
                                            matchingPostDownloadItems.Any(item => !item.PendingImport.IsProcessing &&
                                                                                  !item.PendingImport.IsCompleted &&
                                                                                  !item.PendingImport.IsFailed);
        IsSelectedSeriesDownloading = matchingQueueItems.Any(item => item.IsDownloading || item.IsCancelling) ||
                                      matchingPostDownloadItems.Any(item => item.PendingImport.IsProcessing);
    }

    private void RefreshDownloadQueueState()
    {
        QueuedDownloadsCount = DownloadQueueItems.Count;
        IsDownloadQueueActive = DownloadQueueItems.Count > 0;
        var activeDownloads = DownloadQueueItems.Where(item => item.IsDownloading).ToList();
        IsDownloading = activeDownloads.Count > 0;

        if (activeDownloads.Count == 0)
        {
            DownloadingBookName = string.Empty;
            DownloadProgress = 0;
            return;
        }

        DownloadingBookName = activeDownloads.Count == 1
            ? activeDownloads[0].Name
            : $"{activeDownloads.Count} downloads in progress";
        DownloadProgress = activeDownloads.Average(item => item.Progress);
    }

    private void ScheduleRefreshDownloadQueueState()
    {
        if (_downloadQueueStateRefreshPending)
        {
            return;
        }

        _downloadQueueStateRefreshPending = true;
        void RefreshInternal()
        {
            _downloadQueueStateRefreshPending = false;
            RefreshDownloadQueueState();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshInternal();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshInternal, DispatcherPriority.Background);
        }
    }

    private void ApplySectionLayout(AppSettings settings)
    {
        _isApplyingSectionLayout = true;
        try
        {
            ApplyPreference(settings.KomgaSections, KeepReadingSection);
            ApplyPreference(settings.KomgaSections, OnDeckSection);
            ApplyPreference(settings.KomgaSections, RecentBooksSection);
            ApplyPreference(settings.KomgaSections, RecentSeriesSection);
            ApplyPreference(settings.KomgaSections, LibrariesSection);
            ApplyPreference(settings.KomgaSections, ReadListsSection);
            RefreshHomeSectionVisibilityState();
        }
        finally
        {
            _isApplyingSectionLayout = false;
        }
    }

    private IEnumerable<SectionLayoutItemViewModel> GetHomeSectionLayoutStates()
    {
        yield return KeepReadingSection;
        yield return OnDeckSection;
        yield return RecentBooksSection;
        yield return RecentSeriesSection;
        yield return LibrariesSection;
        yield return ReadListsSection;
    }

    private static void ApplyPreference(
        IEnumerable<SectionLayoutSettings> preferences,
        SectionLayoutItemViewModel section)
    {
        var preference = preferences.FirstOrDefault(item => string.Equals(item.Key, section.Key, StringComparison.OrdinalIgnoreCase));
        if (preference is null)
        {
            return;
        }

        section.Apply(preference);
    }

    private void RefreshHomeSectionVisibilityState()
    {
        OnPropertyChanged(nameof(ShowKeepReadingSection));
        OnPropertyChanged(nameof(ShowOnDeckSection));
        OnPropertyChanged(nameof(ShowRecentBooksSection));
        OnPropertyChanged(nameof(ShowRecentSeriesSection));
        OnPropertyChanged(nameof(ShowLibrariesSection));
        OnPropertyChanged(nameof(ShowReadListsSection));
    }

    private int GetAnimationDelayMs()
    {
        var elapsed = DateTime.UtcNow - _lastLoadTime;
        return elapsed.TotalMilliseconds < 800 ? 300 : 50;
    }

    private KomgaBookDisplay CreateBookDisplay(KomgaBook book, Bitmap? thumbnail, bool isDownloaded)
    {
        _downloadItemsByBookId.TryGetValue(book.Id, out var queueItem);
        _postDownloadWorkItemsByBookId.TryGetValue(book.Id, out var postDownloadWorkItem);
        var queuedForPostDownload = postDownloadWorkItem is not null &&
                                    !postDownloadWorkItem.PendingImport.IsProcessing &&
                                    !postDownloadWorkItem.PendingImport.IsCompleted &&
                                    !postDownloadWorkItem.PendingImport.IsFailed;
        var importingPostDownload = postDownloadWorkItem?.PendingImport.IsProcessing ?? false;
        var display = new KomgaBookDisplay
        {
            Book = book,
            Thumbnail = thumbnail,
            IsThumbnailResolved = thumbnail is not null || string.IsNullOrEmpty(book.ThumbnailUrl),
            IsDownloaded = isDownloaded,
            IsQueued = queueItem?.IsQueued ?? queuedForPostDownload || _downloadPendingBookIds.Contains(book.Id),
            IsDownloading = queueItem?.IsDownloading ?? importingPostDownload,
            IsCancelling = queueItem?.IsCancelling ?? false,
            DownloadProgress = queueItem?.Progress ?? postDownloadWorkItem?.PendingImport.Progress ?? 0
        };

        // Trigger loaded transition after a brief delay on UI thread
        _ = Task.Delay(GetAnimationDelayMs()).ContinueWith(_ => Dispatcher.UIThread.Post(() => display.IsLoaded = true));

        return display;
    }

    private KomgaSeriesDisplay CreateSeriesDisplay(KomgaSeries series, Bitmap? thumbnail)
    {
        var display = new KomgaSeriesDisplay
        {
            Series = series,
            Thumbnail = thumbnail,
            IsThumbnailResolved = thumbnail is not null || string.IsNullOrEmpty(series.ThumbnailUrl)
        };
        ApplySeriesDownloadState(display);

        // Trigger loaded transition after a brief delay on UI thread
        _ = Task.Delay(GetAnimationDelayMs()).ContinueWith(_ => Dispatcher.UIThread.Post(() => display.IsLoaded = true));

        return display;
    }

    private KomgaReadListDisplay CreateReadListDisplay(KomgaReadList readList, Bitmap? thumbnail)
    {
        var display = new KomgaReadListDisplay
        {
            ReadList = readList,
            Thumbnail = thumbnail,
            IsThumbnailResolved = thumbnail is not null
        };

        // Trigger loaded transition after a brief delay on UI thread
        _ = Task.Delay(GetAnimationDelayMs()).ContinueWith(_ => Dispatcher.UIThread.Post(() => display.IsLoaded = true));

        return display;
    }

    private static KomgaReadProgress CreateCompletedReadProgress(KomgaBookDisplay bookDisplay)
    {
        return new KomgaReadProgress
        {
            Page = bookDisplay.PagesCount ?? 0,
            Completed = true,
            ReadDate = DateTime.UtcNow,
            Created = bookDisplay.Book.ReadProgress?.Created ?? DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
            DeviceId = bookDisplay.Book.ReadProgress?.DeviceId ?? string.Empty,
            DeviceName = bookDisplay.Book.ReadProgress?.DeviceName ?? string.Empty
        };
    }

    /// <summary>
    /// Username for the server currently being browsed (used for authenticated image loading)
    /// </summary>
    public string? ServerUsername => _activeServer?.Username;

    /// <summary>
    /// Password for the server currently being browsed (used for authenticated image loading)
    /// </summary>
    public string? ServerPassword => _activeServer?.Password;

    /// <summary>
    /// Name of the Komga server currently being browsed.
    /// Note: Reading progress synchronization is independent and uses each comic's original server.
    /// </summary>
    public string? BrowsingServerName => _activeServer?.Name;

    public bool HasMultipleServers => ConfiguredServers.Count > 1;
    public bool ShowKeepReadingSection => KeepReadingSection.IsVisible && (KeepReadingSection.IsExpanded ? HasKeepReading : true);
    public bool ShowOnDeckSection => OnDeckSection.IsVisible && (OnDeckSection.IsExpanded ? HasOnDeck : true);
    public bool ShowRecentBooksSection => RecentBooksSection.IsVisible && (RecentBooksSection.IsExpanded ? HasRecentBooks : true);
    public bool ShowRecentSeriesSection => RecentSeriesSection.IsVisible && (RecentSeriesSection.IsExpanded ? HasRecentSeries : true);
    public bool ShowLibrariesSection => LibrariesSection.IsVisible && (LibrariesSection.IsExpanded ? Libraries.Count > 0 : true);
    public bool ShowReadListsSection => ReadListsSection.IsVisible && (ReadListsSection.IsExpanded ? ReadLists.Count > 0 : true);

    public KomgaViewModel(
        KomgaApiServiceFactory komgaApiServiceFactory,
        LibraryService libraryService,
        ImportQueueService importQueueService,
        SettingsService settingsService,
        DatabaseService databaseService,
        IExternalLinkService externalLinkService)
    {
        _komgaApiServiceFactory = komgaApiServiceFactory;
        _komgaApiService = _inactiveKomgaApiService;
        _libraryService = libraryService;
        _importQueueService = importQueueService;
        _settingsService = settingsService;
        _databaseService = databaseService;
        _externalLinkService = externalLinkService;
        Title = Loc.Instance.Komga;

        RegisterHomeSectionLayoutState(KeepReadingSection);
        RegisterHomeSectionLayoutState(OnDeckSection);
        RegisterHomeSectionLayoutState(RecentBooksSection);
        RegisterHomeSectionLayoutState(RecentSeriesSection);
        RegisterHomeSectionLayoutState(LibrariesSection);
        RegisterHomeSectionLayoutState(ReadListsSection);

        var initialSettings = _settingsService.LoadSettings();
        ApplySectionLayout(initialSettings);
        ApplyDownloadSettings(initialSettings);
        _settingsService.SettingsChanged += (_, settings) =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                // async void (posted async lambda): exceptions must not escape, they would crash the app
                try
                {
                    ApplySectionLayout(settings);
                    ApplyDownloadSettings(settings);
                    RefreshLocalization();
                    
                    RefreshConfiguredServers(settings);
                    
                    if (_activeServer is not null)
                    {
                        var updatedServer = settings.Servers.FirstOrDefault(s => s.Id == _activeServer.Id);
                        // Settings are saved for many unrelated reasons (tab switch, opening a comic, ...).
                        // Only reconnect (a network round trip) when the connection settings actually changed.
                        if (updatedServer is not null && !HasSameConnectionSettings(_activeServer, updatedServer))
                        {
                            await ApplyServerAsync(updatedServer, useCache: true, persistSelection: false);
                        }
                    }
                    
                    await LoadVisibleAndExpandedSectionsAsync(useCache: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"KomgaViewModel: Failed to apply changed settings: {ex.Message}");
                }
            });
        };
        DownloadQueueItems.CollectionChanged += (_, _) => ScheduleRefreshDownloadQueueState();
        _libraryService.LibraryChanged += (s, e) => InvalidateDownloadedComicsCache();
    }

    private void RegisterHomeSectionLayoutState(SectionLayoutItemViewModel section)
    {
        section.PropertyChanged += OnHomeSectionLayoutChanged;
    }

    private void OnHomeSectionLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SectionLayoutItemViewModel.Label))
        {
            return;
        }

        RefreshHomeSectionVisibilityState();

        if (_isApplyingSectionLayout ||
            sender is not SectionLayoutItemViewModel sectionVm)
        {
            return;
        }

        if (e.PropertyName == nameof(SectionLayoutItemViewModel.IsExpanded))
        {
            _ = PersistHomeSectionLayoutAsync();

            if (sectionVm.IsExpanded && sectionVm.IsVisible)
            {
                _ = LoadSectionDataAsync(sectionVm.Key, useCache: true);
            }
        }
    }

    private async Task LoadSectionDataAsync(string key, bool useCache)
    {
        if (!_komgaApiService.IsConfigured)
        {
            return;
        }

        var ct = _loadingCts?.Token ?? CancellationToken.None;

        switch (key)
        {
            case KomgaSectionKeys.KeepReading:
                if (KeepReadingSection.IsVisible && KeepReadingSection.IsExpanded)
                {
                    await LoadKeepReadingBooksAsync(useCache, ct);
                }
                break;
            case KomgaSectionKeys.OnDeck:
                if (OnDeckSection.IsVisible && OnDeckSection.IsExpanded)
                {
                    await LoadOnDeckBooksAsync(useCache, ct);
                }
                break;
            case KomgaSectionKeys.RecentlyAddedBooks:
                if (RecentBooksSection.IsVisible && RecentBooksSection.IsExpanded)
                {
                    await LoadRecentBooksAsync(useCache, ct);
                }
                break;
            case KomgaSectionKeys.RecentlyAddedSeries:
                if (RecentSeriesSection.IsVisible && RecentSeriesSection.IsExpanded)
                {
                    await LoadRecentSeriesAsync(useCache, ct);
                }
                break;
            case KomgaSectionKeys.Libraries:
                await LoadLibrariesAsync(force: true);
                break;
            case KomgaSectionKeys.ReadLists:
                await LoadReadListsInternalAsync(useCache, force: true);
                break;
        }
    }

    private async Task LoadVisibleAndExpandedSectionsAsync(bool useCache = true)
    {
        if (!_komgaApiService.IsConfigured)
        {
            return;
        }

        await LoadLibrariesAsync();
        await LoadReadListsAsync(useCache);
        await LoadSmartListsAsync(useCache);
    }

    private Task PersistHomeSectionLayoutAsync()
    {
        return _settingsService.UpdateSettingsAsync(settings =>
        {
            settings.KomgaSections = SectionLayoutSettings.MergeWithDefaults(
                GetHomeSectionLayoutStates().Select(section => section.ToSettings()),
                SectionLayoutSettings.CreateDefaultKomgaSections());
        });
    }

    private void RefreshLocalization()
    {
        Title = Loc.Instance.Komga;
        OnPropertyChanged(nameof(Title));
        
        // Refresh properties bound to Loc.Instance
        OnPropertyChanged(nameof(Loc.Instance.Komga));
        OnPropertyChanged(nameof(Loc.Instance.SearchPlaceholder));
        OnPropertyChanged(nameof(Loc.Instance.SeriesResults));
        OnPropertyChanged(nameof(Loc.Instance.BookResults));
        OnPropertyChanged(nameof(Loc.Instance.NoResults));
        OnPropertyChanged(nameof(Loc.Instance.SectionKeepReading));
        OnPropertyChanged(nameof(Loc.Instance.SectionOnDeck));
        OnPropertyChanged(nameof(Loc.Instance.SectionRecentlyAddedBooks));
        OnPropertyChanged(nameof(Loc.Instance.SectionRecentlyAddedSeries));
        OnPropertyChanged(nameof(Loc.Instance.SectionLibraries));
        OnPropertyChanged(nameof(Loc.Instance.SectionReadLists));

        RefreshHomeSectionVisibilityState();
    }

    private void ApplyDownloadSettings(AppSettings settings)
    {
        var parallelDownloads = Math.Max(1, settings.KomgaParallelDownloads);
        _maxParallelDownloads = parallelDownloads;
        DownloadThrottleParallel = parallelDownloads;
        _searchLimit = Math.Max(1, settings.KomgaSearchLimit);
        _smartListSize = Math.Max(1, settings.KomgaSmartListSize);
    }

    private Task PersistPendingKomgaDownloadAddedAsync(string bookId, int? serverId)
    {
        return _databaseService.SavePendingKomgaDownloadAsync(bookId, serverId);
    }

    private Task PersistPendingKomgaDownloadRemovedAsync(string bookId)
    {
        return _databaseService.DeletePendingKomgaDownloadAsync(bookId);
    }

    private async Task RestorePendingKomgaDownloadsAsync()
    {
        if (!_komgaApiService.IsConfigured)
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        var serverById = settings.Servers.ToDictionary(server => server.Id);
        var pendingDownloads = (await _databaseService.GetPendingKomgaDownloadsAsync())
            .Where(pendingDownload => !string.IsNullOrWhiteSpace(pendingDownload.BookId))
            .ToList();
        if (pendingDownloads.Count == 0)
        {
            return;
        }

        foreach (var pendingDownload in pendingDownloads)
        {
            var bookId = pendingDownload.BookId;
            if (_downloadPendingBookIds.Contains(bookId))
            {
                continue;
            }

            var pendingServerId = pendingDownload.ServerId;
            if (pendingServerId.HasValue && _activeServer?.Id != pendingServerId.Value)
            {
                if (!serverById.TryGetValue(pendingServerId.Value, out var pendingServer))
                {
                    continue;
                }

                await ApplyServerAsync(pendingServer, useCache: true, persistSelection: false);
                if (!_komgaApiService.IsConfigured || _activeServer?.Id != pendingServerId.Value)
                {
                    continue;
                }
            }

            var existingDisplay = GetTrackedBookDisplays(bookId).FirstOrDefault();
            if (existingDisplay is not null && !existingDisplay.IsDownloaded)
            {
                QueueBookDownload(existingDisplay, pendingServerId);
                continue;
            }

            KomgaBook? book;
            try
            {
                book = await _komgaApiService.GetBookAsync(bookId);
            }
            catch
            {
                continue;
            }

            if (book is null || await CheckIfDownloadedAsync(book))
            {
                continue;
            }

            QueueBookDownload(CreateBookDisplay(book, null, isDownloaded: false), pendingServerId);
        }

        await _databaseService.ReplacePendingKomgaDownloadsAsync(DownloadQueueItems.Select(queueItem => new KomgaPendingDownload
        {
            BookId = queueItem.Id,
            ServerId = queueItem.ServerId
        }));
    }

    partial void OnDownloadThrottleParallelChanged(double value)
    {
        _maxParallelDownloads = Math.Max(1, (int)Math.Round(value));
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchAsync();
    }

    partial void OnSelectedServerChanged(KomgaServer? value)
    {
        if (_suppressServerSelectionChanged || value is null || value.Id == _activeServer?.Id)
        {
            return;
        }

        _ = SwitchServerAsync(value);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText) || !_komgaApiService.IsConfigured)
        {
            IsSearching = false;
            ClearAndReleaseThumbnails(SearchSeriesResults);
            ClearAndReleaseThumbnails(SearchBookResults);
            return;
        }

        IsSearching = true;
        var ct = ResetLoadingCancellation();

        try
        {
            // Search series
            var seriesResults = await _komgaApiService.SearchSeriesAsync(SearchText, 0, _searchLimit);
            ClearAndReleaseThumbnails(SearchSeriesResults);
            var newSeries = new List<KomgaSeriesDisplay>();
            foreach (var s in seriesResults.Content)
            {
                newSeries.Add(CreateSeriesDisplay(s, null));
            }
            if (!ct.IsCancellationRequested)
            {
                foreach (var display in newSeries)
                {
                    SearchSeriesResults.Add(display);
                }
            }

            // Search books
            var bookResults = await _komgaApiService.SearchBooksAsync(SearchText, 0, _searchLimit);
            ClearAndReleaseThumbnails(SearchBookResults);
            var newBooks = new List<KomgaBookDisplay>();
            foreach (var b in bookResults.Content)
            {
                newBooks.Add(CreateBookDisplay(b, null, isDownloaded: false));
            }
            if (!ct.IsCancellationRequested)
            {
                foreach (var display in newBooks)
                {
                    SearchBookResults.Add(display);
                }
            }
            
            _ = Task.Run(() => LoadBooksDownloadStateAsync(newBooks, ct), CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Komga search failed: {ex.Message}");
        }
    }

    private async Task<bool> CheckIfDownloadedAsync(KomgaBook book)
    {
        try
        {
            await EnsureDownloadedComicsCacheLoadedAsync();
            
            if (_downloadedKomgaIds.Contains(book.Id))
            {
                return true;
            }
            
            if (!string.IsNullOrEmpty(book.FileHash) && _downloadedKomgaHashes.Contains(book.FileHash))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking download status for book '{book.Name}': {ex.Message}");
        }
        
        return false;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        IsSearching = false;
        ClearAndReleaseThumbnails(SearchSeriesResults);
        ClearAndReleaseThumbnails(SearchBookResults);
    }

    /// <summary>
    /// Initializes once per view model, called every time the Komga view is shown.
    /// The initialization (connection test, libraries, lists) is only repeated while not connected,
    /// e.g. when no server was configured yet or the server was offline.
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (_initializeTask is { IsCompleted: false })
        {
            await _initializeTask;
            return;
        }

        if (_initializeTask is not null && IsConnected)
        {
            // Reading progress changes while reading, refresh the smart lists when their cache expired
            await LoadSmartListsAsync(useCache: true);
            return;
        }

        _initializeTask = InitializeAsync();
        await _initializeTask;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            var settings = _settingsService.LoadSettings();
            RefreshConfiguredServers(settings);

            var browsingServer = settings.Servers.FirstOrDefault(s => s.Id == settings.ActiveServerId)
                               ?? settings.Servers.FirstOrDefault();

            await ApplyServerAsync(browsingServer, useCache: true, persistSelection: false);
            await RestorePendingKomgaDownloadsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to initialize: {ex.Message}";
            Logger.Error("Failed to initialize Komga", ex);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        ResetCaches();
        
        await InitializeAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private void ConfigureServer()
    {
        ConfigureServerRequested?.Invoke(this, EventArgs.Empty);
    }
    
    [RelayCommand]
    private async Task RefreshSmartListsAsync()
    {
        // Force refresh smart lists
        _keepReadingCacheTime = DateTime.MinValue;
        _onDeckCacheTime = DateTime.MinValue;
        _recentBooksCacheTime = DateTime.MinValue;
        _recentSeriesCacheTime = DateTime.MinValue;
        await LoadSmartListsAsync(useCache: false);
    }
    
    [RelayCommand]
    private async Task RefreshReadListsAsync()
    {
        // Force refresh read lists
        _readListsCacheTime = DateTime.MinValue;
        _currentReadListPage = 0;
        HasMoreReadLists = true;
        ClearAndReleaseThumbnails(ReadLists);
        await LoadReadListsAsync(useCache: false);
    }

    /// <summary>
    /// Loads smart lists (keep reading, on deck, recently added)
    /// </summary>
    private async Task LoadSmartListsAsync(bool useCache = true)
    {
        if (!_komgaApiService.IsConfigured)
        {
            return;
        }

        var ct = _loadingCts?.Token ?? CancellationToken.None;

        if (KeepReadingSection.IsVisible && KeepReadingSection.IsExpanded)
        {
            await LoadKeepReadingBooksAsync(useCache, ct);
        }

        if (OnDeckSection.IsVisible && OnDeckSection.IsExpanded)
        {
            await LoadOnDeckBooksAsync(useCache, ct);
        }

        if (RecentBooksSection.IsVisible && RecentBooksSection.IsExpanded)
        {
            await LoadRecentBooksAsync(useCache, ct);
        }

        if (RecentSeriesSection.IsVisible && RecentSeriesSection.IsExpanded)
        {
            await LoadRecentSeriesAsync(useCache, ct);
        }
    }

    private async Task LoadKeepReadingBooksAsync(bool useCache, CancellationToken ct)
    {
        if (!ShouldRefreshCachedCollection(useCache, _keepReadingCacheTime, KeepReadingBooks.Count))
        {
            return;
        }

        try
        {
            ClearAndReleaseThumbnails(KeepReadingBooks);
            var keepReading = await _komgaApiService.GetBooksInProgressAsync(0, _smartListSize);
            HasKeepReading = keepReading.Content.Count > 0;
            
            var newDisplays = new List<KomgaBookDisplay>();
            foreach (var book in keepReading.Content)
            {
                newDisplays.Add(CreateBookDisplay(book, null, isDownloaded: false));
            }

            if (!ct.IsCancellationRequested)
            {
                foreach (var display in newDisplays)
                {
                    KeepReadingBooks.Add(display);
                }
                _keepReadingCacheTime = DateTime.UtcNow;
                RefreshHomeSectionVisibilityState();
            }

            _ = Task.Run(() => LoadBooksDownloadStateAsync(newDisplays, ct), CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading keep reading: {ex.Message}");
        }
    }

    private async Task LoadOnDeckBooksAsync(bool useCache, CancellationToken ct)
    {
        if (!ShouldRefreshCachedCollection(useCache, _onDeckCacheTime, OnDeckBooks.Count))
        {
            return;
        }

        try
        {
            ClearAndReleaseThumbnails(OnDeckBooks);
            var onDeck = await _komgaApiService.GetBooksOnDeckAsync(0, _smartListSize);
            HasOnDeck = onDeck.Content.Count > 0;
            
            var newDisplays = new List<KomgaBookDisplay>();
            foreach (var book in onDeck.Content)
            {
                newDisplays.Add(CreateBookDisplay(book, null, isDownloaded: false));
            }

            if (!ct.IsCancellationRequested)
            {
                foreach (var display in newDisplays)
                {
                    OnDeckBooks.Add(display);
                }
                _onDeckCacheTime = DateTime.UtcNow;
                RefreshHomeSectionVisibilityState();
            }

            _ = Task.Run(() => LoadBooksDownloadStateAsync(newDisplays, ct), CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading on deck: {ex.Message}");
        }
    }

    private async Task LoadRecentBooksAsync(bool useCache, CancellationToken ct)
    {
        if (!ShouldRefreshCachedCollection(useCache, _recentBooksCacheTime, RecentlyAddedBooks.Count))
        {
            return;
        }

        try
        {
            ClearAndReleaseThumbnails(RecentlyAddedBooks);
            var recentBooks = await _komgaApiService.GetBooksLatestAsync(0, _smartListSize);
            HasRecentBooks = recentBooks.Content.Count > 0;
            
            var newDisplays = new List<KomgaBookDisplay>();
            foreach (var book in recentBooks.Content)
            {
                newDisplays.Add(CreateBookDisplay(book, null, isDownloaded: false));
            }

            if (!ct.IsCancellationRequested)
            {
                foreach (var display in newDisplays)
                {
                    RecentlyAddedBooks.Add(display);
                }
                _recentBooksCacheTime = DateTime.UtcNow;
                RefreshHomeSectionVisibilityState();
            }

            _ = Task.Run(() => LoadBooksDownloadStateAsync(newDisplays, ct), CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading recent books: {ex.Message}");
        }
    }

    private async Task LoadRecentSeriesAsync(bool useCache, CancellationToken ct)
    {
        if (!ShouldRefreshCachedCollection(useCache, _recentSeriesCacheTime, RecentlyAddedSeries.Count))
        {
            return;
        }

        try
        {
            ClearAndReleaseThumbnails(RecentlyAddedSeries);
            var recentSeries = await _komgaApiService.GetSeriesLatestAsync(0, _smartListSize);
            HasRecentSeries = recentSeries.Content.Count > 0;
            
            var newDisplays = new List<KomgaSeriesDisplay>();
            foreach (var s in recentSeries.Content)
            {
                newDisplays.Add(CreateSeriesDisplay(s, null));
            }

            if (!ct.IsCancellationRequested)
            {
                foreach (var display in newDisplays)
                {
                    RecentlyAddedSeries.Add(display);
                }
                _recentSeriesCacheTime = DateTime.UtcNow;
                RefreshHomeSectionVisibilityState();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading recent series: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadLibrariesAsync(bool force = false)
    {
        if (!_komgaApiService.IsConfigured)
        {
            return;
        }

        if (!force && (!LibrariesSection.IsVisible || !LibrariesSection.IsExpanded))
        {
            return;
        }

        try
        {
            var libs = await _komgaApiService.GetLibrariesAsync();
            Libraries.Clear();
            foreach (var lib in libs)
            {
                Libraries.Add(lib);
            }

            RefreshHomeSectionVisibilityState();
        }
        catch (OperationCanceledException)
        {
            // Navigation can cancel in-flight refresh work when switching context.
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load libraries: {ex.Message}";
            Logger.Error("Error loading libraries", ex);
        }
    }

    public async Task LoadSeriesByIdAsync(string seriesId, int? serverId = null)
    {
        // Cancel any ongoing loading
        ResetLoadingCancellation();
        IsBusy = true;

        try
        {
            if (serverId.HasValue && serverId.Value != _activeServer?.Id)
            {
                var settings = _settingsService.LoadSettings();
                RefreshConfiguredServers(settings);

                var requestedServer = settings.Servers.FirstOrDefault(server => server.Id == serverId.Value);
                if (requestedServer is not null)
                {
                    await ApplyServerAsync(requestedServer, useCache: false, persistSelection: true);
                }
            }

            SelectedLibrary = null;
            SelectedSeries = null;
            _currentPage = 0;
            ClearAndReleaseThumbnails(Series);
            ClearAndReleaseThumbnails(Books);

            var series = await _komgaApiService.GetSeriesAsync(seriesId);
            if (series != null)
            {
                // Find and set the library
                if (Libraries.Count == 0) await LoadLibrariesAsync();
                SelectedLibrary = Libraries.FirstOrDefault(l => l.Id == series.LibraryId);
                
                await SelectSeriesAsync(CreateSeriesDisplay(series, null));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when a previous load is replaced by a newer navigation request.
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load series: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SelectLibraryAsync(KomgaLibrary? library)
    {
        // Cancel any ongoing loading
        ResetLoadingCancellation();
        
        SelectedLibrary = library;
        SelectedSeries = null;
        _currentPage = 0;
        HasMoreSeries = true;
        ClearAndReleaseThumbnails(Series);
        ClearAndReleaseThumbnails(Books);
        
        if (library is not null)
        {
            await LoadSeriesAsync();
        }
    }

    [RelayCommand]
    private async Task SwitchServerAsync(KomgaServer? server)
    {
        if (server is null || server.Id == _activeServer?.Id)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await ApplyServerAsync(server, useCache: false, persistSelection: true);
        }, "Failed to switch Komga server");
    }

    private static bool HasSameConnectionSettings(KomgaServer current, KomgaServer updated)
    {
        return string.Equals(current.Name, updated.Name, StringComparison.Ordinal) &&
               string.Equals(current.BaseUrl, updated.BaseUrl, StringComparison.Ordinal) &&
               string.Equals(current.Username, updated.Username, StringComparison.Ordinal) &&
               string.Equals(current.Password, updated.Password, StringComparison.Ordinal) &&
               string.Equals(current.ApiKey, updated.ApiKey, StringComparison.Ordinal) &&
               current.BypassSslValidation == updated.BypassSslValidation &&
               current.CustomHeaders.Count == updated.CustomHeaders.Count &&
               current.CustomHeaders.Zip(updated.CustomHeaders).All(pair =>
                   string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
                   string.Equals(pair.First.Value, pair.Second.Value, StringComparison.Ordinal));
    }

    private async Task ApplyServerAsync(KomgaServer? server, bool useCache, bool persistSelection)
    {
        if (server is null)
        {
            _komgaApiService = _inactiveKomgaApiService;
            _activeServer = null;
            UpdateSelectedServer(null);
            OnPropertyChanged(nameof(ServerUsername));
            OnPropertyChanged(nameof(ServerPassword));
            OnPropertyChanged(nameof(BrowsingServerName));
            ResetNavigationState();
            ErrorMessage = string.Empty;
            IsConnected = false;
            return;
        }

        var previousServer = _activeServer;
        var previousKomgaApiService = _komgaApiService;
        var hasExistingData = Libraries.Count > 0 ||
                              ReadLists.Count > 0 ||
                              KeepReadingBooks.Count > 0 ||
                              OnDeckBooks.Count > 0 ||
                              RecentlyAddedBooks.Count > 0 ||
                              RecentlyAddedSeries.Count > 0 ||
                              Series.Count > 0 ||
                              Books.Count > 0;
        var activeServer = server;

        if (persistSelection)
        {
            await PersistActiveServerSelectionAsync(server.Id);
            activeServer = ConfiguredServers.FirstOrDefault(configuredServer => configuredServer.Id == server.Id) ?? server;
        }

        var activeKomgaApiService = _komgaApiServiceFactory.GetForServer(activeServer);
        var isConnected = await Task.Run(() => activeKomgaApiService.TestConnectionAsync());
        if (!isConnected)
        {
            _komgaApiService = previousServer is not null ? previousKomgaApiService : _inactiveKomgaApiService;
            IsConnected = !persistSelection && hasExistingData;
            UpdateSelectedServer(previousServer);
            return;
        }

        _komgaApiService = activeKomgaApiService;
        _activeServer = activeServer;
        UpdateSelectedServer(activeServer);
        OnPropertyChanged(nameof(ServerUsername));
        OnPropertyChanged(nameof(ServerPassword));
        OnPropertyChanged(nameof(BrowsingServerName));

        var isSameActiveServer = useCache && previousServer?.Id == activeServer.Id;
        if (!isSameActiveServer)
        {
            ResetNavigationState();
        }
        else
        {
            // Same server re-activated with cache: cancel any in-progress loading but preserve view state
            ResetLoadingCancellation();
        }
        ErrorMessage = string.Empty;
        IsConnected = true;

        if (!useCache)
        {
            ResetCaches();
        }

        await LoadLibrariesAsync();
        await LoadReadListsAsync(useCache);
        await LoadSmartListsAsync(useCache);
    }

    private void RefreshConfiguredServers(AppSettings settings)
    {
        var currentServers = ConfiguredServers.ToList();
        var newServers = settings.Servers.ToList();

        // Remove servers that are no longer in settings
        foreach (var server in currentServers)
        {
            if (!newServers.Any(s => s.Id == server.Id))
            {
                ConfiguredServers.Remove(server);
            }
        }

        // Add or update servers
        for (int i = 0; i < newServers.Count; i++)
        {
            var newServer = newServers[i];
            var existingServer = ConfiguredServers.FirstOrDefault(s => s.Id == newServer.Id);

            if (existingServer is null)
            {
                // New server, insert at correct index
                if (i < ConfiguredServers.Count)
                {
                    ConfiguredServers.Insert(i, newServer);
                }
                else
                {
                    ConfiguredServers.Add(newServer);
                }
            }
            else
            {
                // Update existing server properties and replace in list to trigger ComboBox update
                if (existingServer.Name != newServer.Name ||
                    existingServer.BaseUrl != newServer.BaseUrl ||
                    existingServer.Username != newServer.Username ||
                    existingServer.Password != newServer.Password ||
                    existingServer.ApiKey != newServer.ApiKey)
                {
                    var index = ConfiguredServers.IndexOf(existingServer);
                    ConfiguredServers[index] = newServer;
                }
            }
        }

        OnPropertyChanged(nameof(HasMultipleServers));
    }

    private void UpdateSelectedServer(KomgaServer? server)
    {
        _suppressServerSelectionChanged = true;
        SelectedServer = server;
        _suppressServerSelectionChanged = false;
    }

    private void ResetCaches()
    {
        _readListsCacheTime = DateTime.MinValue;
        _keepReadingCacheTime = DateTime.MinValue;
        _onDeckCacheTime = DateTime.MinValue;
        _recentBooksCacheTime = DateTime.MinValue;
        _recentSeriesCacheTime = DateTime.MinValue;
    }

    private void ResetNavigationState()
    {
        ResetLoadingCancellation();
        InvalidateDownloadedComicsCache();

        ClearSearch();
        SelectedInfoBookDisplay = null;
        BookPendingReadListSelection = null;
        SeriesPendingDownloadSelection = null;
        GoBackToLibraries();
        Libraries.Clear();
        ClearAndReleaseThumbnails(ReadLists);
        ClearAndReleaseThumbnails(KeepReadingBooks);
        ClearAndReleaseThumbnails(OnDeckBooks);
        ClearAndReleaseThumbnails(RecentlyAddedBooks);
        ClearAndReleaseThumbnails(RecentlyAddedSeries);
        HasKeepReading = false;
        HasOnDeck = false;
        HasRecentBooks = false;
        HasRecentSeries = false;
        RefreshHomeSectionVisibilityState();
    }

    private Task PersistActiveServerSelectionAsync(int serverId)
    {
        // Only change the active server, a full SaveSettingsAsync could overwrite concurrent changes made elsewhere
        return _settingsService.UpdateSettingsAsync(settings => settings.ActiveServerId = serverId);
    }

    [RelayCommand]
    private async Task LoadSeriesAsync()
    {
        if (!_komgaApiService.IsConfigured || !HasMoreSeries || string.IsNullOrEmpty(SelectedSeriesPrefix))
        {
            return;
        }

        // Capture the token before awaiting: a navigation during the request replaces _loadingCts and the result
        // of this request must then be dropped (reading the token after the await would accept it).
        var ct = _loadingCts?.Token ?? CancellationToken.None;
        if (_seriesPageLoadingFor == ct)
        {
            // A page for the current navigation is already on its way
            return;
        }
        _seriesPageLoadingFor = ct;

        // Only the first page blocks the view, following pages are loaded while the user scrolls (LoadNextPageIfNeeded)
        var isFirstPage = _currentPage == 0;
        try
        {
            if (isFirstPage)
            {
                IsBusy = true;
            }
            else
            {
                IsLoadingMore = true;
            }

            var prefix = SelectedSeriesPrefix;
            var pageSize = Math.Max(1, _settingsService.LoadSettings().KomgaSeriesPageSize);
            
            var result = await _komgaApiService.GetSeriesAsync(
                page: _currentPage,
                size: pageSize,
                libraryId: SelectedLibrary?.Id,
                searchPrefix: prefix);

            if (!ct.IsCancellationRequested)
            {
                HasMoreSeries = !result.Last;
                _currentPage++;

                // Thumbnails are loaded when the cards get realized, see OnThumbnailElementPrepared
                foreach (var s in result.Content)
                {
                    Series.Add(CreateSeriesDisplay(s, null));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Navigation replaced this request
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load series: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error loading series: {ex}");
        }
        finally
        {
            if (_seriesPageLoadingFor == ct)
            {
                _seriesPageLoadingFor = null;
            }

            if (isFirstPage)
            {
                IsBusy = false;
            }
            else
            {
                IsLoadingMore = false;
            }
        }
    }

    /// <summary>
    /// Called by the view when the user scrolled near the end of the loaded items (or the items don't fill the view):
    /// loads the next page of the list that is currently shown.
    /// </summary>
    public void LoadNextPageIfNeeded()
    {
        if (!IsConnected || IsSearching || IsBusy)
        {
            return;
        }

        if (SelectedReadList is not null)
        {
            if (HasMoreBooks)
            {
                _ = LoadBooksForReadListAsync();
            }
        }
        else if (SelectedLibrary is not null && SelectedSeries is null)
        {
            if (HasMoreSeries)
            {
                _ = LoadSeriesAsync();
            }
        }
        // The books of a series are always loaded completely (see LoadBooksAsync)
    }

    /// <summary>
    /// Returns the path for a cached thumbnail. The path includes the server ID so that
    /// thumbnails from different servers do not overlap.
    /// </summary>
    private static string GetThumbnailCachePath(int serverId, string type, string itemId)
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StripWolf", "thumbnails", serverId.ToString(), type);
        Directory.CreateDirectory(cacheDir);
        return Path.Combine(cacheDir, $"{itemId}.jpg");
    }

    /// <summary>
    /// Returns cached thumbnail bytes, fetching and caching them from the server if not found on disk.
    /// </summary>
    private static async Task<byte[]?> GetOrFetchCachedThumbnailAsync(int serverId, string type, string itemId,
        Func<CancellationToken, Task<byte[]?>> fetch, CancellationToken ct)
    {
        var cachePath = GetThumbnailCachePath(serverId, type, itemId);
        if (File.Exists(cachePath))
            return await File.ReadAllBytesAsync(cachePath, ct);
        var bytes = await fetch(ct);
        if (bytes is { Length: > 0 } && !ct.IsCancellationRequested)
            await File.WriteAllBytesAsync(cachePath, bytes, ct);
        return bytes;
    }

    /// <summary>
    /// Called by the view when an ItemsRepeater realized a card: loads the thumbnail of the item.
    /// Only realized (≈ visible) cards hold a decoded thumbnail, so big libraries don't decode thousands of covers.
    /// </summary>
    public void OnThumbnailElementPrepared(object? item)
    {
        if (item is not KomgaThumbnailDisplay display)
        {
            return;
        }

        display.RealizedElementCount++;
        RequestThumbnail(display);
    }

    /// <summary>
    /// Called by the view when an ItemsRepeater recycles a card: releases the thumbnail of the item.
    /// It is reloaded from the disk cache when the card is realized again.
    /// </summary>
    public void OnThumbnailElementClearing(object? item)
    {
        if (item is not KomgaThumbnailDisplay display)
        {
            return;
        }

        display.RealizedElementCount = Math.Max(0, display.RealizedElementCount - 1);
        if (display.RealizedElementCount > 0)
        {
            return;
        }

        // Deferred: a Move in the collection (local sorting) clears the element and realizes it again in the same
        // layout pass, releasing right away would make the thumbnail flicker and decode it again.
        Dispatcher.UIThread.Post(() =>
        {
            if (display.RealizedElementCount == 0 && !IsThumbnailDisplayInUse(display))
            {
                display.ReleaseThumbnail();
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// True when the display is bound by an overlay (book info, pickers) outside of the card grids
    /// </summary>
    private bool IsThumbnailDisplayInUse(KomgaThumbnailDisplay display)
    {
        return ReferenceEquals(display, SelectedInfoBookDisplay) ||
               ReferenceEquals(display, BookPendingReadListSelection) ||
               ReferenceEquals(display, SeriesPendingDownloadSelection);
    }

    /// <summary>
    /// Clears a display collection and releases the thumbnails of the removed items.
    /// The collection is cleared first: that unbinds the cards, only then the bitmaps can be disposed
    /// (KomgaThumbnailDisplay.ReleaseThumbnail additionally defers the dispose until after the next render pass).
    /// </summary>
    private void ClearAndReleaseThumbnails<T>(ObservableCollection<T> collection) where T : KomgaThumbnailDisplay
    {
        if (collection.Count == 0)
        {
            return;
        }

        var removedDisplays = collection.ToList();
        collection.Clear();
        foreach (var display in removedDisplays)
        {
            // Still shown by an overlay: leave it to the GC, disposing it would break the overlay
            if (IsThumbnailDisplayInUse(display))
            {
                continue;
            }

            display.Detach();
        }
    }

    private void RequestThumbnail(KomgaThumbnailDisplay display)
    {
        if (!display.TryBeginThumbnailLoad(out var version))
        {
            return;
        }

        // Capture the server state now, it can change while the request waits for a free slot
        var komgaApiService = _komgaApiService;
        var serverId = _activeServer?.Id ?? 0;
        Func<CancellationToken, Task<byte[]?>> fetch;
        if (display is KomgaSeriesDisplay seriesDisplay)
        {
            var seriesId = seriesDisplay.Id;
            fetch = ct => GetOrFetchCachedThumbnailAsync(serverId, "series", seriesId,
                token => komgaApiService.GetSeriesThumbnailAsync(seriesId, token), ct);
        }
        else if (display is KomgaBookDisplay bookDisplay)
        {
            var bookId = bookDisplay.Id;
            fetch = ct => GetOrFetchCachedThumbnailAsync(serverId, "books", bookId,
                token => komgaApiService.GetBookThumbnailAsync(bookId, token), ct);
        }
        else if (display is KomgaReadListDisplay readListDisplay)
        {
            // Read list thumbnails are composed from their books and change with the list, they are not cached on disk
            var readListId = readListDisplay.Id;
            fetch = ct => komgaApiService.GetReadListThumbnailAsync(readListId, ct);
        }
        else
        {
            display.CompleteThumbnailLoad(version, null);
            return;
        }

        _ = Task.Run(() => LoadThumbnailAsync(display, version, fetch));
    }

    /// <summary>
    /// Fetches (disk cache or server) and decodes a thumbnail on a background thread, then applies it on the UI thread.
    /// </summary>
    private async Task LoadThumbnailAsync(KomgaThumbnailDisplay display, int version, Func<CancellationToken, Task<byte[]?>> fetch)
    {
        Bitmap? thumbnail = null;
        var failed = false;
        try
        {
            await _thumbnailLoadSemaphore.WaitAsync();
            try
            {
                // The card may have been recycled while this request waited for a slot
                if (display.IsThumbnailLoadCurrent(version))
                {
                    var thumbnailBytes = await fetch(CancellationToken.None);
                    if (thumbnailBytes is { Length: > 0 } && display.IsThumbnailLoadCurrent(version))
                    {
                        thumbnail = ThumbnailDecoder.Decode(thumbnailBytes, ThumbnailDecodeWidth);
                    }
                }
            }
            finally
            {
                _thumbnailLoadSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            // Transient (e.g. network) errors must not mark the thumbnail as unavailable for good
            failed = true;
            System.Diagnostics.Debug.WriteLine($"Error loading Komga thumbnail: {ex.Message}");
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => display.CompleteThumbnailLoad(version, thumbnail, isUnavailable: !failed));
        }
        catch (Exception ex)
        {
            thumbnail?.Dispose();
            System.Diagnostics.Debug.WriteLine($"Error applying Komga thumbnail: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadMoreSeriesAsync()
    {
        await LoadSeriesAsync();
    }

    [RelayCommand]
    private async Task SelectSeriesAsync(KomgaSeriesDisplay? seriesDisplay)
    {
        // Cancel any ongoing loading
        ResetLoadingCancellation();

        if (seriesDisplay?.Series is not null)
        {
            if (Libraries.Count == 0)
            {
                await LoadLibrariesAsync();
            }

            SelectedLibrary = Libraries.FirstOrDefault(library => library.Id == seriesDisplay.Series.LibraryId);
        }

        SelectedReadList = null;
        SelectedSeries = seriesDisplay?.Series;
        // Save series pagination position before resetting for book pagination
        _savedSeriesPage = _currentPage;
        _currentPage = 0;
        HasMoreBooks = true;
        ClearAndReleaseThumbnails(Books);
        
        // Clear search state when navigating to a series
        if (IsSearching)
        {
            IsSearching = false;
            SearchText = string.Empty;
            ClearAndReleaseThumbnails(SearchSeriesResults);
            ClearAndReleaseThumbnails(SearchBookResults);
        }
        
        if (SelectedSeries is not null)
        {
            _ = LoadBooksAsync();
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedSeriesAsync()
    {
        if (SelectedSeries is null || IsSelectedSeriesQueuedForDownload || IsSelectedSeriesDownloading)
        {
            return;
        }

        var seriesDisplay = CreateSeriesDisplay(SelectedSeries, null);

        // Skip the popup when there is no reason to ask: if none of the loaded books are
        // already read or already downloaded, "all missing" equals "unread missing", so
        // we can go straight to downloading everything.
        var hasReadOrDownloaded = Books.Any(b => b.IsRead || b.IsDownloaded);
        if (!hasReadOrDownloaded)
        {
            await QueueSeriesDownloadAsync(seriesDisplay, unreadOnly: false);
            return;
        }

        SeriesPendingDownloadSelection = seriesDisplay;
    }

    [RelayCommand]
    private async Task LoadBooksAsync()
    {
        if (!_komgaApiService.IsConfigured || SelectedSeries is null || !HasMoreBooks)
        {
            return;
        }

        // Capture the token before awaiting, so a navigation during the request drops its result
        var ct = _loadingCts?.Token ?? CancellationToken.None;
        try
        {
            IsBusy = true;
            var pageSize = Math.Max(1, _settingsService.LoadSettings().KomgaSeriesPageSize);
            var result = await _komgaApiService.GetBooksForSeriesAsync(
                SelectedSeries.Id,
                page: _currentPage,
                size: pageSize);

            var newDisplays = new List<KomgaBookDisplay>();
            foreach (var b in result.Content)
            {
                newDisplays.Add(CreateBookDisplay(b, null, isDownloaded: false));
            }

            if (!ct.IsCancellationRequested)
            {
                HasMoreBooks = !result.Last;
                _currentPage++;
                
                foreach (var display in newDisplays)
                {
                    Books.Add(display);
                }
                ApplyLocalSorting();
            }
            
            // Load the download status in background, thumbnails are loaded when the cards get realized
            _ = Task.Run(() => LoadBooksDownloadStateAsync(newDisplays, ct), CancellationToken.None);
            
            // Auto-load remaining pages in the background without showing the busy overlay.
            // Unlike the series list (paged while scrolling), the books of a series are loaded completely:
            // the sorting (number/title, ascending/descending) is done locally and needs all books.
            // Only the book metadata is loaded, the thumbnails are loaded on demand for the visible cards.
            if (HasMoreBooks && !ct.IsCancellationRequested && SelectedSeries is not null)
            {
                _ = AutoLoadRemainingBooksAsync(SelectedSeries.Id, ct);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load books: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error loading books: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Continues loading all remaining book pages in the background without showing the busy overlay.
    /// </summary>
    private async Task AutoLoadRemainingBooksAsync(string seriesId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && HasMoreBooks)
        {
            try
            {
                var pageSize = Math.Max(1, _settingsService.LoadSettings().KomgaSeriesPageSize);
                var page = _currentPage;
                var result = await _komgaApiService.GetBooksForSeriesAsync(seriesId, page, pageSize);

                if (ct.IsCancellationRequested)
                    break;

                var newDisplays = new List<KomgaBookDisplay>();
                foreach (var b in result.Content)
                {
                    newDisplays.Add(CreateBookDisplay(b, null, isDownloaded: false));
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        HasMoreBooks = !result.Last;
                        _currentPage++;
                        foreach (var display in newDisplays)
                        {
                            Books.Add(display);
                        }
                        ApplyLocalSorting();
                    }
                });

                await LoadBooksDownloadStateAsync(newDisplays, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error auto-loading remaining books: {ex.Message}");
                break;
            }
        }
    }

    [RelayCommand]
    private void CycleSortOrder()
    {
        SelectedBookSortOrder = SelectedBookSortOrder == BookSortOrder.Number ? BookSortOrder.Title : BookSortOrder.Number;
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        IsSortDescending = !IsSortDescending;
    }

    /// <summary>
    /// Determines whether the books are already downloaded (in the background) and updates the display models in one go.
    /// </summary>
    private async Task LoadBooksDownloadStateAsync(IReadOnlyList<KomgaBookDisplay> displays, CancellationToken ct)
    {
        if (displays.Count == 0)
        {
            return;
        }

        try
        {
            var downloadStates = new List<(KomgaBookDisplay Display, bool IsDownloaded)>(displays.Count);
            foreach (var display in displays)
            {
                if (ct.IsCancellationRequested) return;
                downloadStates.Add((display, await CheckIfDownloadedAsync(display.Book)));
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                foreach (var (display, isDownloaded) in downloadStates)
                {
                    display.IsDownloaded = isDownloaded;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading the download state of books: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadMoreBooksAsync()
    {
        await LoadBooksAsync();
    }

    [RelayCommand]
    private async Task DownloadBookAsync(KomgaBookDisplay? bookDisplay)
    {
        if (bookDisplay?.Book is null || bookDisplay.IsDownloaded || _downloadPendingBookIds.Contains(bookDisplay.Id))
        {
            return;
        }

        QueueBookDownload(bookDisplay);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenBookOnline(KomgaBookDisplay? bookDisplay)
    {
        if (bookDisplay?.Book is not null && _activeServer is not null)
        {
            var url = $"{_activeServer.BaseUrl.TrimEnd('/')}/book/{bookDisplay.Book.Id}";
            _externalLinkService.OpenUrl(url);
        }
    }

    [RelayCommand]
    private void OpenBookSeriesOnline(KomgaBookDisplay? bookDisplay)
    {
        if (bookDisplay?.Book is not null && _activeServer is not null)
        {
            var url = $"{_activeServer.BaseUrl.TrimEnd('/')}/series/{bookDisplay.Book.SeriesId}";
            _externalLinkService.OpenUrl(url);
        }
    }

    [RelayCommand]
    private void OpenSelectedSeriesOnline()
    {
        if (SelectedSeries is not null && _activeServer is not null)
        {
            var url = $"{_activeServer.BaseUrl.TrimEnd('/')}/series/{SelectedSeries.Id}";
            _externalLinkService.OpenUrl(url);
        }
    }

    [RelayCommand]
    private async Task DownloadSeriesAsync(KomgaSeriesDisplay? seriesDisplay)
    {
        if (seriesDisplay?.Series is null || seriesDisplay.IsDownloading || seriesDisplay.IsQueuedForDownload)
        {
            return;
        }

        // The series-list download button is a "download all missing" action — always skip the
        // popup. Already-downloaded books are filtered out inside QueueSeriesDownloadAsync.
        // The "Unread only" option is available from the books view (DownloadSelectedSeriesAsync).
        await QueueSeriesDownloadAsync(seriesDisplay, unreadOnly: false);
    }

    [RelayCommand]
    private void CancelSeriesDownloadSelection()
    {
        SeriesPendingDownloadSelection = null;
    }

    [RelayCommand]
    private async Task ConfirmSeriesDownloadAllAsync()
    {
        if (SeriesPendingDownloadSelection is null)
        {
            return;
        }

        var seriesDisplay = SeriesPendingDownloadSelection;
        SeriesPendingDownloadSelection = null;
        await QueueSeriesDownloadAsync(seriesDisplay, unreadOnly: false);
    }

    [RelayCommand]
    private async Task ConfirmSeriesDownloadUnreadAsync()
    {
        if (SeriesPendingDownloadSelection is null)
        {
            return;
        }

        var seriesDisplay = SeriesPendingDownloadSelection;
        SeriesPendingDownloadSelection = null;
        await QueueSeriesDownloadAsync(seriesDisplay, unreadOnly: true);
    }

    private async Task QueueSeriesDownloadAsync(KomgaSeriesDisplay seriesDisplay, bool unreadOnly)
    {
        await ExecuteAsync(async () =>
        {
            var seriesBooks = await _komgaApiService.GetAllBooksForSeriesAsync(seriesDisplay.Id);
            var candidateBooks = unreadOnly
                ? seriesBooks.Where(book => !(book.ReadProgress?.Completed ?? false)).ToList()
                : seriesBooks;
            var booksToQueue = new List<KomgaBookDisplay>();

            foreach (var book in candidateBooks)
            {
                if (_downloadPendingBookIds.Contains(book.Id))
                {
                    continue;
                }

                if (await CheckIfDownloadedAsync(book))
                {
                    continue;
                }

                var existingDisplay = GetTrackedBookDisplays(book.Id).FirstOrDefault();
                booksToQueue.Add(existingDisplay ?? CreateBookDisplay(book, null, false));
            }

            if (booksToQueue.Count == 0)
            {
                ErrorMessage = unreadOnly
                    ? candidateBooks.Count == 0
                        ? $"'{seriesDisplay.Name}' has no unread books to download."
                        : $"All unread books in '{seriesDisplay.Name}' are already downloaded."
                    : $"All books in '{seriesDisplay.Name}' are already downloaded.";
                return;
            }

            foreach (var bookDisplay in booksToQueue)
            {
                QueueBookDownload(bookDisplay);
            }
        }, $"Failed to queue series '{seriesDisplay.Name}' for download");
    }

    [RelayCommand]
    private async Task MarkBookAsReadAsync(KomgaBookDisplay? bookDisplay)
    {
        if (bookDisplay is null || bookDisplay.IsRead)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var success = await _komgaApiService.MarkBookAsReadAsync(bookDisplay.Id, bookDisplay.PagesCount ?? 0);
            if (!success)
            {
                throw new InvalidOperationException($"Komga rejected marking '{bookDisplay.Name}' as read.");
            }

            UpdateTrackedBookDisplays(bookDisplay.Id, trackedBook =>
            {
                trackedBook.Book.ReadProgress = CreateCompletedReadProgress(trackedBook);
                trackedBook.RefreshComputedProperties();
            });

            // Remove from "Keep Reading" and "On Deck" since the book is now fully read
            var keepReadingItem = KeepReadingBooks.FirstOrDefault(b => string.Equals(b.Id, bookDisplay.Id, StringComparison.OrdinalIgnoreCase));
            if (keepReadingItem is not null)
            {
                KeepReadingBooks.Remove(keepReadingItem);
                HasKeepReading = KeepReadingBooks.Count > 0;
            }
            var onDeckItem = OnDeckBooks.FirstOrDefault(b => string.Equals(b.Id, bookDisplay.Id, StringComparison.OrdinalIgnoreCase));
            if (onDeckItem is not null)
            {
                OnDeckBooks.Remove(onDeckItem);
                HasOnDeck = OnDeckBooks.Count > 0;
            }
            RefreshHomeSectionVisibilityState();
        }, $"Failed to mark '{bookDisplay.Name}' as read");
    }

    [RelayCommand]
    private void ShowBookInfo(KomgaBookDisplay? bookDisplay)
    {
        if (bookDisplay is null)
        {
            return;
        }

        SelectedInfoBookDisplay = bookDisplay;
    }

    [RelayCommand]
    private void CloseBookInfo()
    {
        SelectedInfoBookDisplay = null;
    }

    [RelayCommand]
    private async Task ViewSelectedBookSeriesAsync()
    {
        if (SelectedInfoBookDisplay is null)
        {
            return;
        }

        var seriesId = SelectedInfoBookDisplay.Book.SeriesId;
        SelectedInfoBookDisplay = null;
        await LoadSeriesByIdAsync(seriesId);
    }

    [RelayCommand]
    private async Task ShowReadListPickerAsync(KomgaBookDisplay? bookDisplay)
    {
        if (bookDisplay is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            IsLoadingReadListSelection = true;
            try
            {
                var readLists = await _komgaApiService.GetAllReadListsAsync();
                AvailableReadLists.Clear();
                foreach (var readList in readLists.OrderBy(readList => readList.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    AvailableReadLists.Add(readList);
                }
            }
            finally
            {
                IsLoadingReadListSelection = false;
            }

            if (AvailableReadLists.Count == 0)
            {
                ErrorMessage = "No Komga read lists are available on the active server.";
                return;
            }

            BookPendingReadListSelection = bookDisplay;
        }, $"Failed to load read lists for '{bookDisplay.Name}'");
    }

    [RelayCommand]
    private void CancelReadListSelection()
    {
        BookPendingReadListSelection = null;
    }

    [RelayCommand]
    private async Task AddBookToReadListAsync(KomgaReadList? readList)
    {
        if (readList is null || BookPendingReadListSelection is null)
        {
            return;
        }

        var pendingBook = BookPendingReadListSelection;

        await ExecuteAsync(async () =>
        {
            var latestReadList = await _komgaApiService.GetReadListAsync(readList.Id) ?? readList;
            var updatedBookIds = latestReadList.BookIds.ToList();

            if (!updatedBookIds.Contains(pendingBook.Id, StringComparer.OrdinalIgnoreCase))
            {
                updatedBookIds.Add(pendingBook.Id);
            }

            var success = await _komgaApiService.UpdateReadListAsync(
                latestReadList.Id,
                latestReadList.Name,
                latestReadList.Summary,
                updatedBookIds,
                latestReadList.Ordered);

            if (!success)
            {
                throw new InvalidOperationException($"Komga rejected adding '{pendingBook.Name}' to '{latestReadList.Name}'.");
            }

            BookPendingReadListSelection = null;
        }, $"Failed to add '{pendingBook.Name}' to '{readList.Name}'");
    }

    private void QueueBookDownload(KomgaBookDisplay bookDisplay, int? serverId = null)
    {
        if (!_downloadPendingBookIds.Add(bookDisplay.Id))
        {
            return;
        }

        var queueItem = new KomgaDownloadQueueItem
        {
            BookDisplay = bookDisplay,
            ServerId = serverId ?? _activeServer?.Id,
            ServerName = serverId.HasValue ? ConfiguredServers.FirstOrDefault(server => server.Id == serverId.Value)?.Name : _activeServer?.Name,
            IsQueued = true,
            Progress = 0,
            IsFailed = false,
            ErrorMessage = null
        };

        _downloadItemsByBookId[bookDisplay.Id] = queueItem;
        DownloadQueueItems.Add(queueItem);
        UpdateTrackedBookDisplays(bookDisplay.Id, trackedBook =>
        {
            trackedBook.IsQueued = true;
            trackedBook.IsDownloading = false;
            trackedBook.IsCancelling = false;
            trackedBook.DownloadProgress = 0;
        });

        if (!GetTrackedBookDisplays(bookDisplay.Id).Any())
        {
            bookDisplay.IsQueued = true;
        }

        RefreshSeriesDownloadState(bookDisplay.Book.SeriesId);

        if (!_isProcessingQueue)
        {
            _isProcessingQueue = true;
            _ = ProcessDownloadQueueAsync();
        }

        _ = PersistPendingKomgaDownloadAddedAsync(bookDisplay.Id, queueItem.ServerId);
    }

    private void ResetDownloadState(string bookId)
    {
        SetDownloadState(bookId, trackedBook =>
        {
            trackedBook.IsQueued = false;
            trackedBook.IsDownloading = false;
            trackedBook.IsCancelling = false;
            if (!trackedBook.IsDownloaded)
            {
                trackedBook.DownloadProgress = 0;
            }
        });
    }

    private static PendingImport CreatePostDownloadPendingImport(KomgaDownloadedFile downloadedFile)
    {
        return new PendingImport
        {
            FilePath = downloadedFile.FilePath,
            FileName = $"{downloadedFile.Book.SeriesTitle} - {downloadedFile.Book.Name}",
            Status = downloadedFile.RequiresConversion ? "Queued for conversion..." : "Queued for import..."
        };
    }

    private async Task QueueDownloadedBookForImportAsync(KomgaDownloadQueueItem queueItem, KomgaDownloadedFile downloadedFile)
    {
        var pendingImport = CreatePostDownloadPendingImport(downloadedFile);
        var workItem = new KomgaPostDownloadWorkItem(downloadedFile, pendingImport);

        _postDownloadWorkItemsByBookId[queueItem.Id] = workItem;
        await _importQueueService.EnqueueAsync(pendingImport);

        var shouldStartProcessor = false;
        lock (_postDownloadQueueLock)
        {
            _postDownloadQueue.Enqueue(workItem);
            if (!_isProcessingPostDownloadQueue)
            {
                _isProcessingPostDownloadQueue = true;
                shouldStartProcessor = true;
            }
        }

        SetDownloadState(queueItem.Id, trackedBook =>
        {
            trackedBook.IsQueued = true;
            trackedBook.IsDownloading = false;
            trackedBook.IsCancelling = false;
            trackedBook.DownloadProgress = 0;
        });

        RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);

        if (shouldStartProcessor)
        {
            _ = ProcessPostDownloadQueueAsync();
        }
    }

    private async Task ProcessPostDownloadQueueAsync()
    {
        try
        {
            while (true)
            {
                KomgaPostDownloadWorkItem? workItem;
                lock (_postDownloadQueueLock)
                {
                    workItem = _postDownloadQueue.Count > 0 ? _postDownloadQueue.Dequeue() : null;
                    if (workItem is null)
                    {
                        _isProcessingPostDownloadQueue = false;
                        return;
                    }
                }

                await RunPostDownloadImportAsync(workItem);
            }
        }
        finally
        {
            lock (_postDownloadQueueLock)
            {
                if (_postDownloadQueue.Count == 0)
                {
                    _isProcessingPostDownloadQueue = false;
                }
            }
        }
    }

    private async Task RunPostDownloadImportAsync(KomgaPostDownloadWorkItem workItem)
    {
        var bookId = workItem.DownloadedFile.Book.Id;
        var seriesId = workItem.DownloadedFile.Book.SeriesId;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            workItem.PendingImport.IsProcessing = true;
            workItem.PendingImport.IsCompleted = false;
            workItem.PendingImport.IsFailed = false;
            workItem.PendingImport.ErrorMessage = null;
            workItem.PendingImport.Progress = 0;
            workItem.PendingImport.Status = workItem.DownloadedFile.RequiresConversion ? "Converting..." : "Importing...";

            SetDownloadState(bookId, trackedBook =>
            {
                trackedBook.IsQueued = false;
                trackedBook.IsDownloading = true;
                trackedBook.IsCancelling = false;
                trackedBook.DownloadProgress = 0;
            });
            RefreshSeriesDownloadState(seriesId);
        });

        try
        {
            var progress = UiProgressThrottle.Create(value =>
            {
                workItem.PendingImport.Progress = value;
                workItem.PendingImport.Status = workItem.DownloadedFile.RequiresConversion
                    ? $"Converting... {value:P0}"
                    : $"Importing... {value:P0}";
                SetDownloadState(bookId, trackedBook => trackedBook.DownloadProgress = value);
                RefreshSeriesDownloadState(seriesId);
            });

            await _libraryService.ImportDownloadedKomgaBookAsync(workItem.DownloadedFile, progress);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                workItem.PendingImport.IsProcessing = false;
                workItem.PendingImport.IsCompleted = true;
                workItem.PendingImport.Progress = 1;
                workItem.PendingImport.Status = "Completed";

                SetDownloadState(bookId, trackedBook =>
                {
                    trackedBook.IsQueued = false;
                    trackedBook.IsDownloading = false;
                    trackedBook.IsCancelling = false;
                    trackedBook.IsDownloaded = true;
                    trackedBook.DownloadProgress = 1;
                });

                _downloadPendingBookIds.Remove(bookId);
                _ = PersistPendingKomgaDownloadRemovedAsync(bookId);
                _postDownloadWorkItemsByBookId.Remove(bookId);
                RefreshSeriesDownloadState(seriesId);
            });

            ScheduleCompletedPostDownloadImportRemoval(workItem.PendingImport);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                workItem.PendingImport.IsProcessing = false;
                workItem.PendingImport.IsFailed = true;
                workItem.PendingImport.Status = "Failed";
                workItem.PendingImport.ErrorMessage = ex.Message;

                _downloadPendingBookIds.Remove(bookId);
                _ = PersistPendingKomgaDownloadRemovedAsync(bookId);
                _postDownloadWorkItemsByBookId.Remove(bookId);
                ResetDownloadState(bookId);
                RefreshSeriesDownloadState(seriesId);
            });

            ErrorMessage = $"Failed to import '{workItem.PendingImport.FileName}'.";
            System.Diagnostics.Debug.WriteLine($"Post-download import error: {ex}");
        }
    }

    private void ScheduleCompletedPostDownloadImportRemoval(PendingImport pendingImport)
    {
        _ = RemoveCompletedPostDownloadImportAfterDelayAsync(pendingImport);
    }

    private async Task RemoveCompletedPostDownloadImportAfterDelayAsync(PendingImport pendingImport)
    {
        await Task.Delay(500);
        await _importQueueService.RemoveAsync(pendingImport);
    }

    private async Task<bool> IsDownloadConnectionAvailableAsync(int? serverId, bool forceProbe = false)
    {
        if (!serverId.HasValue)
        {
            return false;
        }

        if (!forceProbe &&
            _lastDownloadConnectionProbeServerId == serverId.Value &&
            DateTime.UtcNow - _lastDownloadConnectionProbeUtc < DownloadConnectionProbeInterval)
        {
            return _lastDownloadConnectionProbeResult;
        }

        var settings = _settingsService.LoadSettings();
        var server = settings.Servers.FirstOrDefault(s => s.Id == serverId.Value);
        if (server is null)
        {
            return false;
        }

        _lastDownloadConnectionProbeUtc = DateTime.UtcNow;
        _lastDownloadConnectionProbeServerId = serverId.Value;
        
        var apiService = _komgaApiServiceFactory.GetForServer(server);
        _lastDownloadConnectionProbeResult = await Task.Run(() => apiService.TestConnectionAsync());
        return _lastDownloadConnectionProbeResult;
    }

    private void PauseQueueForConnectionLoss(int? serverId)
    {
        if (serverId.HasValue)
        {
            _offlineServerIds.Add(serverId.Value);
        }

        foreach (var queueItem in DownloadQueueItems.Where(item => item.IsDownloading && item.ServerId == serverId).ToList())
        {
            _pauseRequestedBookIds.Add(queueItem.Id);
            if (_downloadCancellationTokens.TryGetValue(queueItem.Id, out var cts))
            {
                cts.Cancel();
            }
        }
    }

    private void ResumeQueueAfterConnectionRestore(int? serverId)
    {
        if (serverId.HasValue)
        {
            _offlineServerIds.Remove(serverId.Value);
            
            foreach (var queueItem in DownloadQueueItems.Where(item => item.IsQueued && item.ServerId == serverId.Value && item.ErrorMessage == "Paused, waiting for connection...").ToList())
            {
                queueItem.ErrorMessage = null;
            }
        }
    }

    private IEnumerable<KomgaDownloadQueueItem> OrderQueuedItemsForScheduling(IReadOnlyList<KomgaDownloadQueueItem> queuedItems)
    {
        if (!_recentFailedServerId.HasValue)
        {
            return queuedItems;
        }

        var alternateServerItems = queuedItems.Where(item => item.ServerId != _recentFailedServerId).ToList();
        if (alternateServerItems.Count == 0)
        {
            return queuedItems;
        }

        return alternateServerItems.Concat(queuedItems.Where(item => item.ServerId == _recentFailedServerId));
    }

    private static bool IsLikelyConnectivityIssue(Exception ex)
    {
        if (ex is InvalidOperationException)
        {
            return false;
        }

        if (ex is HttpRequestException or IOException or TimeoutException)
        {
            return true;
        }

        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("host", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("exception_was_thrown", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("runtimeexception", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("503", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("502", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("504", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSpeedText(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return string.Empty;
        }

        var units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        var value = bytesPerSecond;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.0} {units[unitIndex]}";
    }

    private static string FormatEtaText(double remainingSeconds)
    {
        if (remainingSeconds <= 0 || double.IsInfinity(remainingSeconds) || double.IsNaN(remainingSeconds))
        {
            return string.Empty;
        }

        var remaining = TimeSpan.FromSeconds(remainingSeconds);
        if (remaining.TotalHours >= 1)
        {
            return $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }

        return $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private async Task ProcessDownloadQueueAsync()
    {
        try
        {
            while (true)
            {
                var activeCount = DownloadQueueItems.Count(item => item.IsDownloading || item.IsCancelling);
                var queuedItems = DownloadQueueItems.Where(item => item.IsQueued && !item.IsCancelling).ToList();

                if (activeCount == 0 && queuedItems.Count == 0)
                {
                    break;
                }

                if (!IsDownloadQueuePaused && queuedItems.Count > 0)
                {
                    // Group active/cancelling downloads by ServerId to track running downloads per server
                    var activeByServer = DownloadQueueItems
                        .Where(item => item.IsDownloading || item.IsCancelling)
                        .GroupBy(item => item.ServerId)
                        .ToDictionary(g => g.Key ?? 0, g => g.Count());

                    var candidates = OrderQueuedItemsForScheduling(queuedItems).ToList();
                    var maxParallel = Math.Max(1, _maxParallelDownloads);

                    foreach (var queueItem in candidates)
                    {
                        if (queueItem.IsDownloading || queueItem.IsCancelling)
                        {
                            continue;
                        }

                        activeByServer.TryGetValue(queueItem.ServerId ?? 0, out var activeForServerCount);
                        if (activeForServerCount < maxParallel)
                        {
                            var hasConnection = await IsDownloadConnectionAvailableAsync(queueItem.ServerId);
                            if (hasConnection)
                            {
                                ResumeQueueAfterConnectionRestore(queueItem.ServerId);
                                
                                activeByServer[queueItem.ServerId ?? 0] = activeForServerCount + 1;
                                
                                var cts = new CancellationTokenSource();
                                _downloadCancellationTokens[queueItem.Id] = cts;
                                _ = RunDownloadAsync(queueItem, cts.Token);
                            }
                            else
                            {
                                PauseQueueForConnectionLoss(queueItem.ServerId);
                            }
                        }
                    }
                }

                ScheduleRefreshDownloadQueueState();
                await Task.Delay(activeCount > 0 ? 250 : 1000);
            }
        }
        finally
        {
            foreach (var cts in _downloadCancellationTokens.Values)
            {
                cts.Dispose();
            }

            _downloadCancellationTokens.Clear();
            _cancelRequestedBookIds.Clear();
            _pauseRequestedBookIds.Clear();
            _isProcessingQueue = false;
            ScheduleRefreshDownloadQueueState();
        }
    }

    private async Task RunDownloadAsync(KomgaDownloadQueueItem queueItem, CancellationToken token)
    {
        try
        {
            SetDownloadState(queueItem.Id, trackedBook =>
            {
                trackedBook.IsQueued = false;
                trackedBook.IsDownloading = true;
                trackedBook.IsCancelling = false;
                trackedBook.DownloadProgress = 0;
            });
            queueItem.IsQueued = false;
            queueItem.IsDownloading = true;
            queueItem.IsCancelling = false;
            queueItem.IsFailed = false;
            queueItem.ErrorMessage = null;
            queueItem.Progress = 0;
            queueItem.SpeedText = string.Empty;
            queueItem.EtaText = string.Empty;
            RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
            ScheduleRefreshDownloadQueueState();

            for (var attempt = 1; attempt <= MaxDownloadRetryCount; attempt++)
            {
                try
                {
                    var speedStopwatch = Stopwatch.StartNew();
                    var lastSpeedSampleAt = speedStopwatch.Elapsed;
                    var lastSampleBytes = 0L;
                    var progress = UiProgressThrottle.Create(p =>
                    {
                        queueItem.Progress = p;
                        SetDownloadState(queueItem.Id, trackedBook => trackedBook.DownloadProgress = p);
                        ScheduleRefreshDownloadQueueState();
                    });
                    var detailedProgress = UiProgressThrottle.Create<KomgaDownloadProgress>(downloadProgress =>
                    {
                        var currentSampleAt = speedStopwatch.Elapsed;
                        var elapsed = (currentSampleAt - lastSpeedSampleAt).TotalSeconds;
                        if (elapsed >= 0.4 && downloadProgress.DownloadedBytes >= lastSampleBytes)
                        {
                            var speed = (downloadProgress.DownloadedBytes - lastSampleBytes) / elapsed;
                            queueItem.SpeedText = FormatSpeedText(speed);
                            if (downloadProgress.TotalBytes.HasValue && speed > 0)
                            {
                                var remainingBytes = Math.Max(0, downloadProgress.TotalBytes.Value - downloadProgress.DownloadedBytes);
                                queueItem.EtaText = FormatEtaText(remainingBytes / speed);
                            }
                            else
                            {
                                queueItem.EtaText = string.Empty;
                            }

                            lastSampleBytes = downloadProgress.DownloadedBytes;
                            lastSpeedSampleAt = currentSampleAt;
                        }
                    });

                    var downloadedFile = await _libraryService.DownloadKomgaBookAsync(
                        queueItem.BookDisplay.Book,
                        queueItem.ServerId,
                        progress,
                        detailedProgress,
                        token);
                    queueItem.Progress = 1.0;
                    queueItem.SpeedText = string.Empty;
                    queueItem.EtaText = string.Empty;
                    _recentFailedServerId = null;
                    _downloadItemsByBookId.Remove(queueItem.Id);
                    DownloadQueueItems.Remove(queueItem);

                    if (downloadedFile is null)
                    {
                        SetDownloadState(queueItem.Id, trackedBook =>
                        {
                            trackedBook.IsQueued = false;
                            trackedBook.IsDownloading = false;
                            trackedBook.IsCancelling = false;
                            trackedBook.IsDownloaded = true;
                            trackedBook.DownloadProgress = 1.0;
                        });
                        queueItem.BookDisplay.IsDownloaded = true;
                        _downloadPendingBookIds.Remove(queueItem.Id);
                        _ = PersistPendingKomgaDownloadRemovedAsync(queueItem.Id);
                    }
                    else
                    {
                        await QueueDownloadedBookForImportAsync(queueItem, downloadedFile);
                    }

                    RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
                    ScheduleRefreshDownloadQueueState();
                    return;
                }
                catch (OperationCanceledException)
                {
                    if (_pauseRequestedBookIds.Remove(queueItem.Id))
                    {
                        var pausedProgress = queueItem.Progress;
                        SetDownloadState(queueItem.Id, trackedBook =>
                        {
                            trackedBook.IsQueued = true;
                            trackedBook.IsDownloading = false;
                            trackedBook.IsCancelling = false;
                            trackedBook.DownloadProgress = pausedProgress;
                        });
                        queueItem.IsQueued = true;
                        queueItem.IsDownloading = false;
                        queueItem.IsCancelling = false;
                        queueItem.SpeedText = string.Empty;
                        queueItem.EtaText = string.Empty;
                        RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
                        ScheduleRefreshDownloadQueueState();
                        return;
                    }

                    // Otherwise, treat it as a cancellation (remove from cancel requests if present, and clean up)
                    _cancelRequestedBookIds.Remove(queueItem.Id);
                    _libraryService.CleanupPendingKomgaDownload(queueItem.BookDisplay.Book);
                    _downloadPendingBookIds.Remove(queueItem.Id);
                    _ = PersistPendingKomgaDownloadRemovedAsync(queueItem.Id);
                    _downloadItemsByBookId.Remove(queueItem.Id);
                    ResetDownloadState(queueItem.Id);
                    queueItem.IsDownloading = false;
                    queueItem.IsCancelling = false;
                    queueItem.SpeedText = string.Empty;
                    queueItem.EtaText = string.Empty;
                    DownloadQueueItems.Remove(queueItem);
                    RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
                    ScheduleRefreshDownloadQueueState();
                    return;
                }
                catch (Exception ex)
                {
                    _recentFailedServerId = queueItem.ServerId;
                    if (attempt < MaxDownloadRetryCount && ex is not InvalidOperationException)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), token);
                        continue;
                    }

                    if (IsLikelyConnectivityIssue(ex))
                    {
                        queueItem.IsFailed = false;
                        queueItem.ErrorMessage = "Paused, waiting for connection...";
                        queueItem.IsDownloading = false;
                        queueItem.IsCancelling = false;
                        queueItem.IsQueued = true;
                        queueItem.SpeedText = string.Empty;
                        queueItem.EtaText = string.Empty;
                        SetDownloadState(queueItem.Id, trackedBook =>
                        {
                            trackedBook.IsQueued = true;
                            trackedBook.IsDownloading = false;
                            trackedBook.IsCancelling = false;
                        });
                        PauseQueueForConnectionLoss(queueItem.ServerId);
                        ScheduleRefreshDownloadQueueState();
                        return;
                    }

                    queueItem.IsFailed = true;
                    queueItem.ErrorMessage = ex.Message;
                    queueItem.IsDownloading = false;
                    queueItem.IsCancelling = false;
                    queueItem.IsQueued = false;
                    queueItem.SpeedText = string.Empty;
                    queueItem.EtaText = string.Empty;
                    SetDownloadState(queueItem.Id, trackedBook =>
                    {
                        trackedBook.IsQueued = false;
                        trackedBook.IsDownloading = false;
                        trackedBook.IsCancelling = false;
                        trackedBook.DownloadProgress = 0;
                    });
                    ErrorMessage = $"Failed to download '{queueItem.Name}' after {MaxDownloadRetryCount} attempts.";
                    System.Diagnostics.Debug.WriteLine($"Download error: {ex}");
                    RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
                    ScheduleRefreshDownloadQueueState();
                    return;
                }
            }
        }
        finally
        {
            if (_downloadCancellationTokens.Remove(queueItem.Id, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    [RelayCommand]
    private void CancelDownload(KomgaDownloadQueueItem? queueItem)
    {
        if (queueItem is null)
        {
            return;
        }

        var hasToken = _downloadCancellationTokens.TryGetValue(queueItem.Id, out var activeCts);
        if (queueItem.IsDownloading || hasToken)
        {
            queueItem.IsCancelling = true;
            queueItem.IsDownloading = false;
            SetDownloadState(queueItem.Id, trackedBook =>
            {
                trackedBook.IsCancelling = true;
                trackedBook.IsDownloading = false;
            });
            RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
            _cancelRequestedBookIds.Add(queueItem.Id);
            if (hasToken && activeCts is not null)
            {
                activeCts.Cancel();
            }
            ScheduleRefreshDownloadQueueState();
            return;
        }

        _downloadPendingBookIds.Remove(queueItem.Id);
        _ = PersistPendingKomgaDownloadRemovedAsync(queueItem.Id);
        _downloadItemsByBookId.Remove(queueItem.Id);
        _libraryService.CleanupPendingKomgaDownload(queueItem.BookDisplay.Book);
        ResetDownloadState(queueItem.Id);
        DownloadQueueItems.Remove(queueItem);
        RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
        ScheduleRefreshDownloadQueueState();
    }

    [RelayCommand]
    private void CancelAllDownloads()
    {
        var itemsToCancel = DownloadQueueItems.ToList();
        foreach (var queueItem in itemsToCancel)
        {
            var hasToken = _downloadCancellationTokens.TryGetValue(queueItem.Id, out var activeCts);
            if (queueItem.IsDownloading || hasToken)
            {
                queueItem.IsCancelling = true;
                queueItem.IsDownloading = false;
                SetDownloadState(queueItem.Id, trackedBook =>
                {
                    trackedBook.IsCancelling = true;
                    trackedBook.IsDownloading = false;
                });
                RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
                _cancelRequestedBookIds.Add(queueItem.Id);
                if (hasToken && activeCts is not null)
                {
                    activeCts.Cancel();
                }
            }
            else
            {
                _downloadPendingBookIds.Remove(queueItem.Id);
                _ = PersistPendingKomgaDownloadRemovedAsync(queueItem.Id);
                _downloadItemsByBookId.Remove(queueItem.Id);
                _libraryService.CleanupPendingKomgaDownload(queueItem.BookDisplay.Book);
                ResetDownloadState(queueItem.Id);
                DownloadQueueItems.Remove(queueItem);
                RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
            }
        }
        ScheduleRefreshDownloadQueueState();
    }

    [RelayCommand]
    private void MoveDownloadUp(KomgaDownloadQueueItem? queueItem)
    {
        if (queueItem is null || queueItem.IsDownloading || queueItem.IsCancelling)
        {
            return;
        }

        var index = DownloadQueueItems.IndexOf(queueItem);
        if (index <= 0)
        {
            return;
        }

        DownloadQueueItems.Move(index, index - 1);
        ScheduleRefreshDownloadQueueState();
    }

    [RelayCommand]
    private void MoveDownloadDown(KomgaDownloadQueueItem? queueItem)
    {
        if (queueItem is null || queueItem.IsDownloading || queueItem.IsCancelling)
        {
            return;
        }

        var index = DownloadQueueItems.IndexOf(queueItem);
        if (index < 0 || index >= DownloadQueueItems.Count - 1)
        {
            return;
        }

        DownloadQueueItems.Move(index, index + 1);
        ScheduleRefreshDownloadQueueState();
    }

    [RelayCommand]
    private void PauseAllDownloads()
    {
        if (IsDownloadQueuePaused)
        {
            return;
        }

        IsDownloadQueuePaused = true;
        foreach (var queueItem in DownloadQueueItems.Where(item => item.IsDownloading).ToList())
        {
            _pauseRequestedBookIds.Add(queueItem.Id);
            if (_downloadCancellationTokens.TryGetValue(queueItem.Id, out var cts))
            {
                cts.Cancel();
            }
        }
    }

    [RelayCommand]
    private void ResumeAllDownloads()
    {
        _offlineServerIds.Clear();
        IsDownloadQueuePaused = false;
        _lastDownloadConnectionProbeUtc = DateTime.MinValue;
        if (!_isProcessingQueue && DownloadQueueItems.Any(item => item.IsQueued))
        {
            _isProcessingQueue = true;
            _ = ProcessDownloadQueueAsync();
        }
    }

    [RelayCommand]
    private void RetryFailedDownloads()
    {
        foreach (var queueItem in DownloadQueueItems.Where(item => item.IsFailed).ToList())
        {
            RetryDownloadInternal(queueItem);
        }

        if (!_isProcessingQueue && DownloadQueueItems.Any(item => item.IsQueued))
        {
            _isProcessingQueue = true;
            _ = ProcessDownloadQueueAsync();
        }
    }

    [RelayCommand]
    private void RetryDownload(KomgaDownloadQueueItem? queueItem)
    {
        if (queueItem is null || !queueItem.IsFailed)
        {
            return;
        }

        RetryDownloadInternal(queueItem);

        if (!_isProcessingQueue && DownloadQueueItems.Any(item => item.IsQueued))
        {
            _isProcessingQueue = true;
            _ = ProcessDownloadQueueAsync();
        }
    }

    private void RetryDownloadInternal(KomgaDownloadQueueItem queueItem)
    {
        _recentFailedServerId = null;
        queueItem.IsFailed = false;
        queueItem.ErrorMessage = null;
        queueItem.IsQueued = true;
        queueItem.IsDownloading = false;
        queueItem.IsCancelling = false;
        queueItem.Progress = 0;
        SetDownloadState(queueItem.Id, trackedBook =>
        {
            trackedBook.IsQueued = true;
            trackedBook.IsDownloading = false;
            trackedBook.IsCancelling = false;
            trackedBook.DownloadProgress = 0;
        });
        RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
    }

    [RelayCommand]
    private void GoBackToLibraries()
    {
        SelectedSeries = null;
        SelectedLibrary = null;
        SelectedReadList = null;
        ClearAndReleaseThumbnails(Books);
        ClearAndReleaseThumbnails(Series);
        // ReadLists and smart lists are cached with 5-minute expiration and only refreshed via explicit refresh button.
        // Don't clear them here to avoid unnecessary API calls when navigating.
        _currentPage = 0;
        HasMoreSeries = true;
        HasMoreBooks = true;
        // Reset read list page counter (used for pagination) but preserve the cached list
        _currentReadListPage = 0;
        HasMoreReadLists = true;
    }

    [RelayCommand]
    private async Task GoBackToSeriesAsync()
    {
        // Stop the background loading of the remaining books, it would keep adding books and advancing _currentPage
        ResetLoadingCancellation();
        SelectedSeries = null;
        ClearAndReleaseThumbnails(Books);
        HasMoreBooks = true;
        // Restore series pagination position saved when we entered the books view
        _currentPage = _savedSeriesPage;
        
        // If the series list is empty (e.g. we jumped here directly from the reader),
        // reload it from the server so the user has something to navigate.
        if (Series.Count == 0 && SelectedLibrary != null)
        {
            // Nothing loaded: start at the first page, not at the saved position
            _currentPage = 0;
            HasMoreSeries = true;
            await LoadSeriesAsync();
        }
    }

    [RelayCommand]
    private void GoBackToReadLists()
    {
        SelectedReadList = null;
        ClearAndReleaseThumbnails(Books);
        _currentPage = 0;
        HasMoreBooks = true;
    }

    #region Read Lists

    [RelayCommand]
    private async Task LoadReadListsAsync(bool useCache = true)
    {
        await LoadReadListsInternalAsync(useCache, force: false);
    }

    private async Task LoadReadListsInternalAsync(bool useCache, bool force)
    {
        if (!_komgaApiService.IsConfigured || !HasMoreReadLists)
        {
            return;
        }

        if (!force && (!ReadListsSection.IsVisible || !ReadListsSection.IsExpanded))
        {
            return;
        }

        // Check cache if using cache and this is the first page
        if (_currentReadListPage == 0 && !ShouldRefreshCachedCollection(useCache, _readListsCacheTime, ReadLists.Count))
        {
            return;
        }

        try
        {
            IsBusy = true;
            
            var result = await _komgaApiService.GetReadListsAsync(
                page: _currentReadListPage,
                size: 20);

            var ct = _loadingCts?.Token ?? CancellationToken.None;
            var newDisplays = new List<KomgaReadListDisplay>();
            foreach (var rl in result.Content)
            {
                newDisplays.Add(CreateReadListDisplay(rl, null));
            }

            if (!ct.IsCancellationRequested)
            {
                HasMoreReadLists = !result.Last;
                _currentReadListPage++;
                
                // Update cache time on first page load
                if (_currentReadListPage == 1)
                {
                    _readListsCacheTime = DateTime.UtcNow;
                }

                foreach (var display in newDisplays)
                {
                    ReadLists.Add(display);
                }
                RefreshHomeSectionVisibilityState();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load read lists: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error loading read lists: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool ShouldRefreshCachedCollection(bool useCache, DateTime cacheTime, int itemCount)
    {
        return !useCache || itemCount == 0 || DateTime.UtcNow - cacheTime > CacheExpiration;
    }

    [RelayCommand]
    private async Task LoadMoreReadListsAsync()
    {
        await LoadReadListsAsync();
    }

    [RelayCommand]
    private async Task SelectReadListAsync(KomgaReadListDisplay? readListDisplay)
    {
        // Cancel any ongoing loading
        ResetLoadingCancellation();
        
        SelectedReadList = readListDisplay?.ReadList;
        SelectedLibrary = null;
        SelectedSeries = null;
        _currentPage = 0;
        HasMoreBooks = true;
        ClearAndReleaseThumbnails(Books);
        
        if (SelectedReadList is not null)
        {
            await LoadBooksForReadListAsync();
        }
    }

    [RelayCommand]
    private async Task LoadBooksForReadListAsync()
    {
        if (!_komgaApiService.IsConfigured || SelectedReadList is null || !HasMoreBooks)
        {
            return;
        }

        // Same as LoadSeriesAsync: capture the token before awaiting and don't request a page twice
        var ct = _loadingCts?.Token ?? CancellationToken.None;
        if (_readListBooksPageLoadingFor == ct)
        {
            return;
        }
        _readListBooksPageLoadingFor = ct;

        var isFirstPage = _currentPage == 0;
        try
        {
            if (isFirstPage)
            {
                IsBusy = true;
            }
            else
            {
                IsLoadingMore = true;
            }

            var result = await _komgaApiService.GetBooksForReadListAsync(
                SelectedReadList.Id,
                page: _currentPage,
                size: 20);

            var newDisplays = new List<KomgaBookDisplay>();
            foreach (var b in result.Content)
            {
                newDisplays.Add(CreateBookDisplay(b, null, isDownloaded: false));
            }

            if (!ct.IsCancellationRequested)
            {
                HasMoreBooks = !result.Last;
                _currentPage++;
                foreach (var display in newDisplays)
                {
                    Books.Add(display);
                }
                ApplyLocalSorting();
            }

            _ = Task.Run(() => LoadBooksDownloadStateAsync(newDisplays, ct), CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load books for read list: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error loading books for read list: {ex}");
        }
        finally
        {
            if (_readListBooksPageLoadingFor == ct)
            {
                _readListBooksPageLoadingFor = null;
            }

            if (isFirstPage)
            {
                IsBusy = false;
            }
            else
            {
                IsLoadingMore = false;
            }
        }
    }

    [RelayCommand]
    private async Task LoadMoreBooksForReadListAsync()
    {
        await LoadBooksForReadListAsync();
    }

    private sealed class KomgaPostDownloadWorkItem
    {
        public KomgaPostDownloadWorkItem(KomgaDownloadedFile downloadedFile, PendingImport pendingImport)
        {
            DownloadedFile = downloadedFile;
            PendingImport = pendingImport;
        }

        public KomgaDownloadedFile DownloadedFile { get; }

        public PendingImport PendingImport { get; }
    }

    #endregion
}
