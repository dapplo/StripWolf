using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kom2go.Models;
using Kom2go.Models.Komga;
using Kom2go.Services;

namespace Kom2go.ViewModels;

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
    private KomgaBook? _selectedBook;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _downloadingBookName = string.Empty;

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
    private bool _hasMoreReadLists = true;

    private int _currentPage;
    
    [ObservableProperty]
    private bool _hasMoreSeries = true;
    
    [ObservableProperty]
    private bool _hasMoreBooks = true;
    
    private int _currentReadListPage;
    
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

    public KomgaViewModel(
        KomgaApiService komgaApiService,
        LibraryService libraryService,
        SettingsService settingsService)
    {
        _komgaApiService = komgaApiService;
        _libraryService = libraryService;
        _settingsService = settingsService;
        Title = "Komga";
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchAsync();
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
                    SearchSeriesResults.Add(new KomgaSeriesDisplay { Series = series, Thumbnail = thumbnail });
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
                        SearchSeriesResults.Add(new KomgaSeriesDisplay { Series = series, Thumbnail = null });
                    }
                });
            }
        }
    }

    private async Task LoadSearchBookThumbnailAsync(KomgaBook book, CancellationToken ct)
    {
        try
        {
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
                    SearchBookResults.Add(new KomgaBookDisplay { Book = book, Thumbnail = thumbnail });
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
                        SearchBookResults.Add(new KomgaBookDisplay { Book = book, Thumbnail = null });
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
            var settings = await _settingsService.LoadSettingsAsync();
            _activeServer = settings.Servers.FirstOrDefault(s => s.IsActive);
            
            OnPropertyChanged(nameof(ServerUsername));
            OnPropertyChanged(nameof(ServerPassword));
            OnPropertyChanged(nameof(ActiveServerName));
            
            if (_activeServer is not null)
            {
                _komgaApiService.Configure(_activeServer);
                IsConnected = await _komgaApiService.TestConnectionAsync();
                
                if (IsConnected)
                {
                    await LoadLibrariesAsync();
                    await LoadReadListsAsync(useCache: true);
                    await LoadSmartListsAsync(useCache: true);
                }
            }
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
        // Force refresh by clearing cache
        _readListsCacheTime = DateTime.MinValue;
        _keepReadingCacheTime = DateTime.MinValue;
        _onDeckCacheTime = DateTime.MinValue;
        _recentBooksCacheTime = DateTime.MinValue;
        _recentSeriesCacheTime = DateTime.MinValue;
        
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
                    collection.Add(new KomgaBookDisplay { Book = book, Thumbnail = thumbnail });
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
                        collection.Add(new KomgaBookDisplay { Book = book, Thumbnail = null });
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
                    collection.Add(new KomgaSeriesDisplay { Series = series, Thumbnail = thumbnail });
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
                        collection.Add(new KomgaSeriesDisplay { Series = series, Thumbnail = null });
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
    private async Task LoadSeriesAsync()
    {
        if (!_komgaApiService.IsConfigured || !HasMoreSeries)
        {
            return;
        }

        try
        {
            IsBusy = true;
            
            var result = await _komgaApiService.GetSeriesAsync(
                page: _currentPage,
                size: 20,
                libraryId: SelectedLibrary?.Id);

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
                    Series.Add(new KomgaSeriesDisplay
                    {
                        Series = series,
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
            System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for series '{series.Name}': {ex.Message}");
            
            // Add without thumbnail
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Series.Add(new KomgaSeriesDisplay
                        {
                            Series = series,
                            Thumbnail = null
                        });
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

    /// <summary>
    /// Loads a book thumbnail in the background and adds it to the collection
    /// </summary>
    private async Task LoadBookThumbnailAsync(KomgaBook book, CancellationToken ct)
    {
        try
        {
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
                    Books.Add(new KomgaBookDisplay
                    {
                        Book = book,
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
            System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for book '{book.Name}': {ex.Message}");
            
            // Add without thumbnail
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Books.Add(new KomgaBookDisplay
                        {
                            Book = book,
                            Thumbnail = null
                        });
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
        var book = bookDisplay?.Book;
        if (book is null || IsDownloading)
        {
            return;
        }

        IsDownloading = true;
        DownloadingBookName = book.Name;
        DownloadProgress = 0;

        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            await _libraryService.DownloadFromKomgaAsync(book, progress);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to download '{book.Name}': {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            DownloadingBookName = string.Empty;
            DownloadProgress = 0;
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
        // Don't clear ReadLists - they are cached
        _currentPage = 0;
        HasMoreSeries = true;
        HasMoreBooks = true;
        // Reset read list page but don't clear cache
        _currentReadListPage = 0;
        HasMoreReadLists = true;
    }

    [RelayCommand]
    private void GoBackToSeries()
    {
        SelectedSeries = null;
        Books.Clear();
        _currentPage = 0;
        HasMoreBooks = true;
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
