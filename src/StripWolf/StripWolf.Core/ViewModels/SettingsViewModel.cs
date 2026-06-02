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
using Avalonia;
using Avalonia.Threading;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Core.Models;
using StripWolf.Core.Resources;
using StripWolf.Core.Services;

namespace StripWolf.Core.ViewModels;

public record StartupBehaviorOption(StartupBehavior Value, string DisplayName);
public record ReadingModeOption(ReadingMode Value, string DisplayName);
public record ReadingDirectionModeOption(ReadingDirectionMode Value, string DisplayName);
public record HandednessOption(Handedness Value, string DisplayName);
public record AppThemeOption(AppThemePreference Value, string DisplayName);
public record EpubConversionThemeOption(EpubConversionTheme Value, string DisplayName);
public record EpubOutputResolutionOption(EpubOutputResolution Value, string DisplayName);
public record UnsupportedFormatHandlingModeOption(UnsupportedFormatHandlingMode Value, string DisplayName);

/// <summary>
/// View model for settings page
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly LibraryService _libraryService;
    private readonly LocalizationService _localizationService;
    private readonly IDonationService _donationService;
    
    private AppSettings? _appSettings;
    private int _nextServerId = 1;
    private bool _suppressSectionLayoutPersistence;
    private CancellationTokenSource? _testConnectionCts;

    [ObservableProperty]
    private ObservableCollection<KomgaServer> _servers = [];

    [ObservableProperty]
    private KomgaServer? _selectedServer;

    [ObservableProperty]
    private bool _showServerDeleteConfirmation;

    [ObservableProperty]
    private int _linkedComicsCount;

    [ObservableProperty]
    private KomgaServer? _serverPendingDeletion;

    public int? ActiveServerId => _appSettings?.ActiveServerId;

    [ObservableProperty]
    private string _serverName = string.Empty;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private ObservableCollection<KomgaHeader> _customHeaders = [];

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string? _connectionStatus;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private bool _isSupportMeVisible;

    public string AppVersion
    {
        get
        {
            var assembly = typeof(SettingsViewModel).Assembly;
            var infoVersion = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;
            
            var fullVersion = infoVersion?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "1.0.0";
            
            string displayVersion;
            string? hash = null;

            if (fullVersion.Contains('+'))
            {
                var parts = fullVersion.Split('+');
                displayVersion = parts[0];
                var metadata = parts[1];
                
                // metadata could be just the hash, or Sha.hash, or other things
                if (metadata.StartsWith("Sha.", StringComparison.OrdinalIgnoreCase))
                {
                    hash = metadata[4..];
                }
                else
                {
                    hash = metadata;
                }
                
                if (hash.Length > 8) hash = hash[..8];
            }
            else
            {
                displayVersion = fullVersion;
            }

            return hash != null 
                ? $"Version {displayVersion} (g{hash})" 
                : $"Version {displayVersion}";
        }
    }

    public bool SupportsGuidedReading =>
#if DISABLE_GUIDED_READING
        false;
#else
        true;
#endif

    public bool SupportsEpubFeatures =>
#if DISABLE_EPUB_SUPPORT
        false;
#else
        true;
#endif

    [ObservableProperty]
    private LanguageOption _selectedLanguage = LocalizationService.AvailableLanguages[0];
    
    /// <summary>
    /// Available languages for selection
    /// </summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages => LocalizationService.AvailableLanguages;
    
    // Reading mode settings
    [ObservableProperty]
    private ReadingModeOption? _selectedReadingModeOption;

    [ObservableProperty]
    private ReadingDirectionModeOption? _selectedReadingDirectionModeOption;
    
    [ObservableProperty]
    private HandednessOption? _selectedHandednessOption;

    [ObservableProperty]
    private bool _compactOverview;

    [ObservableProperty]
    private AppThemeOption? _selectedAppThemeOption;

    [ObservableProperty]
    private EpubConversionThemeOption? _selectedEpubConversionThemeOption;

    [ObservableProperty]
    private EpubOutputResolutionOption? _selectedEpubOutputResolutionOption;

    [ObservableProperty]
    private UnsupportedFormatHandlingModeOption? _selectedUnsupportedFormatHandlingModeOption;

    [ObservableProperty]
    private bool _skipExternalDeleteConfirmation;

    [ObservableProperty]
    private bool _syncReadProgress;

    [ObservableProperty]
    private int _selectedKomgaParallelDownloads = 1;

    [ObservableProperty]
    private bool _allowMeteredKomgaDownloads;

    [ObservableProperty]
    private int _selectedKomgaSeriesPageSize = 20;

    [ObservableProperty]
    private int _selectedKomgaSearchLimit = 10;

    [ObservableProperty]
    private int _selectedKomgaSmartListSize = 10;

    [ObservableProperty]
    private ObservableCollection<SectionLayoutItemViewModel> _librarySections = [];

    [ObservableProperty]
    private ObservableCollection<SectionLayoutItemViewModel> _komgaSections = [];
    
    /// <summary>
    /// Available reading modes
    /// </summary>
    public IReadOnlyList<ReadingModeOption> AvailableReadingModes =>
        SupportsGuidedReading
            ? [
                new(ReadingMode.Normal, Loc.Instance.ReadingModeNormal),
                new(ReadingMode.Zoomed, Loc.Instance.ReadingModeZoomed),
                new(ReadingMode.Guided, Loc.Instance.ReadingModeGuided)
              ]
            : [
                new(ReadingMode.Normal, Loc.Instance.ReadingModeNormal),
                new(ReadingMode.Zoomed, Loc.Instance.ReadingModeZoomed)
              ];
    
    /// <summary>
    /// Available handedness options
    /// </summary>
    public IReadOnlyList<HandednessOption> AvailableHandednessOptions =>
    [
        new(Handedness.RightHanded, Loc.Instance.HandednessRight),
        new(Handedness.LeftHanded, Loc.Instance.HandednessLeft)
    ];

    /// <summary>
    /// Available reading direction options
    /// </summary>
    public IReadOnlyList<ReadingDirectionModeOption> AvailableReadingDirectionModes =>
    [
        new(ReadingDirectionMode.Automatic, Loc.Instance.ReadingDirectionAutomatic),
        new(ReadingDirectionMode.LeftToRight, Loc.Instance.ReadingDirectionLeftToRight),
        new(ReadingDirectionMode.RightToLeft, Loc.Instance.ReadingDirectionRightToLeft),
        new(ReadingDirectionMode.LeftToRightReversedPages, Loc.Instance.ReadingDirectionLeftToRightReversedPages),
        new(ReadingDirectionMode.RightToLeftReversedPages, Loc.Instance.ReadingDirectionRightToLeftReversedPages)
    ];

    public IReadOnlyList<StartupBehaviorOption> AvailableStartupBehaviors => 
    [
        new(StartupBehavior.ContinueWhereLeftOff, Loc.Instance.ContinueWhereLeftOff),
        new(StartupBehavior.Library, Loc.Instance.LibraryView)
    ];

    [ObservableProperty]
    private StartupBehaviorOption? _selectedStartupBehaviorOption;

    public IReadOnlyList<AppThemeOption> AvailableAppThemes =>
    [
        new(AppThemePreference.System, Loc.Instance.ThemeSystem),
        new(AppThemePreference.Light, Loc.Instance.ThemeLight),
        new(AppThemePreference.Dark, Loc.Instance.ThemeDark)
    ];

    public IReadOnlyList<EpubConversionThemeOption> AvailableEpubConversionThemes =>
    [
        new(EpubConversionTheme.System, Loc.Instance.ThemeSystem),
        new(EpubConversionTheme.Light, Loc.Instance.ThemeLight),
        new(EpubConversionTheme.Dark, Loc.Instance.ThemeDark)
    ];

    public IReadOnlyList<EpubOutputResolutionOption> AvailableEpubOutputResolutions =>
    [
        new(EpubOutputResolution.Low, Loc.Instance.ResolutionLow),
        new(EpubOutputResolution.Medium, Loc.Instance.ResolutionMedium),
        new(EpubOutputResolution.High, Loc.Instance.ResolutionHigh)
    ];

    public IReadOnlyList<UnsupportedFormatHandlingModeOption> AvailableUnsupportedFormatHandlingModes =>
    [
        new(UnsupportedFormatHandlingMode.ConvertOnImport, Loc.Instance.UnsupportedFormatHandlingConvertOnImport),
        new(UnsupportedFormatHandlingMode.ConvertWhileReading, Loc.Instance.UnsupportedFormatHandlingConvertWhileReading)
    ];

    public string UnsupportedFormatHandlingDescription => Loc.Instance.UnsupportedFormatHandlingDescription;

    public IReadOnlyList<int> AvailableKomgaParallelDownloadOptions { get; } = [1, 2, 3, 4];

    public IReadOnlyList<int> AvailableKomgaSeriesPageSizeOptions { get; } = [10, 20, 50, 100];

    public IReadOnlyList<int> AvailableKomgaSearchLimitOptions { get; } = [5, 10, 20, 50];

    public IReadOnlyList<int> AvailableKomgaSmartListSizeOptions { get; } = [5, 10, 20, 50];

    private KomgaServer? _editingServer;

    public SettingsViewModel(SettingsService settingsService, LibraryService libraryService, LocalizationService localizationService, IDonationService donationService)
    {
        _settingsService = settingsService;
        _libraryService = libraryService;
        _localizationService = localizationService;
        _donationService = donationService;
        Title = "Settings";
        _settingsService.SettingsChanged += (_, settings) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _appSettings = settings.Clone();
                ReplaceSectionCollection(LibrarySections, settings.LibrarySections);
                ReplaceSectionCollection(KomgaSections, settings.KomgaSections);
                OnPropertyChanged(nameof(ActiveServerId));
            });
        };
    }

    private ReadingMode NormalizeReadingMode(ReadingMode value)
    {
        if (!SupportsGuidedReading && value == ReadingMode.Guided)
        {
            return ReadingMode.Zoomed;
        }

        return value;
    }

    private void ReplaceSectionCollection(
        ObservableCollection<SectionLayoutItemViewModel> target,
        IEnumerable<SectionLayoutSettings> source)
    {
        _suppressSectionLayoutPersistence = true;
        try
        {
            foreach (var item in target)
            {
                item.PropertyChanged -= OnSectionLayoutItemChanged;
            }

            target.Clear();
            foreach (var item in source.OrderBy(section => section.Order))
            {
                var sectionViewModel = new SectionLayoutItemViewModel();
                sectionViewModel.Apply(item);
                sectionViewModel.PropertyChanged += OnSectionLayoutItemChanged;
                target.Add(sectionViewModel);
            }
        }
        finally
        {
            _suppressSectionLayoutPersistence = false;
        }
    }

    private void OnSectionLayoutItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSectionLayoutPersistence || sender is not SectionLayoutItemViewModel section)
        {
            return;
        }

        if (e.PropertyName == nameof(SectionLayoutItemViewModel.Label))
        {
            return;
        }

        section.RefreshLocalization();
        _ = PersistSectionLayoutAsync(section);
    }

    private Task PersistSectionLayoutAsync(SectionLayoutItemViewModel section)
    {
        var collection = GetSectionCollection(section);
        if (collection is null)
        {
            return Task.CompletedTask;
        }

        return _settingsService.UpdateSettingsAsync(settings =>
        {
            if (ReferenceEquals(collection, LibrarySections))
            {
                settings.LibrarySections = CreateSectionSnapshot(LibrarySections, SectionLayoutSettings.CreateDefaultLibrarySections());
            }
            else if (ReferenceEquals(collection, KomgaSections))
            {
                settings.KomgaSections = CreateSectionSnapshot(KomgaSections, SectionLayoutSettings.CreateDefaultKomgaSections());
            }
        });
    }

    private static List<SectionLayoutSettings> CreateSectionSnapshot(
        IEnumerable<SectionLayoutItemViewModel> sections,
        IReadOnlyList<SectionLayoutSettings> defaults)
    {
        return SectionLayoutSettings.MergeWithDefaults(
            sections.Select(section => section.ToSettings()),
            defaults);
    }

    private ObservableCollection<SectionLayoutItemViewModel>? GetSectionCollection(SectionLayoutItemViewModel? section)
    {
        if (section is null)
        {
            return null;
        }

        if (LibrarySections.Contains(section))
        {
            return LibrarySections;
        }

        if (KomgaSections.Contains(section))
        {
            return KomgaSections;
        }

        return null;
    }

    private static void SyncSectionOrder(IReadOnlyList<SectionLayoutItemViewModel> sections)
    {
        for (var index = 0; index < sections.Count; index++)
        {
            sections[index].Order = index;
        }
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    [RelayCommand]
    private void ToggleSupportMe()
    {
        IsSupportMeVisible = !IsSupportMeVisible;
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        _donationService.OpenGitHub();
    }

    [RelayCommand]
    private void OpenPayPal()
    {
        _donationService.OpenPayPal();
    }

    [RelayCommand]
    private void OpenKoFi()
    {
        _donationService.OpenKoFi();
    }

    [RelayCommand]
    private void LoadServers()
    {
        _appSettings = _settingsService.LoadSettings();
        Servers.Clear();
        foreach (var server in _appSettings.Servers)
        {
            Servers.Add(server);
        }
            
        // Track the next available ID
        if (_appSettings.Servers.Count > 0)
        {
            _nextServerId = _appSettings.Servers.Max(s => s.Id) + 1;
        }
            
        // Load language setting
        if (_appSettings.UseSystemLanguage)
        {
            SelectedLanguage = AvailableLanguages[0]; // System Default
        }
        else
        {
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.CultureCode == _appSettings.LanguageCode) 
                                ?? AvailableLanguages[0];
        }
            
        // Load reading mode settings
        SelectedAppThemeOption = AvailableAppThemes.FirstOrDefault(o => o.Value == _appSettings.AppTheme) ?? AvailableAppThemes[0];
        var readingMode = NormalizeReadingMode(_appSettings.PreferredReadingMode);
        SelectedReadingModeOption = AvailableReadingModes.FirstOrDefault(o => o.Value == readingMode) ?? AvailableReadingModes[0];
        SelectedReadingDirectionModeOption = AvailableReadingDirectionModes.FirstOrDefault(o => o.Value == _appSettings.PreferredReadingDirectionMode) ?? AvailableReadingDirectionModes[0];
        SelectedHandednessOption = AvailableHandednessOptions.FirstOrDefault(o => o.Value == _appSettings.Handedness) ?? AvailableHandednessOptions[0];
        SelectedStartupBehaviorOption = AvailableStartupBehaviors.FirstOrDefault(o => o.Value == _appSettings.StartupBehavior) ?? AvailableStartupBehaviors[0];
        CompactOverview = _appSettings.CompactOverview;
        SelectedEpubConversionThemeOption = AvailableEpubConversionThemes.FirstOrDefault(o => o.Value == _appSettings.EpubConversionTheme) ?? AvailableEpubConversionThemes[0];
        SelectedEpubOutputResolutionOption = AvailableEpubOutputResolutions.FirstOrDefault(o => o.Value == _appSettings.EpubOutputResolution) ?? AvailableEpubOutputResolutions[0];
        SelectedUnsupportedFormatHandlingModeOption = AvailableUnsupportedFormatHandlingModes.FirstOrDefault(o => o.Value == _appSettings.UnsupportedFormatHandlingMode) ?? AvailableUnsupportedFormatHandlingModes[0];
        SkipExternalDeleteConfirmation = _appSettings.SkipExternalDeleteConfirmation;
        SyncReadProgress = _appSettings.SyncReadProgress;
        SelectedKomgaParallelDownloads = Math.Max(1, _appSettings.KomgaParallelDownloads);
        AllowMeteredKomgaDownloads = _appSettings.AllowMeteredKomgaDownloads;
        SelectedKomgaSeriesPageSize = Math.Max(1, _appSettings.KomgaSeriesPageSize);
        SelectedKomgaSearchLimit = Math.Max(1, _appSettings.KomgaSearchLimit);
        SelectedKomgaSmartListSize = Math.Max(1, _appSettings.KomgaSmartListSize);

        if (_appSettings.PreferredReadingMode != readingMode)
        {
            _appSettings.PreferredReadingMode = readingMode;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }

        ReplaceSectionCollection(LibrarySections, _appSettings.LibrarySections);
        ReplaceSectionCollection(KomgaSections, _appSettings.KomgaSections);
    }

    partial void OnSyncReadProgressChanged(bool value)
    {
        if (_appSettings is not null)
        {
            _appSettings.SyncReadProgress = value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedAppThemeOptionChanged(AppThemeOption? value)
    {
        if (value is null) return;
        ApplyAppTheme(value.Value);

        if (_appSettings is not null)
        {
            _appSettings.AppTheme = value.Value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnCompactOverviewChanged(bool value)
    {
        // Save to settings
        if (_appSettings is not null)
        {
            _appSettings.CompactOverview = value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        // Apply language change
        _localizationService.SetLanguage(value.CultureCode);
        
        // Refresh all localized properties
        OnPropertyChanged(nameof(AvailableReadingModes));
        OnPropertyChanged(nameof(AvailableReadingDirectionModes));
        OnPropertyChanged(nameof(AvailableHandednessOptions));
        OnPropertyChanged(nameof(AvailableStartupBehaviors));
        OnPropertyChanged(nameof(AvailableAppThemes));
        OnPropertyChanged(nameof(AvailableEpubConversionThemes));
        OnPropertyChanged(nameof(AvailableEpubOutputResolutions));
        OnPropertyChanged(nameof(AvailableUnsupportedFormatHandlingModes));
        OnPropertyChanged(nameof(UnsupportedFormatHandlingDescription));

        // Re-sync current selections to the new localized options
        if (_appSettings is not null)
        {
            SelectedReadingModeOption = AvailableReadingModes.FirstOrDefault(o => o.Value == _appSettings.PreferredReadingMode);
            SelectedReadingDirectionModeOption = AvailableReadingDirectionModes.FirstOrDefault(o => o.Value == _appSettings.PreferredReadingDirectionMode);
            SelectedHandednessOption = AvailableHandednessOptions.FirstOrDefault(o => o.Value == _appSettings.Handedness);
            SelectedStartupBehaviorOption = AvailableStartupBehaviors.FirstOrDefault(o => o.Value == _appSettings.StartupBehavior);
            SelectedAppThemeOption = AvailableAppThemes.FirstOrDefault(o => o.Value == _appSettings.AppTheme);
            SelectedEpubConversionThemeOption = AvailableEpubConversionThemes.FirstOrDefault(o => o.Value == _appSettings.EpubConversionTheme);
            SelectedEpubOutputResolutionOption = AvailableEpubOutputResolutions.FirstOrDefault(o => o.Value == _appSettings.EpubOutputResolution);
            SelectedUnsupportedFormatHandlingModeOption = AvailableUnsupportedFormatHandlingModes.FirstOrDefault(o => o.Value == _appSettings.UnsupportedFormatHandlingMode);
        }

        foreach (var section in LibrarySections) section.RefreshLocalization();
        foreach (var section in KomgaSections) section.RefreshLocalization();

        // Save to settings
        if (_appSettings is not null)
        {
            _appSettings.LanguageCode = value.CultureCode;
            _appSettings.UseSystemLanguage = value.CultureCode is null;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }
    
    partial void OnSelectedReadingModeOptionChanged(ReadingModeOption? value)
    {
        if (value is null) return;
        var normalized = NormalizeReadingMode(value.Value);
        if (normalized != value.Value)
        {
            SelectedReadingModeOption = AvailableReadingModes.FirstOrDefault(o => o.Value == normalized);
            return;
        }

        // Save to settings
        if (_appSettings is not null)
        {
            _appSettings.PreferredReadingMode = normalized;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedReadingDirectionModeOptionChanged(ReadingDirectionModeOption? value)
    {
        if (value is null) return;
        if (_appSettings is not null)
        {
            _appSettings.PreferredReadingDirectionMode = value.Value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }
    
    partial void OnSelectedHandednessOptionChanged(HandednessOption? value)
    {
        if (value is null) return;
        // Save to settings
        if (_appSettings is not null)
        {
            _appSettings.Handedness = value.Value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedStartupBehaviorOptionChanged(StartupBehaviorOption? value)
    {
        // Save to settings
        if (value is not null && _appSettings is not null)
        {
            _appSettings.StartupBehavior = value.Value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedEpubConversionThemeOptionChanged(EpubConversionThemeOption? value)
    {
        if (value is not null && _appSettings is not null)
        {
            _appSettings.EpubConversionTheme = value.Value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedEpubOutputResolutionOptionChanged(EpubOutputResolutionOption? value)
    {
        if (value is not null && _appSettings is not null)
        {
            _appSettings.EpubOutputResolution = value.Value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedKomgaParallelDownloadsChanged(int value)
    {
        if (_appSettings is not null)
        {
            _appSettings.KomgaParallelDownloads = Math.Max(1, value);
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedKomgaSeriesPageSizeChanged(int value)
    {
        if (_appSettings is not null)
        {
            _appSettings.KomgaSeriesPageSize = Math.Max(1, value);
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedKomgaSearchLimitChanged(int value)
    {
        if (_appSettings is not null)
        {
            _appSettings.KomgaSearchLimit = Math.Max(1, value);
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedKomgaSmartListSizeChanged(int value)
    {
        if (_appSettings is not null)
        {
            _appSettings.KomgaSmartListSize = Math.Max(1, value);
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnAllowMeteredKomgaDownloadsChanged(bool value)
    {
        if (_appSettings is not null)
        {
            _appSettings.AllowMeteredKomgaDownloads = value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedUnsupportedFormatHandlingModeOptionChanged(UnsupportedFormatHandlingModeOption? value)
    {
        if (value is not null && _appSettings is not null)
        {
            _appSettings.UnsupportedFormatHandlingMode = value.Value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSkipExternalDeleteConfirmationChanged(bool value)
    {
        if (_appSettings is not null)
        {
            _appSettings.SkipExternalDeleteConfirmation = value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    private static void ApplyAppTheme(AppThemePreference theme)
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
    private void AddHeader()
    {
        CustomHeaders.Add(new KomgaHeader());
    }

    [RelayCommand]
    private void RemoveHeader(KomgaHeader? header)
    {
        if (header is not null)
        {
            CustomHeaders.Remove(header);
        }
    }

    [RelayCommand]
    private void AddServer()
    {
        _editingServer = null;
        ServerName = string.Empty;
        ServerUrl = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        ApiKey = string.Empty;
        CustomHeaders.Clear();
        ConnectionStatus = null;
        IsPasswordVisible = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void EditServer(KomgaServer? server)
    {
        if (server is null)
        {
            return;
        }

        _editingServer = server;
        ServerName = server.Name;
        ServerUrl = server.BaseUrl;
        Username = server.Username;
        Password = server.Password;
        ApiKey = server.ApiKey;
        CustomHeaders.Clear();
        foreach (var header in server.CustomHeaders)
        {
            CustomHeaders.Add(new KomgaHeader { Name = header.Name, Value = header.Value });
        }
        ConnectionStatus = null;
        IsPasswordVisible = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        CancelConnectionTest();
        IsEditing = false;
        IsPasswordVisible = false;
        _editingServer = null;
        CustomHeaders.Clear();
    }

    private void CancelConnectionTest()
    {
        var testConnectionCts = _testConnectionCts;
        _testConnectionCts = null;
        if (testConnectionCts is null)
        {
            return;
        }

        testConnectionCts.Cancel();
        testConnectionCts.Dispose();
        IsTestingConnection = false;
    }

    [RelayCommand]
    private async Task SaveServerAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerName) ||
            string.IsNullOrWhiteSpace(ServerUrl))
        {
            ErrorMessage = "Server Name and URL are required.";
            return;
        }

        bool hasAuth = !string.IsNullOrWhiteSpace(ApiKey) || 
                       (!string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password));

        if (!hasAuth)
        {
            ErrorMessage = "Either API Key OR Username and Password are required.";
            return;
        }

        // Validate URL format
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            ErrorMessage = "Please enter a valid URL (e.g., https://komga.example.com)";
            return;
        }

        await ExecuteAsync(async () =>
        {
            _appSettings ??= new AppSettings();
            
            KomgaServer server;
            if (_editingServer is not null)
            {
                server = _editingServer;
            }
            else
            {
                server = new KomgaServer
                {
                    Id = _nextServerId++
                };
            }
            
            server.Name = ServerName;
            server.BaseUrl = ServerUrl.TrimEnd('/');
            server.Username = Username;
            server.Password = Password;
            server.ApiKey = ApiKey;
            server.CustomHeaders = CustomHeaders.Where(h => !string.IsNullOrWhiteSpace(h.Name)).ToList();
            
            // Ensure we have a browsing server ID set if this is the only server
            if (_appSettings.ActiveServerId == null || _appSettings.Servers.Count == 0)
            {
                _appSettings.ActiveServerId = server.Id;
            }
            
            if (_editingServer is null)
            {
                _appSettings.Servers.Add(server);
                Servers.Add(server);
            }
            else
            {
                var index = Servers.IndexOf(_editingServer);
                if (index >= 0)
                {
                    Servers[index] = server;
                }
                
                // Update in settings list
                var settingsIndex = _appSettings.Servers.FindIndex(s => s.Id == server.Id);
                if (settingsIndex >= 0)
                {
                    _appSettings.Servers[settingsIndex] = server;
                }
            }

            // Persist settings with encrypted password
            await _settingsService.SaveSettingsAsync(_appSettings);

            IsEditing = false;
            _editingServer = null;
            CustomHeaders.Clear();
        }, "Failed to save server");
    }

    [RelayCommand]
    private async Task DeleteServerAsync(KomgaServer? server)
    {
        if (server is null || _appSettings is null)
        {
            return;
        }

        // Check if comics are linked to this server
        var linkedComics = await _libraryService.GetComicsByKomgaServerIdAsync(server.Id);
        if (linkedComics.Count > 0)
        {
            // Show confirmation overlay
            ServerPendingDeletion = server;
            LinkedComicsCount = linkedComics.Count;
            ShowServerDeleteConfirmation = true;
            return;
        }

        // No linked comics, delete directly
        await PerformDeleteServerAsync(server);
    }

    [RelayCommand]
    private async Task ConfirmDeleteServerAsync()
    {
        if (ServerPendingDeletion != null)
        {
            await PerformDeleteServerAsync(ServerPendingDeletion);
            CancelDeleteServer();
        }
    }

    [RelayCommand]
    private void CancelDeleteServer()
    {
        ShowServerDeleteConfirmation = false;
        ServerPendingDeletion = null;
        LinkedComicsCount = 0;
    }

    private async Task PerformDeleteServerAsync(KomgaServer server)
    {
        await ExecuteAsync(async () =>
        {
            _appSettings?.Servers.RemoveAll(s => s.Id == server.Id);
            Servers.Remove(server);
            
            // If the deleted server was the browsing server, reset it
            if (_appSettings?.ActiveServerId == server.Id)
            {
                _appSettings.ActiveServerId = _appSettings.Servers.FirstOrDefault()?.Id;
            }

            if (_appSettings is not null)
            {
                await _settingsService.SaveSettingsAsync(_appSettings);
            }
        }, "Failed to delete server");
    }

    [RelayCommand]
    private async Task SetBrowsingServerAsync(KomgaServer? server)
    {
        if (server is null || _appSettings is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            _appSettings.ActiveServerId = server.Id;

            // Persist the change
            await _settingsService.SaveSettingsAsync(_appSettings);

            // Refresh the list
            LoadServers();
        }, "Failed to set browsing server");
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            ConnectionStatus = "URL is required";
            return;
        }

        bool hasAuth = !string.IsNullOrWhiteSpace(ApiKey) || 
                       (!string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password));

        if (!hasAuth)
        {
            ConnectionStatus = "Credentials or API Key required";
            return;
        }

        CancelConnectionTest();
        var testConnectionCts = new CancellationTokenSource();
        _testConnectionCts = testConnectionCts;
        IsTestingConnection = true;
        ConnectionStatus = "Testing connection...";

        try
        {
            var testServer = new KomgaServer
            {
                BaseUrl = ServerUrl.TrimEnd('/'),
                Username = Username,
                Password = Password,
                ApiKey = ApiKey,
                CustomHeaders = CustomHeaders.Where(h => !string.IsNullOrWhiteSpace(h.Name)).ToList()
            };

            using var testKomgaApiService = new KomgaApiService();
            testKomgaApiService.Configure(testServer);
            var result = await testKomgaApiService.TestConnectionWithDetailsAsync(testConnectionCts.Token);

            if (!ReferenceEquals(_testConnectionCts, testConnectionCts))
            {
                return;
            }

            ConnectionStatus = result.Success
                ? "✓ Connection successful!"
                : string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "✗ Connection failed"
                    : $"✗ Connection failed: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"✗ Error: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_testConnectionCts, testConnectionCts))
            {
                _testConnectionCts = null;
                testConnectionCts.Dispose();
                IsTestingConnection = false;
            }
        }
    }

    [RelayCommand]
    private async Task MoveSectionUpAsync(SectionLayoutItemViewModel? section)
    {
        var collection = GetSectionCollection(section);
        if (section is null || collection is null)
        {
            return;
        }

        var index = collection.IndexOf(section);
        if (index <= 0)
        {
            return;
        }

        await MoveSectionAsync(section, collection[index - 1]);
    }

    [RelayCommand]
    private async Task MoveSectionDownAsync(SectionLayoutItemViewModel? section)
    {
        var collection = GetSectionCollection(section);
        if (section is null || collection is null)
        {
            return;
        }

        var index = collection.IndexOf(section);
        if (index < 0 || index >= collection.Count - 1)
        {
            return;
        }

        await MoveSectionAsync(section, collection[index + 1]);
    }

    public async Task MoveSectionAsync(SectionLayoutItemViewModel? section, SectionLayoutItemViewModel? targetSection)
    {
        var sourceCollection = GetSectionCollection(section);
        var targetCollection = GetSectionCollection(targetSection);
        if (section is null ||
            targetSection is null ||
            sourceCollection is null ||
            targetCollection is null ||
            !ReferenceEquals(sourceCollection, targetCollection) ||
            ReferenceEquals(section, targetSection))
        {
            return;
        }

        var sourceIndex = sourceCollection.IndexOf(section);
        var targetIndex = targetCollection.IndexOf(targetSection);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        _suppressSectionLayoutPersistence = true;
        try
        {
            sourceCollection.Move(sourceIndex, targetIndex);
            SyncSectionOrder(sourceCollection);
        }
        finally
        {
            _suppressSectionLayoutPersistence = false;
        }

        await PersistSectionLayoutAsync(section);
    }
}
