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

using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using System.Diagnostics;

namespace StripWolf.Core.ViewModels;

public record WelcomeThemeOption(AppThemePreference Value, string DisplayName);

/// <summary>
/// Main view model that handles navigation between pages
/// </summary>
public partial class MainViewModel : ViewModelBase
{
#if PLAY_STORE_BUILD
    private const int WelcomeExperienceStepCount = 6;
#else
    private const int WelcomeExperienceStepCount = 7;
#endif
    private readonly LibraryViewModel _libraryViewModel;
    private readonly KomgaViewModel _komgaViewModel;
    private readonly ActivityViewModel _activityViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ReaderViewModel _readerViewModel;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localizationService;
    private readonly IExternalLinkService _externalLinkService;
    private readonly KomgaSyncService _komgaSyncService;
    private readonly UpdateService _updateService;
    private readonly TrialService _trialService;
    private bool _isInitializing = true;
    private bool _isApplyingWelcomePreferences;
    private bool _shouldStartWelcomeAfterInitialization;
    private bool _hasWelcomeStartState;
    private bool _isRestoringWelcomeState;
    private bool _welcomeStartedInReader;
    private int _welcomeStartTabIndex;
    private IReadOnlyList<WelcomeThemeOption>? _welcomeThemeOptions;

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isInReader;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _showUpdateNotification;

    [ObservableProperty]
    private string _updateNotificationMessage = string.Empty;

    [ObservableProperty]
    private bool _showWelcomeExperience;

    [ObservableProperty]
    private bool _showPremiumUnlockDialog;

    [ObservableProperty]
    private int _welcomeExperienceStep;

    [ObservableProperty]
    private bool _showWelcomeLicenseDialog;

    [ObservableProperty]
    private bool _hasAcceptedWelcomeLicense;

    [ObservableProperty]
    private LanguageOption _welcomeSelectedLanguage = LocalizationService.AvailableLanguages[0];

    [ObservableProperty]
    private WelcomeThemeOption? _welcomeSelectedThemeOption;

    public MainViewModel(
        LibraryViewModel libraryViewModel,
        KomgaViewModel komgaViewModel,
        ActivityViewModel activityViewModel,
        SettingsViewModel settingsViewModel,
        ReaderViewModel readerViewModel,
        SettingsService settingsService,
        LocalizationService localizationService,
        IExternalLinkService externalLinkService,
        KomgaSyncService komgaSyncService,
        UpdateService updateService,
        TrialService trialService)
    {
        _libraryViewModel = libraryViewModel;
        _komgaViewModel = komgaViewModel;
        _activityViewModel = activityViewModel;
        _settingsViewModel = settingsViewModel;
        _readerViewModel = readerViewModel;
        _settingsService = settingsService;
        _localizationService = localizationService;
        _externalLinkService = externalLinkService;
        _komgaSyncService = komgaSyncService;
        _updateService = updateService;
        _trialService = trialService;
        
        _trialService.PremiumUnlockRequested += (s, e) => ShowPremiumUnlockDialog = true;
        
        Title = "StripWolf";
        _currentView = _libraryViewModel;

        // Subscribe to events
        _libraryViewModel.ComicOpenRequested += OnComicOpenRequested;
        _libraryViewModel.ViewKomgaSeriesRequested += OnViewKomgaSeriesRequested;
        _komgaViewModel.ConfigureServerRequested += OnConfigureKomgaServerRequested;
        _readerViewModel.CloseRequested += OnReaderCloseRequested;
        _readerViewModel.ComicOpenRequested += OnComicOpenRequested;
        _readerViewModel.ViewSeriesRequested += OnViewKomgaSeriesRequested;
        
        _updateService.NewVersionDetected += (sender, version) =>
        {
            UpdateNotificationMessage = string.Format(Resources.Loc.Instance.UpdatePopupMessage, version);
            ShowUpdateNotification = true;
        };

        _activityViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivityViewModel.ActiveItemsCount))
            {
                OnPropertyChanged(nameof(ActivityItemsCount));
                OnPropertyChanged(nameof(HasActivityItems));
            }
        };
        _localizationService.LanguageChanged += OnLanguageChanged;

        if (_settingsService.LoadSettings().HasCompletedWelcomeExperience is not true)
        {
            StartWelcomeExperience();
        }
    }

    /// <summary>
    /// Called when the application is resumed/activated
    /// </summary>
    public async Task OnAppResumedAsync()
    {
        // Sync all comics in background
        _ = _komgaSyncService.SyncAllComicsAsync();

        // If a comic is currently being read, sync and refresh its state
        if (IsInReader)
        {
            await _readerViewModel.SyncAndRefreshProgressAsync();
        }
    }

    public async Task InitializeAsync()
    {
        var settings = _settingsService.LoadSettings();

        try
        {
            if (settings.StartupBehavior == StartupBehavior.ContinueWhereLeftOff)
            {
                if (settings.WasInReader && settings.LastOpenedComicId.HasValue)
                {
                    // Try to load the last opened comic
                    await OpenReaderAsync(settings.LastOpenedComicId.Value);

                    // After opening, sync with Komga to jump to newer page if needed
                    await _readerViewModel.SyncAndRefreshProgressAsync();
                }
                else
                {
                    SelectedTabIndex = settings.LastTabIndex;
                }
            }
        }
        catch
        {
            // If restoring the previous state fails, continue app startup on the library tab.
            IsInReader = false;
            SelectedTabIndex = 0;
            CurrentView = _libraryViewModel;
        }
        finally
        {
            _isInitializing = false;
        }

        if (settings.HasCompletedWelcomeExperience is not true)
        {
            StartWelcomeExperience();
        }
        else if (_shouldStartWelcomeAfterInitialization)
        {
            _shouldStartWelcomeAfterInitialization = false;
            StartWelcomeExperience();
        }

        // Run check for updates in background
        _ = Task.Run(() => _updateService.CheckForUpdatesIfNeededAsync());
    }

    partial void OnWelcomeExperienceStepChanged(int value)
    {
        RefreshWelcomeBindings();
    }

    partial void OnShowWelcomeExperienceChanged(bool value)
    {
        RefreshWelcomeBindings();
    }

    partial void OnHasAcceptedWelcomeLicenseChanged(bool value)
    {
        OnPropertyChanged(nameof(CanContinueWelcomeStep));
        OnPropertyChanged(nameof(CanSkipWelcome));
    }

    partial void OnWelcomeSelectedLanguageChanged(LanguageOption value)
    {
        if (_isApplyingWelcomePreferences)
        {
            return;
        }

        _ = ApplyWelcomeLanguageAsync(value);
    }

    partial void OnWelcomeSelectedThemeOptionChanged(WelcomeThemeOption? value)
    {
        if (_isApplyingWelcomePreferences || value is null)
        {
            return;
        }

        ApplyTheme(value.Value);
        _ = _settingsService.UpdateSettingsAsync(settings => settings.AppTheme = value.Value);
    }

    private void RefreshWelcomeBindings()
    {
        OnPropertyChanged(nameof(IsWelcomeFirstStep));
        OnPropertyChanged(nameof(IsWelcomeLastStep));
        OnPropertyChanged(nameof(CanContinueWelcomeStep));
        OnPropertyChanged(nameof(CanSkipWelcome));
        OnPropertyChanged(nameof(IsWelcomeLibraryStep));
        OnPropertyChanged(nameof(IsWelcomeLibraryImportStep));
        OnPropertyChanged(nameof(IsWelcomeKomgaStep));
        OnPropertyChanged(nameof(IsWelcomeActivityStep));
        OnPropertyChanged(nameof(IsWelcomeSettingsStep));
        OnPropertyChanged(nameof(IsWelcomeSupportStep));
        OnPropertyChanged(nameof(HasWelcomeExperienceTargetHint));
        OnPropertyChanged(nameof(WelcomeBackgroundContentOpacity));
        OnPropertyChanged(nameof(WelcomeExperienceProgress));
        OnPropertyChanged(nameof(WelcomeExperienceNextButtonLabel));
        OnPropertyChanged(nameof(WelcomeExperienceStepTitle));
        OnPropertyChanged(nameof(WelcomeExperienceStepDescription));
        OnPropertyChanged(nameof(WelcomeExperienceTargetHint));
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

        if (!IsInReader && !_isRestoringWelcomeState)
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

    private void OnConfigureKomgaServerRequested(object? sender, EventArgs e)
    {
        ActivateTab(3);
        _settingsViewModel.AddServerCommand.Execute(null);
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
    public bool IsWelcomeFirstStep => WelcomeExperienceStep == 0;
    public bool IsWelcomeLastStep => WelcomeExperienceStep >= WelcomeExperienceStepCount - 1;
    public bool CanContinueWelcomeStep => !IsWelcomeFirstStep || HasAcceptedWelcomeLicense;
    public bool CanSkipWelcome => !IsWelcomeFirstStep || HasAcceptedWelcomeLicense;
    public IReadOnlyList<LanguageOption> WelcomeAvailableLanguages => LocalizationService.AvailableLanguages;
    public IReadOnlyList<WelcomeThemeOption> WelcomeThemeOptions => _welcomeThemeOptions ??=
    [
        new WelcomeThemeOption(AppThemePreference.System, Resources.Loc.Instance.ThemeSystem),
        new WelcomeThemeOption(AppThemePreference.Light, Resources.Loc.Instance.ThemeLight),
        new WelcomeThemeOption(AppThemePreference.Dark, Resources.Loc.Instance.ThemeDark)
    ];
    public string WelcomeExperienceProgress => string.Format(Resources.Loc.Instance.WelcomeExperienceProgress, WelcomeExperienceStep + 1, WelcomeExperienceStepCount);
    public string WelcomeExperienceNextButtonLabel => IsWelcomeLastStep ? Resources.Loc.Instance.WelcomeExperienceFinish : Resources.Loc.Instance.WelcomeExperienceNext;
    public bool IsWelcomeLibraryStep => ShowWelcomeExperience && (WelcomeExperienceStep == 1 || WelcomeExperienceStep == 2);
    public bool IsWelcomeLibraryImportStep => ShowWelcomeExperience && WelcomeExperienceStep == 2;
    public bool IsWelcomeKomgaStep => ShowWelcomeExperience && WelcomeExperienceStep == 3;
    public bool IsWelcomeActivityStep => ShowWelcomeExperience && WelcomeExperienceStep == 4;
#if PLAY_STORE_BUILD
    public bool IsWelcomeSettingsStep => ShowWelcomeExperience && WelcomeExperienceStep == 5;
    public bool IsWelcomeSupportStep => false;
#else
    public bool IsWelcomeSettingsStep => ShowWelcomeExperience && (WelcomeExperienceStep == 5 || WelcomeExperienceStep == 6);
    public bool IsWelcomeSupportStep => ShowWelcomeExperience && WelcomeExperienceStep == 6;
#endif
    public bool HasWelcomeExperienceTargetHint => !string.IsNullOrWhiteSpace(WelcomeExperienceTargetHint);
    public double WelcomeBackgroundContentOpacity => ShowWelcomeExperience && WelcomeExperienceStep == 0 ? 0.08 : 1.0;
    public string WelcomeExperienceStepTitle => WelcomeExperienceStep switch
    {
        0 => Resources.Loc.Instance.WelcomeExperienceIntroTitle,
        1 => Resources.Loc.Instance.WelcomeExperienceLibraryTitle,
        2 => Resources.Loc.Instance.WelcomeExperienceImportTitle,
        3 => Resources.Loc.Instance.WelcomeExperienceKomgaTitle,
        4 => Resources.Loc.Instance.WelcomeExperienceActivityTitle,
        5 => Resources.Loc.Instance.WelcomeExperienceSettingsTitle,
#if !PLAY_STORE_BUILD
        6 => Resources.Loc.Instance.WelcomeExperienceSupportTitle,
#endif
        _ => Resources.Loc.Instance.WelcomeExperienceIntroTitle
    };
    public string WelcomeExperienceStepDescription => WelcomeExperienceStep switch
    {
        0 => Resources.Loc.Instance.WelcomeExperienceIntroDescription,
        1 => Resources.Loc.Instance.WelcomeExperienceLibraryDescription,
        2 => Resources.Loc.Instance.WelcomeExperienceImportDescription,
        3 => Resources.Loc.Instance.WelcomeExperienceKomgaDescription,
        4 => Resources.Loc.Instance.WelcomeExperienceActivityDescription,
        5 => Resources.Loc.Instance.WelcomeExperienceSettingsDescription,
#if !PLAY_STORE_BUILD
        6 => Resources.Loc.Instance.WelcomeExperienceSupportDescription,
#endif
        _ => Resources.Loc.Instance.WelcomeExperienceIntroDescription
    };
    public string WelcomeExperienceTargetHint => WelcomeExperienceStep switch
    {
        1 => Resources.Loc.Instance.WelcomeExperienceLibraryHint,
        2 => Resources.Loc.Instance.WelcomeExperienceImportHint,
        3 => Resources.Loc.Instance.WelcomeExperienceKomgaHint,
        4 => Resources.Loc.Instance.WelcomeExperienceActivityHint,
        5 => Resources.Loc.Instance.WelcomeExperienceSettingsHint,
#if !PLAY_STORE_BUILD
        6 => Resources.Loc.Instance.WelcomeExperienceSupportHint,
#endif
        _ => string.Empty
    };

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

    [RelayCommand]
    private void CloseUpdateNotification()
    {
        ShowUpdateNotification = false;
    }

    [RelayCommand]
    private void GoToUpdate()
    {
        ShowUpdateNotification = false;
        _updateService.GoToReleases();
    }

    [RelayCommand]
    private void StartWelcomeExperience()
    {
        if (ShowWelcomeExperience)
        {
            return;
        }

        if (_isInitializing)
        {
            _shouldStartWelcomeAfterInitialization = true;
            return;
        }
        _shouldStartWelcomeAfterInitialization = false;

        _welcomeStartedInReader = IsInReader;
        _welcomeStartTabIndex = SelectedTabIndex;
        _hasWelcomeStartState = true;

        var settings = _settingsService.LoadSettings();
        _isApplyingWelcomePreferences = true;
        WelcomeSelectedLanguage = settings.UseSystemLanguage
            ? LocalizationService.AvailableLanguages[0]
            : LocalizationService.AvailableLanguages.FirstOrDefault(language => language.CultureCode == settings.LanguageCode) ?? LocalizationService.AvailableLanguages[0];
        WelcomeSelectedThemeOption = WelcomeThemeOptions.FirstOrDefault(option => option.Value == settings.AppTheme) ?? WelcomeThemeOptions[0];
        _isApplyingWelcomePreferences = false;

        HasAcceptedWelcomeLicense = false;
        ShowWelcomeLicenseDialog = false;
        ShowWelcomeExperience = true;
        IsInReader = false;
        ActivateTab(_welcomeStartTabIndex);
        SetWelcomeExperienceStep(0);
    }

    [RelayCommand]
    private void PreviousWelcomeExperienceStep()
    {
        SetWelcomeExperienceStep(WelcomeExperienceStep - 1);
    }

    [RelayCommand]
    private async Task NextWelcomeExperienceStepAsync()
    {
        if (!CanContinueWelcomeStep)
        {
            return;
        }

        if (IsWelcomeLastStep)
        {
            await CompleteWelcomeExperienceAsync();
            return;
        }

        SetWelcomeExperienceStep(WelcomeExperienceStep + 1);
    }

    [RelayCommand]
    private async Task SkipWelcomeExperienceAsync()
    {
        if (!CanSkipWelcome)
        {
            return;
        }

        await CompleteWelcomeExperienceAsync();
    }

    private void SetWelcomeExperienceStep(int step)
    {
        WelcomeExperienceStep = Math.Clamp(step, 0, WelcomeExperienceStepCount - 1);
        var targetTab = WelcomeExperienceStep switch
        {
            1 => 0,
            2 => 0,
            3 => 1,
            4 => 2,
            5 => 3,
#if !PLAY_STORE_BUILD
            6 => 3,
#endif
            _ => (int?)null
        };

        if (targetTab.HasValue)
        {
            ActivateTab(targetTab.Value);
        }
    }

    private async Task CompleteWelcomeExperienceAsync()
    {
        ShowWelcomeExperience = false;
        await _settingsService.UpdateSettingsAsync(settings => settings.HasCompletedWelcomeExperience = true);
        RestoreStateAfterWelcomeExperience();
    }

    private void RestoreStateAfterWelcomeExperience()
    {
        if (!_hasWelcomeStartState)
        {
            return;
        }

        _isRestoringWelcomeState = true;
        try
        {
            _hasWelcomeStartState = false;
            var restoreTabIndex = _welcomeStartTabIndex;
            var restoreReader = _welcomeStartedInReader;

            IsInReader = restoreReader;
            SelectedTabIndex = restoreTabIndex;
            CurrentView = restoreReader ? _readerViewModel : GetViewForTab(restoreTabIndex);
        }
        finally
        {
            _isRestoringWelcomeState = false;
        }
    }

    [RelayCommand]
    private void OpenWelcomeLicenseDialog()
    {
        ShowWelcomeLicenseDialog = true;
    }

    [RelayCommand]
    private void CloseWelcomeLicenseDialog()
    {
        ShowWelcomeLicenseDialog = false;
    }

    [RelayCommand]
    private void AcceptWelcomeLicense()
    {
        HasAcceptedWelcomeLicense = true;
        ShowWelcomeLicenseDialog = false;
    }

    [RelayCommand]
    private void OpenGplLicense()
    {
        _externalLinkService.OpenUrl("https://www.gnu.org/licenses/gpl-3.0.html");
    }

    [RelayCommand]
    private void OpenWelcomePayPal()
    {
        _externalLinkService.OpenPayPal();
    }

    [RelayCommand]
    private void OpenWelcomeKoFi()
    {
        _externalLinkService.OpenKoFi();
    }

    private async Task ApplyWelcomeLanguageAsync(LanguageOption language)
    {
        try
        {
            _localizationService.SetLanguage(language.CultureCode);
            await _settingsService.UpdateSettingsAsync(settings =>
            {
                settings.UseSystemLanguage = language.CultureCode is null;
                settings.LanguageCode = language.CultureCode;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to apply welcome language selection: {ex.Message}");
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var selectedThemeValue = WelcomeSelectedThemeOption?.Value ?? AppThemePreference.System;

            _isApplyingWelcomePreferences = true;
            _welcomeThemeOptions = null;
            OnPropertyChanged(nameof(WelcomeThemeOptions));
            WelcomeSelectedThemeOption = WelcomeThemeOptions.FirstOrDefault(option => option.Value == selectedThemeValue) ?? WelcomeThemeOptions[0];
            _isApplyingWelcomePreferences = false;

            RefreshWelcomeBindings();
        });
    }

    private static void ApplyTheme(AppThemePreference theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    [RelayCommand]
    private void ClosePremiumUnlockDialog()
    {
        ShowPremiumUnlockDialog = false;
    }

    [RelayCommand]
    private async Task PurchasePremiumUnlock()
    {
        await _trialService.UnlockPremiumAsync();
        ShowPremiumUnlockDialog = false;
    }
}
