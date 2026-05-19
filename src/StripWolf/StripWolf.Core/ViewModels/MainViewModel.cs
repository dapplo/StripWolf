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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Core.Models;
using StripWolf.Core.Services;

namespace StripWolf.Core.ViewModels;

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
    private readonly SettingsService _settingsService;
    private bool _isInitializing = true;

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isInReader;

    [ObservableProperty]
    private string _title;

    public MainViewModel(
        LibraryViewModel libraryViewModel,
        KomgaViewModel komgaViewModel,
        ActivityViewModel activityViewModel,
        SettingsViewModel settingsViewModel,
        ReaderViewModel readerViewModel,
        SettingsService settingsService)
    {
        _libraryViewModel = libraryViewModel;
        _komgaViewModel = komgaViewModel;
        _activityViewModel = activityViewModel;
        _settingsViewModel = settingsViewModel;
        _readerViewModel = readerViewModel;
        _settingsService = settingsService;
        
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

    public async Task InitializeAsync()
    {
        var settings = _settingsService.LoadSettings();

        if (settings.StartupBehavior == StartupBehavior.ContinueWhereLeftOff)
        {
            if (settings.WasInReader && settings.LastOpenedComicId.HasValue)
            {
                // Try to load the last opened comic
                // We set _isInitializing to false AFTER this so that IsInReader=true is saved correctly if needed,
                // although it should already be true in settings.
                await OpenReaderAsync(settings.LastOpenedComicId.Value);
            }
            else
            {
                SelectedTabIndex = settings.LastTabIndex;
            }
        }
        _isInitializing = false;
    }

    partial void OnIsInReaderChanged(bool value)
    {
        if (_isInitializing) return;

        // Save to settings
        var settings = _settingsService.LoadSettings();
        settings.WasInReader = value;
        _ = _settingsService.SaveSettingsAsync(settings);
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (_isInitializing) return;

        // Save to settings
        var settings = _settingsService.LoadSettings();
        settings.LastTabIndex = value;
        _ = _settingsService.SaveSettingsAsync(settings);

        if (!IsInReader)
        {
            CurrentView = GetViewForTab(value);
        }
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
        CurrentView = _readerViewModel;
        await _readerViewModel.LoadComicAsync(comicId);
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
