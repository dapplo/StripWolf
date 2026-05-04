using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Models;
using StripWolf.Models.Komga;
using StripWolf.Services;

namespace StripWolf.ViewModels;

/// <summary>
/// View model for browsing Komga content
/// </summary>
public partial class KomgaViewModel : ViewModelBase
{
    private readonly KomgaApiService _komgaApiService;
    private readonly LibraryService _libraryService;
    private readonly SettingsService _settingsService;
    
    private KomgaServer? _activeServer;
    private CancellationTokenSource? _loadingCts;
    
    // Cache timestamps for smart lists
    private DateTime _readListsCacheTime;
    private DateTime _keepReadingCacheTime;
    private DateTime _onDeckCacheTime;
    private DateTime _recentBooksCacheTime;
    private DateTime _recentSeriesCacheTime;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

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
    private CancellationTokenSource? _activeDownloadCts;
    private KomgaDownloadQueueItem? _activeDownloadItem;
    private bool _isProcessingQueue;

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
    
    [ObservableProperty]
    private bool _hasMoreSeries = true;
    
    [ObservableProperty]
    private bool _hasMoreBooks = true;
    
    private int _currentReadListPage;
    
    [ObservableProperty]
    private string? _selectedSeriesPrefix = "All";

    public List<string> AvailablePrefixes { get; } = 
        ["All", "0-9", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"];

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
        SelectedSeriesPrefix = prefix;

        if (SelectedLibrary != null)
        {
            _currentPage = 0;
            HasMoreSeries = true;
            Series.Clear();
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
        seriesDisplay.IsQueuedForDownload = matchingQueueItems.Any(item => item.IsQueued);
        seriesDisplay.IsDownloading = matchingQueueItems.Any(item => item.IsDownloading || item.IsCancelling);
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
        IsSelectedSeriesQueuedForDownload = matchingQueueItems.Any(item => item.IsQueued);
        IsSelectedSeriesDownloading = matchingQueueItems.Any(item => item.IsDownloading || item.IsCancelling);
    }

    private void RefreshDownloadQueueState()
    {
        QueuedDownloadsCount = DownloadQueueItems.Count;
        IsDownloadQueueActive = DownloadQueueItems.Count > 0;
        IsDownloading = _activeDownloadItem is not null;
    }

    private KomgaBookDisplay CreateBookDisplay(KomgaBook book, Bitmap? thumbnail, bool isDownloaded)
    {
        _downloadItemsByBookId.TryGetValue(book.Id, out var queueItem);
        return new KomgaBookDisplay
        {
            Book = book,
            Thumbnail = thumbnail,
            IsDownloaded = isDownloaded,
            IsQueued = queueItem?.IsQueued ?? _downloadPendingBookIds.Contains(book.Id),
            IsDownloading = queueItem?.IsDownloading ?? false,
            IsCancelling = queueItem?.IsCancelling ?? false,
            DownloadProgress = queueItem?.Progress ?? 0
        };
    }

    private KomgaSeriesDisplay CreateSeriesDisplay(KomgaSeries series, Bitmap? thumbnail)
    {
        var display = new KomgaSeriesDisplay
        {
            Series = series,
            Thumbnail = thumbnail
        };
        ApplySeriesDownloadState(display);
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
    /// Username for the active server (used for authenticated image loading)
    /// </summary>
    public string? ServerUsername => _activeServer?.Username;

    /// <summary>
    /// Password for the active server (used for authenticated image loading)
    /// </summary>
    public string? ServerPassword => _activeServer?.Password;

    /// <summary>
    /// Name of the active Komga server
    /// </summary>
    public string? ActiveServerName => _activeServer?.Name;

    public bool HasMultipleServers => ConfiguredServers.Count > 1;

    public KomgaViewModel(
        KomgaApiService komgaApiService,
        LibraryService libraryService,
        SettingsService settingsService)
    {
        _komgaApiService = komgaApiService;
        _libraryService = libraryService;
        _settingsService = settingsService;
        Title = "Komga";
        DownloadQueueItems.CollectionChanged += (_, _) => RefreshDownloadQueueState();
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
            SearchSeriesResults.Clear();
            SearchBookResults.Clear();
            return;
        }

        IsSearching = true;
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = new CancellationTokenSource();

        try
        {
            var ct = _loadingCts.Token;
            
            // Search series
            var seriesResults = await _komgaApiService.SearchSeriesAsync(SearchText, 0, 10);
            SearchSeriesResults.Clear();
            foreach (var s in seriesResults.Content)
            {
                if (ct.IsCancellationRequested) break;
                _ = LoadSearchSeriesThumbnailAsync(s, ct);
            }

            // Search books
            var bookResults = await _komgaApiService.SearchBooksAsync(SearchText, 0, 10);
            SearchBookResults.Clear();
            foreach (var b in bookResults.Content)
            {
                if (ct.IsCancellationRequested) break;
                _ = LoadSearchBookThumbnailAsync(b, ct);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Komga search failed: {ex.Message}");
        }
    }

    private async Task LoadSearchSeriesThumbnailAsync(KomgaSeries series, CancellationToken ct)
    {
        try
        {
            Bitmap? thumbnail = null;
            var thumbnailBytes = await _komgaApiService.GetSeriesThumbnailAsync(series.Id);
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0 && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream(thumbnailBytes);
                thumbnail = new Bitmap(stream);
            }
            
            if (ct.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    SearchSeriesResults.Add(CreateSeriesDisplay(series, thumbnail));
                }
                else
                {
                    thumbnail?.Dispose();
                }
            });
        }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        SearchSeriesResults.Add(CreateSeriesDisplay(series, null));
                    }
                });
            }
        }
    }

    private async Task<bool> CheckIfDownloadedAsync(KomgaBook book)
    {
        var comic = await _libraryService.GetComicByKomgaIdOrHashAsync(book.Id, book.FileHash);
        if (comic is null) return false;
        // If the comic has a server link, only consider it downloaded for the current server.
        // Comics without a server link (downloaded before this feature) are treated as matching
        // all servers to preserve backward compatibility.
        if (comic.KomgaServerId.HasValue && _activeServer is not null)
        {
            return comic.KomgaServerId.Value == _activeServer.Id;
        }
        return true;
    }

    private async Task LoadSearchBookThumbnailAsync(KomgaBook book, CancellationToken ct)
    {
        try
        {
            var isDownloaded = await CheckIfDownloadedAsync(book);
            Bitmap? thumbnail = null;
            var thumbnailBytes = await _komgaApiService.GetBookThumbnailAsync(book.Id);
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0 && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream(thumbnailBytes);
                thumbnail = new Bitmap(stream);
            }
            
            if (ct.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    SearchBookResults.Add(CreateBookDisplay(book, thumbnail, isDownloaded));
                }
                else
                {
                    thumbnail?.Dispose();
                }
            });
        }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                var isDownloaded = await CheckIfDownloadedAsync(book);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        SearchBookResults.Add(CreateBookDisplay(book, null, isDownloaded));
                    }
                });
            }
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        IsSearching = false;
        SearchSeriesResults.Clear();
        SearchBookResults.Clear();
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            var settings = _settingsService.LoadSettings();
            RefreshConfiguredServers(settings);

            var activeServer = settings.Servers.FirstOrDefault(s => s.Id == settings.ActiveServerId)
                               ?? settings.Servers.FirstOrDefault(s => s.IsActive);

            await ApplyServerAsync(activeServer, useCache: true, persistSelection: false);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to initialize: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error initializing Komga: {ex}");
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
        ReadLists.Clear();
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

        // Load Keep Reading books (in progress)
        if (!useCache || DateTime.UtcNow - _keepReadingCacheTime > CacheExpiration)
        {
            try
            {
                KeepReadingBooks.Clear();
                var keepReading = await _komgaApiService.GetBooksInProgressAsync(0, 10);
                HasKeepReading = keepReading.Content.Count > 0;
                
                foreach (var book in keepReading.Content)
                {
                    if (ct.IsCancellationRequested) break;
                    _ = LoadSmartListBookThumbnailAsync(book, KeepReadingBooks, ct);
                }
                _keepReadingCacheTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading keep reading: {ex.Message}");
            }
        }

        // Load On Deck books
        if (!useCache || DateTime.UtcNow - _onDeckCacheTime > CacheExpiration)
        {
            try
            {
                OnDeckBooks.Clear();
                var onDeck = await _komgaApiService.GetBooksOnDeckAsync(0, 10);
                HasOnDeck = onDeck.Content.Count > 0;
                
                foreach (var book in onDeck.Content)
                {
                    if (ct.IsCancellationRequested) break;
                    _ = LoadSmartListBookThumbnailAsync(book, OnDeckBooks, ct);
                }
                _onDeckCacheTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading on deck: {ex.Message}");
            }
        }

        // Load Recently Added Books
        if (!useCache || DateTime.UtcNow - _recentBooksCacheTime > CacheExpiration)
        {
            try
            {
                RecentlyAddedBooks.Clear();
                var recentBooks = await _komgaApiService.GetBooksLatestAsync(0, 10);
                HasRecentBooks = recentBooks.Content.Count > 0;
                
                foreach (var book in recentBooks.Content)
                {
                    if (ct.IsCancellationRequested) break;
                    _ = LoadSmartListBookThumbnailAsync(book, RecentlyAddedBooks, ct);
                }
                _recentBooksCacheTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recent books: {ex.Message}");
            }
        }

        // Load Recently Added Series
        if (!useCache || DateTime.UtcNow - _recentSeriesCacheTime > CacheExpiration)
        {
            try
            {
                RecentlyAddedSeries.Clear();
                var recentSeries = await _komgaApiService.GetSeriesLatestAsync(0, 10);
                HasRecentSeries = recentSeries.Content.Count > 0;
                
                foreach (var s in recentSeries.Content)
                {
                    if (ct.IsCancellationRequested) break;
                    _ = LoadSmartListSeriesThumbnailAsync(s, RecentlyAddedSeries, ct);
                }
                _recentSeriesCacheTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recent series: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Loads a book thumbnail for smart lists in the background
    /// </summary>
    private async Task LoadSmartListBookThumbnailAsync(KomgaBook book, ObservableCollection<KomgaBookDisplay> collection, CancellationToken ct)
    {
        try
        {
            var isDownloaded = await CheckIfDownloadedAsync(book);
            Bitmap? thumbnail = null;
            var thumbnailBytes = await _komgaApiService.GetBookThumbnailAsync(book.Id);
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0 && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream(thumbnailBytes);
                thumbnail = new Bitmap(stream);
            }
            
            if (ct.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    collection.Add(CreateBookDisplay(book, thumbnail, isDownloaded));
                }
                else
                {
                    thumbnail?.Dispose();
                }
            });
        }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                var isDownloaded = await CheckIfDownloadedAsync(book);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        collection.Add(CreateBookDisplay(book, null, isDownloaded));
                    }
                });
            }
        }
    }

    /// <summary>
    /// Loads a series thumbnail for smart lists in the background
    /// </summary>
    private async Task LoadSmartListSeriesThumbnailAsync(KomgaSeries series, ObservableCollection<KomgaSeriesDisplay> collection, CancellationToken ct)
    {
        try
        {
            Bitmap? thumbnail = null;
            var thumbnailBytes = await _komgaApiService.GetSeriesThumbnailAsync(series.Id);
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0 && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream(thumbnailBytes);
                thumbnail = new Bitmap(stream);
            }
            
            if (ct.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    collection.Add(CreateSeriesDisplay(series, thumbnail));
                }
                else
                {
                    thumbnail?.Dispose();
                }
            });
        }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        collection.Add(CreateSeriesDisplay(series, null));
                    }
                });
            }
        }
    }

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        if (!_komgaApiService.IsConfigured)
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
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load libraries: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error loading libraries: {ex}");
        }
    }

    public async Task LoadSeriesByIdAsync(string seriesId, int? serverId = null)
    {
        // Cancel any ongoing loading
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = new CancellationTokenSource();
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
            Series.Clear();
            Books.Clear();

            var series = await _komgaApiService.GetSeriesAsync(seriesId);
            if (series != null)
            {
                // Find and set the library
                if (Libraries.Count == 0) await LoadLibrariesAsync();
                SelectedLibrary = Libraries.FirstOrDefault(l => l.Id == series.LibraryId);
                
                await SelectSeriesAsync(CreateSeriesDisplay(series, null));
            }
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
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = new CancellationTokenSource();
        
        SelectedLibrary = library;
        SelectedSeries = null;
        _currentPage = 0;
        HasMoreSeries = true;
        Series.Clear();
        Books.Clear();
        
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

    private async Task ApplyServerAsync(KomgaServer? server, bool useCache, bool persistSelection)
    {
        _activeServer = server;
        UpdateSelectedServer(server);

        OnPropertyChanged(nameof(ServerUsername));
        OnPropertyChanged(nameof(ServerPassword));
        OnPropertyChanged(nameof(ActiveServerName));

        ResetNavigationState();
        ErrorMessage = string.Empty;

        if (server is null)
        {
            IsConnected = false;
            return;
        }

        if (persistSelection)
        {
            await PersistActiveServerSelectionAsync(server.Id);
            var settings = _settingsService.LoadSettings();
            RefreshConfiguredServers(settings);
            var refreshedServer = settings.Servers.FirstOrDefault(configuredServer => configuredServer.Id == server.Id);
            if (refreshedServer is not null)
            {
                _activeServer = refreshedServer;
                UpdateSelectedServer(refreshedServer);
            }
        }

        var activeServer = _activeServer ?? server;
        _activeServer = activeServer;
        _komgaApiService.Configure(activeServer);
        IsConnected = await _komgaApiService.TestConnectionAsync();

        if (!IsConnected)
        {
            return;
        }

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
        ConfiguredServers.Clear();
        foreach (var server in settings.Servers)
        {
            ConfiguredServers.Add(server);
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
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = new CancellationTokenSource();

        ClearSearch();
        SelectedInfoBookDisplay = null;
        BookPendingReadListSelection = null;
        SeriesPendingDownloadSelection = null;
        GoBackToLibraries();
        Libraries.Clear();
        ReadLists.Clear();
        KeepReadingBooks.Clear();
        OnDeckBooks.Clear();
        RecentlyAddedBooks.Clear();
        RecentlyAddedSeries.Clear();
        HasKeepReading = false;
        HasOnDeck = false;
        HasRecentBooks = false;
        HasRecentSeries = false;
    }

    private async Task PersistActiveServerSelectionAsync(int serverId)
    {
        var settings = _settingsService.LoadSettings();
        foreach (var server in settings.Servers)
        {
            server.IsActive = server.Id == serverId;
        }

        settings.ActiveServerId = serverId;
        await _settingsService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    private async Task LoadSeriesAsync()
    {
        if (!_komgaApiService.IsConfigured || !HasMoreSeries)
        {
            return;
        }

        try
        {
            IsBusy = true;
            
            var prefix = SelectedSeriesPrefix == "All" ? null : SelectedSeriesPrefix;
            
            var result = await _komgaApiService.GetSeriesAsync(
                page: _currentPage,
                size: 20,
                libraryId: SelectedLibrary?.Id,
                searchPrefix: prefix);

            HasMoreSeries = !result.Last;
            _currentPage++;
            
            // Load thumbnails in background and add items progressively
            var ct = _loadingCts?.Token ?? CancellationToken.None;
            foreach (var s in result.Content)
            {
                if (ct.IsCancellationRequested) break;
                
                // Start background thumbnail loading
                _ = LoadSeriesThumbnailAsync(s, ct);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load series: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error loading series: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Loads a series thumbnail in the background and adds it to the collection
    /// </summary>
    private async Task LoadSeriesThumbnailAsync(KomgaSeries series, CancellationToken ct)
    {
        try
        {
            Bitmap? thumbnail = null;
            
            // Try to load the thumbnail
            var thumbnailBytes = await _komgaApiService.GetSeriesThumbnailAsync(series.Id);
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0 && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream(thumbnailBytes);
                thumbnail = new Bitmap(stream);
            }
            
            if (ct.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            
            // Add to collection on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    Series.Add(CreateSeriesDisplay(series, thumbnail));
                }
                else
                {
                    thumbnail?.Dispose();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for series '{series.Name}': {ex.Message}");
            
            // Add without thumbnail
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Series.Add(CreateSeriesDisplay(series, null));
                    }
                });
            }
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
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = new CancellationTokenSource();

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
        _currentPage = 0;
        HasMoreBooks = true;
        Books.Clear();
        
        // Clear search state when navigating to a series
        if (IsSearching)
        {
            IsSearching = false;
            SearchText = string.Empty;
            SearchSeriesResults.Clear();
            SearchBookResults.Clear();
        }
        
        if (SelectedSeries is not null)
        {
            await LoadBooksAsync();
        }
    }

    [RelayCommand]
    private void DownloadSelectedSeries()
    {
        if (SelectedSeries is null || IsSelectedSeriesQueuedForDownload || IsSelectedSeriesDownloading)
        {
            return;
        }

        SeriesPendingDownloadSelection = CreateSeriesDisplay(SelectedSeries, null);
    }

    [RelayCommand]
    private async Task LoadBooksAsync()
    {
        if (!_komgaApiService.IsConfigured || SelectedSeries is null || !HasMoreBooks)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await _komgaApiService.GetBooksForSeriesAsync(
                SelectedSeries.Id,
                page: _currentPage,
                size: 20);

            HasMoreBooks = !result.Last;
            _currentPage++;
            
            // Load thumbnails in background and add items progressively
            var ct = _loadingCts?.Token ?? CancellationToken.None;
            foreach (var b in result.Content)
            {
                if (ct.IsCancellationRequested) break;
                
                // Start background thumbnail loading
                _ = LoadBookThumbnailAsync(b, ct);
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
    /// Loads a book thumbnail in the background and adds it to the collection
    /// </summary>
    private async Task LoadBookThumbnailAsync(KomgaBook book, CancellationToken ct)
    {
        try
        {
            var isDownloaded = await CheckIfDownloadedAsync(book);
            Bitmap? thumbnail = null;
            
            // Try to load the thumbnail
            var thumbnailBytes = await _komgaApiService.GetBookThumbnailAsync(book.Id);
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0 && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream(thumbnailBytes);
                thumbnail = new Bitmap(stream);
            }
            
            if (ct.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            
            // Add to collection on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    Books.Add(CreateBookDisplay(book, thumbnail, isDownloaded));
                    ApplyLocalSorting();
                }
                else
                {
                    thumbnail?.Dispose();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for book '{book.Name}': {ex.Message}");
            
            // Add without thumbnail
            if (!ct.IsCancellationRequested)
            {
                var isDownloaded = await CheckIfDownloadedAsync(book);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Books.Add(CreateBookDisplay(book, null, isDownloaded));
                        ApplyLocalSorting();
                    }
                });
            }
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
    private void DownloadSeries(KomgaSeriesDisplay? seriesDisplay)
    {
        if (seriesDisplay?.Series is null || seriesDisplay.IsDownloading || seriesDisplay.IsQueuedForDownload)
        {
            return;
        }

        SeriesPendingDownloadSelection = seriesDisplay;
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
            var success = await _komgaApiService.MarkBookAsReadAsync(bookDisplay.Id);
            if (!success)
            {
                throw new InvalidOperationException($"Komga rejected marking '{bookDisplay.Name}' as read.");
            }

            UpdateTrackedBookDisplays(bookDisplay.Id, trackedBook =>
            {
                trackedBook.Book.ReadProgress = CreateCompletedReadProgress(trackedBook);
                trackedBook.RefreshComputedProperties();
            });
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

    private void QueueBookDownload(KomgaBookDisplay bookDisplay)
    {
        if (!_downloadPendingBookIds.Add(bookDisplay.Id))
        {
            return;
        }

        var queueItem = new KomgaDownloadQueueItem
        {
            BookDisplay = bookDisplay,
            IsQueued = true,
            Progress = 0
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

    private async Task ProcessDownloadQueueAsync()
    {
        try
        {
            while (true)
            {
                var queueItem = DownloadQueueItems.FirstOrDefault(item => item.IsQueued && !item.IsCancelling);
                if (queueItem is null)
                {
                    break;
                }

                _activeDownloadItem = queueItem;
                _activeDownloadCts?.Dispose();
                _activeDownloadCts = new CancellationTokenSource();

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
                queueItem.Progress = 0;

                DownloadingBookName = queueItem.Name;
                DownloadProgress = 0;
                RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
                RefreshDownloadQueueState();

                try
                {
                    var progress = new Progress<double>(p =>
                    {
                        DownloadProgress = p;
                        queueItem.Progress = p;
                        SetDownloadState(queueItem.Id, trackedBook => trackedBook.DownloadProgress = p);
                    });

                    await _libraryService.DownloadFromKomgaAsync(queueItem.BookDisplay.Book, _activeServer?.Id, progress, _activeDownloadCts.Token);
                    SetDownloadState(queueItem.Id, trackedBook =>
                    {
                        trackedBook.IsDownloaded = true;
                        trackedBook.DownloadProgress = 1.0;
                    });
                    queueItem.BookDisplay.IsDownloaded = true;
                    queueItem.Progress = 1.0;
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"Download cancelled for '{queueItem.Name}'.");
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Failed to download '{queueItem.Name}': {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"Download error: {ex}");
                }
                finally
                {
                    _downloadPendingBookIds.Remove(queueItem.Id);
                    _downloadItemsByBookId.Remove(queueItem.Id);
                    ResetDownloadState(queueItem.Id);
                    queueItem.IsDownloading = false;
                    queueItem.IsCancelling = false;
                    DownloadQueueItems.Remove(queueItem);
                    DownloadingBookName = string.Empty;
                    DownloadProgress = 0;
                    RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
                    _activeDownloadItem = null;
                    _activeDownloadCts?.Dispose();
                    _activeDownloadCts = null;
                }

                await Task.Delay(100);
            }
        }
        finally
        {
            _activeDownloadItem = null;
            _activeDownloadCts?.Dispose();
            _activeDownloadCts = null;
            _isProcessingQueue = false;
            DownloadingBookName = string.Empty;
            DownloadProgress = 0;
            RefreshDownloadQueueState();
        }
    }

    [RelayCommand]
    private void CancelDownload(KomgaDownloadQueueItem? queueItem)
    {
        if (queueItem is null)
        {
            return;
        }

        if (ReferenceEquals(queueItem, _activeDownloadItem) || queueItem.IsDownloading)
        {
            queueItem.IsCancelling = true;
            queueItem.IsDownloading = false;
            SetDownloadState(queueItem.Id, trackedBook =>
            {
                trackedBook.IsCancelling = true;
                trackedBook.IsDownloading = false;
            });
            RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
            _activeDownloadCts?.Cancel();
            return;
        }

        _downloadPendingBookIds.Remove(queueItem.Id);
        _downloadItemsByBookId.Remove(queueItem.Id);
        ResetDownloadState(queueItem.Id);
        DownloadQueueItems.Remove(queueItem);
        RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
    }

    [RelayCommand]
    private void CancelAllDownloads()
    {
        foreach (var queueItem in DownloadQueueItems.Where(item => !ReferenceEquals(item, _activeDownloadItem) && !item.IsDownloading).ToList())
        {
            _downloadPendingBookIds.Remove(queueItem.Id);
            _downloadItemsByBookId.Remove(queueItem.Id);
            ResetDownloadState(queueItem.Id);
            DownloadQueueItems.Remove(queueItem);
            RefreshSeriesDownloadState(queueItem.BookDisplay.Book.SeriesId);
        }

        if (_activeDownloadItem is not null)
        {
            _activeDownloadItem.IsCancelling = true;
            _activeDownloadItem.IsDownloading = false;
            SetDownloadState(_activeDownloadItem.Id, trackedBook =>
            {
                trackedBook.IsCancelling = true;
                trackedBook.IsDownloading = false;
            });
            RefreshSeriesDownloadState(_activeDownloadItem.BookDisplay.Book.SeriesId);
            _activeDownloadCts?.Cancel();
        }
    }

    [RelayCommand]
    private void GoBackToLibraries()
    {
        SelectedSeries = null;
        SelectedLibrary = null;
        SelectedReadList = null;
        Books.Clear();
        Series.Clear();
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
        SelectedSeries = null;
        Books.Clear();
        _currentPage = 0;
        HasMoreSeries = true;
        Series.Clear();
        
        if (SelectedLibrary != null)
        {
            await LoadSeriesAsync();
        }
    }

    [RelayCommand]
    private void GoBackToReadLists()
    {
        SelectedReadList = null;
        Books.Clear();
        _currentPage = 0;
        HasMoreBooks = true;
    }

    #region Read Lists

    [RelayCommand]
    private async Task LoadReadListsAsync(bool useCache = true)
    {
        if (!_komgaApiService.IsConfigured || !HasMoreReadLists)
        {
            return;
        }

        // Check cache if using cache and this is the first page
        if (useCache && _currentReadListPage == 0 && ReadLists.Count > 0 && 
            DateTime.UtcNow - _readListsCacheTime < CacheExpiration)
        {
            return;
        }

        try
        {
            IsBusy = true;
            
            var result = await _komgaApiService.GetReadListsAsync(
                page: _currentReadListPage,
                size: 20);

            HasMoreReadLists = !result.Last;
            _currentReadListPage++;
            
            // Update cache time on first page load
            if (_currentReadListPage == 1)
            {
                _readListsCacheTime = DateTime.UtcNow;
            }
            
            // Load thumbnails in background and add items progressively
            var ct = _loadingCts?.Token ?? CancellationToken.None;
            foreach (var rl in result.Content)
            {
                if (ct.IsCancellationRequested) break;
                
                // Start background thumbnail loading
                _ = LoadReadListThumbnailAsync(rl, ct);
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

    /// <summary>
    /// Loads a read list thumbnail in the background and adds it to the collection
    /// </summary>
    private async Task LoadReadListThumbnailAsync(KomgaReadList readList, CancellationToken ct)
    {
        try
        {
            Bitmap? thumbnail = null;
            
            // Try to load the thumbnail
            var thumbnailBytes = await _komgaApiService.GetReadListThumbnailAsync(readList.Id);
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0 && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream(thumbnailBytes);
                thumbnail = new Bitmap(stream);
            }
            
            if (ct.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            
            // Add to collection on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    ReadLists.Add(new KomgaReadListDisplay
                    {
                        ReadList = readList,
                        Thumbnail = thumbnail
                    });
                }
                else
                {
                    thumbnail?.Dispose();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for read list '{readList.Name}': {ex.Message}");
            
            // Add without thumbnail
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        ReadLists.Add(new KomgaReadListDisplay
                        {
                            ReadList = readList,
                            Thumbnail = null
                        });
                    }
                });
            }
        }
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
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = new CancellationTokenSource();
        
        SelectedReadList = readListDisplay?.ReadList;
        SelectedLibrary = null;
        SelectedSeries = null;
        _currentPage = 0;
        HasMoreBooks = true;
        Books.Clear();
        
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

        try
        {
            IsBusy = true;
            var result = await _komgaApiService.GetBooksForReadListAsync(
                SelectedReadList.Id,
                page: _currentPage,
                size: 20);

            HasMoreBooks = !result.Last;
            _currentPage++;
            
            // Load thumbnails in background and add items progressively
            var ct = _loadingCts?.Token ?? CancellationToken.None;
            foreach (var b in result.Content)
            {
                if (ct.IsCancellationRequested) break;
                
                // Start background thumbnail loading
                _ = LoadBookThumbnailAsync(b, ct);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load books for read list: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error loading books for read list: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreBooksForReadListAsync()
    {
        await LoadBooksForReadListAsync();
    }

    #endregion
}
