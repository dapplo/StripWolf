using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kom2go.ViewModels;

/// <summary>
/// Main view model that handles navigation between pages
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly LibraryViewModel _libraryViewModel;
    private readonly KomgaViewModel _komgaViewModel;
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
        SettingsViewModel settingsViewModel,
        ReaderViewModel readerViewModel)
    {
        _libraryViewModel = libraryViewModel;
        _komgaViewModel = komgaViewModel;
        _settingsViewModel = settingsViewModel;
        _readerViewModel = readerViewModel;
        
        Title = "Kom2go";
        _currentView = _libraryViewModel;

        // Subscribe to events
        _libraryViewModel.ComicOpenRequested += OnComicOpenRequested;
        _readerViewModel.CloseRequested += OnReaderCloseRequested;
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
    public SettingsViewModel SettingsViewModel => _settingsViewModel;
    public ReaderViewModel ReaderViewModel => _readerViewModel;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (!IsInReader)
        {
            CurrentView = value switch
            {
                0 => _libraryViewModel,
                1 => _komgaViewModel,
                2 => _settingsViewModel,
                _ => _libraryViewModel
            };
        }
    }

    [RelayCommand]
    private void NavigateToLibrary()
    {
        SelectedTabIndex = 0;
    }

    [RelayCommand]
    private void NavigateToKomga()
    {
        SelectedTabIndex = 1;
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        SelectedTabIndex = 2;
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
        CurrentView = _libraryViewModel;
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
