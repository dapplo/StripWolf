using System.Text.Json;
using SQLite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Models;

/// <summary>
/// Represents a custom HTTP header
/// </summary>
public partial class KomgaHeader : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;
}

/// <summary>
/// Represents a Komga server connection configuration
/// </summary>
public class KomgaServer
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Display name for the server
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the Komga server (e.g., https://komga.example.com)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Username for authentication
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password for authentication (should be stored securely)
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// API Key for authentication (preferred over username/password)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Custom HTTP headers to be sent with every request
    /// </summary>
    [Ignore]
    public List<KomgaHeader> CustomHeaders { get; set; } = [];

    /// <summary>
    /// JSON representation of custom headers for SQLite storage
    /// </summary>
    public string CustomHeadersJson
    {
        get => JsonSerializer.Serialize(CustomHeaders);
        set => CustomHeaders = string.IsNullOrEmpty(value) ? [] : JsonSerializer.Deserialize<List<KomgaHeader>>(value) ?? [];
    }

    /// <summary>
    /// Whether this is the active/default server
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// When the server was added
    /// </summary>
    public DateTime AddedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last successful connection time
    /// </summary>
    public DateTime? LastConnected { get; set; }
}
