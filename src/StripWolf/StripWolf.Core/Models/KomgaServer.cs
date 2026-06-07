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

using System.Text.Json;
using SQLite;
using CommunityToolkit.Mvvm.ComponentModel;
using StripWolf.Core.Data;
using System.Diagnostics.CodeAnalysis;

namespace StripWolf.Core.Models;

/// <summary>
/// Represents a custom HTTP header
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
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
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
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
        get => JsonSerializer.Serialize(CustomHeaders, StripWolfJsonContext.Default.ListKomgaHeader);
        set => CustomHeaders = string.IsNullOrEmpty(value) ? [] : JsonSerializer.Deserialize(value, StripWolfJsonContext.Default.ListKomgaHeader) ?? [];
    }

    /// <summary>
    /// Last successful connection time
    /// </summary>
    public DateTime? LastConnected { get; set; }

    /// <summary>
    /// Gets or sets whether to bypass SSL/TLS certificate verification.
    /// </summary>
    public bool BypassSslValidation { get; set; }
}

