using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kom2go.Models;
using Kom2go.Services;

namespace Kom2go.ViewModels;

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
    private async Task LoadServersAsync()
    {
        await ExecuteAsync(async () =>
        {
            _appSettings = await _settingsService.LoadSettingsAsync();
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
        });
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
            string.IsNullOrWhiteSpace(ServerUrl) ||
            string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "All fields are required.";
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
            
            // Make the first server active by default
            if (_appSettings.Servers.Count == 0 && _editingServer is null)
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
            await LoadServersAsync();
        }, "Failed to set active server");
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) ||
            string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Password))
        {
            ConnectionStatus = "Please fill in all fields";
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
                Password = Password
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
}
