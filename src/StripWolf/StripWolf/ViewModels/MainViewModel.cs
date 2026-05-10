using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Models;

namespace StripWolf.ViewModels;

/// <summary>
/// Main view model that handles navigation between pages
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly LibraryViewModel _libraryViewModel;
    private readonly KomgaViewModel _komgaViewModel;
    private readonly ActivityViewModel _activityViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ReaderViewModel _readerViewModel;

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isInReader;

    public MainViewModel(
        LibraryViewModel libraryViewModel,
        KomgaViewModel komgaViewModel,
        ActivityViewModel activityViewModel,
        SettingsViewModel settingsViewModel,
        ReaderViewModel readerViewModel)
    {
        _libraryViewModel = libraryViewModel;
        _komgaViewModel = komgaViewModel;
        _activityViewModel = activityViewModel;
        _settingsViewModel = settingsViewModel;
        _readerViewModel = readerViewModel;
        
        Title = "StripWolf";
        _currentView = _libraryViewModel;

        // Subscribe to events
        _libraryViewModel.ComicOpenRequested += OnComicOpenRequested;
        _libraryViewModel.ViewKomgaSeriesRequested += OnViewKomgaSeriesRequested;
        _readerViewModel.CloseRequested += OnReaderCloseRequested;
        _readerViewModel.ComicOpenRequested += OnComicOpenRequested;
        _readerViewModel.ViewSeriesRequested += OnViewKomgaSeriesRequested;
        _activityViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivityViewModel.ActiveItemsCount))
            {
                OnPropertyChanged(nameof(ActivityItemsCount));
                OnPropertyChanged(nameof(HasActivityItems));
            }
        };
    }

    private async void OnViewKomgaSeriesRequested(object? sender, KomgaSeriesNavigationRequest request)
    {
        if (sender == _readerViewModel || IsInReader)
        {
            IsInReader = false;
        }

        ActivateTab(1);
        await _komgaViewModel.LoadSeriesByIdAsync(request.SeriesId, request.ServerId);
    }

    private async void OnComicOpenRequested(object? sender, int comicId)
    {
        await OpenReaderAsync(comicId);
    }

    private void OnReaderCloseRequested(object? sender, EventArgs e)
    {
        CloseReader();
    }

    public LibraryViewModel LibraryViewModel => _libraryViewModel;
    public KomgaViewModel KomgaViewModel => _komgaViewModel;
    public ActivityViewModel ActivityViewModel => _activityViewModel;
    public SettingsViewModel SettingsViewModel => _settingsViewModel;
    public ReaderViewModel ReaderViewModel => _readerViewModel;
    public int ActivityItemsCount => _activityViewModel.ActiveItemsCount;
    public bool HasActivityItems => ActivityItemsCount > 0;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (!IsInReader)
        {
            CurrentView = GetViewForTab(value);
        }
    }

    private ViewModelBase GetViewForTab(int tabIndex)
    {
        return tabIndex switch
        {
            0 => _libraryViewModel,
            1 => _komgaViewModel,
            2 => _activityViewModel,
            3 => _settingsViewModel,
            _ => _libraryViewModel
        };
    }

    private void ActivateTab(int tabIndex)
    {
        SelectedTabIndex = tabIndex;

        if (!IsInReader)
        {
            CurrentView = GetViewForTab(tabIndex);
        }
    }

    [RelayCommand]
    private void NavigateToLibrary()
    {
        ActivateTab(0);
    }

    [RelayCommand]
    private void NavigateToKomga()
    {
        ActivateTab(1);
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        ActivateTab(3);
    }

    [RelayCommand]
    private void NavigateToActivity()
    {
        ActivateTab(2);
    }

    [RelayCommand]
    private async Task OpenReaderAsync(int comicId)
    {
        IsInReader = true;
        await _readerViewModel.LoadComicAsync(comicId);
        CurrentView = _readerViewModel;
    }

    [RelayCommand]
    private void CloseReader()
    {
        IsInReader = false;
        ActivateTab(0);
        // Refresh the library to show updated progress
        _ = _libraryViewModel.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Performs cleanup operations when the application is shutting down.
    /// This includes permanently deleting any comics that are pending deletion.
    /// </summary>
    public async Task OnShutdownAsync()
    {
        await _libraryViewModel.DeleteAllPendingComicsAsync();
    }
}
