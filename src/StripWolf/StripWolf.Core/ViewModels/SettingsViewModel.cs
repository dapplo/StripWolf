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
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Core.Models;
using StripWolf.Core.Services;

namespace StripWolf.Core.ViewModels;

/// <summary>
/// View model for settings page
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly KomgaApiService _komgaApiService;
    private readonly LocalizationService _localizationService;
    private readonly IDonationService _donationService;
    
    private AppSettings? _appSettings;
    private int _nextServerId = 1;

    [ObservableProperty]
    private ObservableCollection<KomgaServer> _servers = [];

    [ObservableProperty]
    private KomgaServer? _selectedServer;

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
    private ReadingMode _selectedReadingMode = ReadingMode.Normal;
    
    [ObservableProperty]
    private Handedness _selectedHandedness = Handedness.RightHanded;

    [ObservableProperty]
    private bool _compactOverview;

    [ObservableProperty]
    private AppThemePreference _selectedAppTheme = AppThemePreference.System;

    [ObservableProperty]
    private EpubConversionTheme _selectedEpubConversionTheme = EpubConversionTheme.System;

    [ObservableProperty]
    private EpubOutputResolution _selectedEpubOutputResolution = EpubOutputResolution.Low;

    [ObservableProperty]
    private UnsupportedFormatHandlingMode _selectedUnsupportedFormatHandlingMode = UnsupportedFormatHandlingMode.ConvertOnImport;

    [ObservableProperty]
    private bool _skipExternalDeleteConfirmation;

    [ObservableProperty]
    private int _selectedKomgaParallelDownloads = 1;

    [ObservableProperty]
    private ObservableCollection<SectionLayoutPreference> _librarySections = [];

    [ObservableProperty]
    private ObservableCollection<SectionLayoutPreference> _komgaSections = [];
    
    /// <summary>
    /// Available reading modes
    /// </summary>
    public IReadOnlyList<ReadingMode> AvailableReadingModes =>
        SupportsGuidedReading
            ? [ReadingMode.Normal, ReadingMode.Zoomed, ReadingMode.Guided]
            : [ReadingMode.Normal, ReadingMode.Zoomed];
    
    /// <summary>
    /// Available handedness options
    /// </summary>
    public IReadOnlyList<Handedness> AvailableHandednessOptions { get; } = 
        [Handedness.RightHanded, Handedness.LeftHanded];

    public IReadOnlyList<AppThemePreference> AvailableAppThemes { get; } =
        [AppThemePreference.System, AppThemePreference.Light, AppThemePreference.Dark];

    public IReadOnlyList<EpubConversionTheme> AvailableEpubConversionThemes { get; } =
        [EpubConversionTheme.System, EpubConversionTheme.Light, EpubConversionTheme.Dark];

    public IReadOnlyList<EpubOutputResolution> AvailableEpubOutputResolutions { get; } =
        [EpubOutputResolution.Low, EpubOutputResolution.Medium, EpubOutputResolution.High];

    public IReadOnlyList<UnsupportedFormatHandlingMode> AvailableUnsupportedFormatHandlingModes { get; } =
        [UnsupportedFormatHandlingMode.ConvertOnImport, UnsupportedFormatHandlingMode.ConvertWhileReading];

    public string UnsupportedFormatHandlingDescription =>
        SupportsEpubFeatures
            ? "Convert On Import keeps the current behavior and stores a CBZ. Convert While Reading keeps the original PDF/EPUB and renders pages on demand, while still caching the cover thumbnail separately."
            : "Convert On Import keeps the current behavior and stores a CBZ. Convert While Reading keeps the original PDF and renders pages on demand, while still caching the cover thumbnail separately.";

    public IReadOnlyList<int> AvailableKomgaParallelDownloadOptions { get; } = [1, 2, 3, 4];

    private KomgaServer? _editingServer;

    public SettingsViewModel(SettingsService settingsService, KomgaApiService komgaApiService, LocalizationService localizationService, IDonationService donationService)
    {
        _settingsService = settingsService;
        _komgaApiService = komgaApiService;
        _localizationService = localizationService;
        _donationService = donationService;
        Title = "Settings";
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
        ObservableCollection<SectionLayoutPreference> target,
        IEnumerable<SectionLayoutPreference> source)
    {
        foreach (var item in target)
        {
            item.PropertyChanged -= OnSectionPreferenceChanged;
        }

        target.Clear();
        foreach (var item in source.OrderBy(section => section.Order))
        {
            item.Label = GetSectionLabel(item.Key);
            item.PropertyChanged += OnSectionPreferenceChanged;
            target.Add(item);
        }
    }

    private void OnSectionPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_appSettings is null || sender is not SectionLayoutPreference section)
        {
            return;
        }

        if (e.PropertyName == nameof(SectionLayoutPreference.Label))
        {
            return;
        }

        section.Label = GetSectionLabel(section.Key);
        _ = _settingsService.SaveSettingsAsync(_appSettings);
    }

    private string GetSectionLabel(string key)
    {
        return key switch
        {
            LibrarySectionKeys.ContinueReading => "Continue Reading",
            LibrarySectionKeys.NewComics => "New Comics",
            LibrarySectionKeys.Favorites => "Favorites",
            LibrarySectionKeys.Series => "Series",
            LibrarySectionKeys.Read => "Read",
            KomgaSectionKeys.KeepReading => "Keep Reading",
            KomgaSectionKeys.OnDeck => "On Deck",
            KomgaSectionKeys.RecentlyAddedBooks => "Recently Added Books",
            KomgaSectionKeys.RecentlyAddedSeries => "Recently Added Series",
            KomgaSectionKeys.Libraries => "Libraries",
            KomgaSectionKeys.ReadLists => "Read Lists",
            _ => key
        };
    }

    private async Task SaveSectionLayoutAsync()
    {
        if (_appSettings is null)
        {
            return;
        }

        await _settingsService.SaveSettingsAsync(_appSettings);
    }

    private ObservableCollection<SectionLayoutPreference>? GetSectionCollection(SectionLayoutPreference? section)
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

    private static void SyncSectionOrder(IReadOnlyList<SectionLayoutPreference> sections)
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
        SelectedAppTheme = _appSettings.AppTheme;
        SelectedReadingMode = NormalizeReadingMode(_appSettings.PreferredReadingMode);
        SelectedHandedness = _appSettings.Handedness;
        CompactOverview = _appSettings.CompactOverview;
        SelectedEpubConversionTheme = _appSettings.EpubConversionTheme;
        SelectedEpubOutputResolution = _appSettings.EpubOutputResolution;
        SelectedUnsupportedFormatHandlingMode = _appSettings.UnsupportedFormatHandlingMode;
        SkipExternalDeleteConfirmation = _appSettings.SkipExternalDeleteConfirmation;
        SelectedKomgaParallelDownloads = Math.Max(1, _appSettings.KomgaParallelDownloads);

        if (_appSettings.PreferredReadingMode != SelectedReadingMode)
        {
            _appSettings.PreferredReadingMode = SelectedReadingMode;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }

        ReplaceSectionCollection(LibrarySections, _appSettings.LibrarySections);
        ReplaceSectionCollection(KomgaSections, _appSettings.KomgaSections);
    }

    partial void OnSelectedAppThemeChanged(AppThemePreference value)
    {
        ApplyAppTheme(value);

        if (_appSettings is not null)
        {
            _appSettings.AppTheme = value;
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
        
        // Save to settings
        if (_appSettings is not null)
        {
            _appSettings.LanguageCode = value.CultureCode;
            _appSettings.UseSystemLanguage = value.CultureCode is null;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }
    
    partial void OnSelectedReadingModeChanged(ReadingMode value)
    {
        var normalized = NormalizeReadingMode(value);
        if (normalized != value)
        {
            SelectedReadingMode = normalized;
            return;
        }

        // Save to settings
        if (_appSettings is not null)
        {
            _appSettings.PreferredReadingMode = normalized;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }
    
    partial void OnSelectedHandednessChanged(Handedness value)
    {
        // Save to settings
        if (_appSettings is not null)
        {
            _appSettings.Handedness = value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedEpubConversionThemeChanged(EpubConversionTheme value)
    {
        if (_appSettings is not null)
        {
            _appSettings.EpubConversionTheme = value;
            _ = _settingsService.SaveSettingsAsync(_appSettings);
        }
    }

    partial void OnSelectedEpubOutputResolutionChanged(EpubOutputResolution value)
    {
        if (_appSettings is not null)
        {
            _appSettings.EpubOutputResolution = value;
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

    partial void OnSelectedUnsupportedFormatHandlingModeChanged(UnsupportedFormatHandlingMode value)
    {
        if (_appSettings is not null)
        {
            _appSettings.UnsupportedFormatHandlingMode = value;
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
        IsEditing = false;
        IsPasswordVisible = false;
        _editingServer = null;
        CustomHeaders.Clear();
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
            
            // Ensure we have an active server ID set if this is the only server or if none is active
            if (_appSettings.ActiveServerId == null || _appSettings.Servers.Count == 0)
            {
                server.IsActive = true;
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
        if (server is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            _appSettings?.Servers.RemoveAll(s => s.Id == server.Id);
            Servers.Remove(server);
            
            if (_appSettings is not null)
            {
                await _settingsService.SaveSettingsAsync(_appSettings);
            }
        }, "Failed to delete server");
    }

    [RelayCommand]
    private async Task SetActiveServerAsync(KomgaServer? server)
    {
        if (server is null || _appSettings is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            // Deactivate all servers
            foreach (var s in _appSettings.Servers)
            {
                s.IsActive = false;
            }
            foreach (var s in Servers)
            {
                s.IsActive = false;
            }

            // Activate selected server
            server.IsActive = true;
            _appSettings.ActiveServerId = server.Id;
            
            var settingsServer = _appSettings.Servers.FirstOrDefault(s => s.Id == server.Id);
            if (settingsServer is not null)
            {
                settingsServer.IsActive = true;
            }

            // Persist the change
            await _settingsService.SaveSettingsAsync(_appSettings);

            // Refresh the list
            LoadServers();
        }, "Failed to set active server");
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

            _komgaApiService.Configure(testServer);
            var success = await _komgaApiService.TestConnectionAsync();

            ConnectionStatus = success ? "✓ Connection successful!" : "✗ Connection failed";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"✗ Error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private async Task MoveSectionUpAsync(SectionLayoutPreference? section)
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

        collection.Move(index, index - 1);
        SyncSectionOrder(collection);
        await SaveSectionLayoutAsync();
    }

    [RelayCommand]
    private async Task MoveSectionDownAsync(SectionLayoutPreference? section)
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

        collection.Move(index, index + 1);
        SyncSectionOrder(collection);
        await SaveSectionLayoutAsync();
    }
}

