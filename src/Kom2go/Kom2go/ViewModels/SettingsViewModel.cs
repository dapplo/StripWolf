using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kom2go.Data;
using Kom2go.Models;
using Kom2go.Services;

namespace Kom2go.ViewModels;

/// <summary>
/// View model for settings page
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly KomgaApiService _komgaApiService;

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

    private KomgaServer? _editingServer;

    public SettingsViewModel(DatabaseService databaseService, KomgaApiService komgaApiService)
    {
        _databaseService = databaseService;
        _komgaApiService = komgaApiService;
        Title = "Settings";
    }

    [RelayCommand]
    private async Task LoadServersAsync()
    {
        await ExecuteAsync(async () =>
        {
            var servers = await _databaseService.GetServersAsync();
            Servers.Clear();
            foreach (var server in servers)
            {
                Servers.Add(server);
            }
        });
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
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
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
            var server = _editingServer ?? new KomgaServer();
            server.Name = ServerName;
            server.BaseUrl = ServerUrl.TrimEnd('/');
            server.Username = Username;
            server.Password = Password;
            
            // Make the first server active by default
            if (Servers.Count == 0 && _editingServer is null)
            {
                server.IsActive = true;
            }

            await _databaseService.SaveServerAsync(server);

            if (_editingServer is null)
            {
                Servers.Add(server);
            }
            else
            {
                var index = Servers.IndexOf(_editingServer);
                if (index >= 0)
                {
                    Servers[index] = server;
                }
            }

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
            await _databaseService.DeleteServerAsync(server);
            Servers.Remove(server);
        }, "Failed to delete server");
    }

    [RelayCommand]
    private async Task SetActiveServerAsync(KomgaServer? server)
    {
        if (server is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            // Deactivate all servers
            foreach (var s in Servers)
            {
                if (s.IsActive)
                {
                    s.IsActive = false;
                    await _databaseService.SaveServerAsync(s);
                }
            }

            // Activate selected server
            server.IsActive = true;
            await _databaseService.SaveServerAsync(server);

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
