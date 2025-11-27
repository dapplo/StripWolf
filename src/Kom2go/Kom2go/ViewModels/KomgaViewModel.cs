using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kom2go.Data;
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
    private readonly DatabaseService _databaseService;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private ObservableCollection<KomgaLibrary> _libraries = [];

    [ObservableProperty]
    private ObservableCollection<KomgaSeries> _series = [];

    [ObservableProperty]
    private ObservableCollection<KomgaBook> _books = [];

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

    private int _currentPage;
    private bool _hasMoreSeries = true;
    private bool _hasMoreBooks = true;

    public KomgaViewModel(
        KomgaApiService komgaApiService,
        LibraryService libraryService,
        DatabaseService databaseService)
    {
        _komgaApiService = komgaApiService;
        _libraryService = libraryService;
        _databaseService = databaseService;
        Title = "Komga";
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await ExecuteAsync(async () =>
        {
            var server = await _databaseService.GetActiveServerAsync();
            if (server is not null)
            {
                _komgaApiService.Configure(server);
                IsConnected = await _komgaApiService.TestConnectionAsync();
                
                if (IsConnected)
                {
                    await LoadLibrariesAsync();
                }
            }
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await InitializeAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        if (!_komgaApiService.IsConfigured)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var libs = await _komgaApiService.GetLibrariesAsync();
            Libraries.Clear();
            foreach (var lib in libs)
            {
                Libraries.Add(lib);
            }
        });
    }

    [RelayCommand]
    private async Task SelectLibraryAsync(KomgaLibrary? library)
    {
        SelectedLibrary = library;
        SelectedSeries = null;
        _currentPage = 0;
        _hasMoreSeries = true;
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
        if (!_komgaApiService.IsConfigured || !_hasMoreSeries)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _komgaApiService.GetSeriesAsync(
                page: _currentPage,
                size: 20,
                libraryId: SelectedLibrary?.Id);

            foreach (var s in result.Content)
            {
                Series.Add(s);
            }

            _hasMoreSeries = !result.Last;
            _currentPage++;
        });
    }

    [RelayCommand]
    private async Task LoadMoreSeriesAsync()
    {
        await LoadSeriesAsync();
    }

    [RelayCommand]
    private async Task SelectSeriesAsync(KomgaSeries? series)
    {
        SelectedSeries = series;
        _currentPage = 0;
        _hasMoreBooks = true;
        Books.Clear();
        
        if (series is not null)
        {
            await LoadBooksAsync();
        }
    }

    [RelayCommand]
    private async Task LoadBooksAsync()
    {
        if (!_komgaApiService.IsConfigured || SelectedSeries is null || !_hasMoreBooks)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _komgaApiService.GetBooksForSeriesAsync(
                SelectedSeries.Id,
                page: _currentPage,
                size: 20);

            foreach (var b in result.Content)
            {
                Books.Add(b);
            }

            _hasMoreBooks = !result.Last;
            _currentPage++;
        });
    }

    [RelayCommand]
    private async Task LoadMoreBooksAsync()
    {
        await LoadBooksAsync();
    }

    [RelayCommand]
    private async Task DownloadBookAsync(KomgaBook? book)
    {
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
        Books.Clear();
        Series.Clear();
        _currentPage = 0;
        _hasMoreSeries = true;
    }

    [RelayCommand]
    private void GoBackToSeries()
    {
        SelectedSeries = null;
        Books.Clear();
        _currentPage = 0;
        _hasMoreBooks = true;
    }
}
