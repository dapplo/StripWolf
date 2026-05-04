using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripWolf.Models;
using StripWolf.Services;

namespace StripWolf.ViewModels;

/// <summary>
/// View model for settings page
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly KomgaApiService _komgaApiService;
    private readonly LocalizationService _localizationService;
    
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
    private bool _isEditing;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string? _connectionStatus;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private bool _isReleaseNotesVisible;

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
    private ObservableCollection<SectionLayoutPreference> _librarySections = [];

    [ObservableProperty]
    private ObservableCollection<SectionLayoutPreference> _komgaSections = [];
    
    /// <summary>
    /// Available reading modes
    /// </summary>
    public IReadOnlyList<ReadingMode> AvailableReadingModes { get; } = 
        [ReadingMode.Normal, ReadingMode.Zoomed, ReadingMode.Guided];
    
    /// <summary>
    /// Available handedness options
    /// </summary>
    public IReadOnlyList<Handedness> AvailableHandednessOptions { get; } = 
        [Handedness.RightHanded, Handedness.LeftHanded];

    private KomgaServer? _editingServer;

    public SettingsViewModel(SettingsService settingsService, KomgaApiService komgaApiService, LocalizationService localizationService)
    {
        _settingsService = settingsService;
        _komgaApiService = komgaApiService;
        _localizationService = localizationService;
        Title = "Settings";
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
    private void ShowReleaseNotes()
    {
        IsReleaseNotesVisible = true;
    }

    [RelayCommand]
    private void HideReleaseNotes()
    {
        IsReleaseNotesVisible = false;
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
        SelectedReadingMode = _appSettings.PreferredReadingMode;
        SelectedHandedness = _appSettings.Handedness;
        CompactOverview = _appSettings.CompactOverview;

        ReplaceSectionCollection(LibrarySections, _appSettings.LibrarySections);
        ReplaceSectionCollection(KomgaSections, _appSettings.KomgaSections);
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
        // Save to settings
        if (_appSettings is not null)
        {
            _appSettings.PreferredReadingMode = value;
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

    [RelayCommand]
    private void AddServer()
    {
        _editingServer = null;
        ServerName = string.Empty;
        ServerUrl = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        ApiKey = string.Empty;
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
                ApiKey = ApiKey
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
