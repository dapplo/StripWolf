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

using System;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace StripWolf.Core.Models.Komga;

/// <summary>
/// Represents a series from Komga
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class KomgaSeries
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    [JsonPropertyName("fileLastModified")]
    public DateTime FileLastModified { get; set; }

    [JsonPropertyName("booksCount")]
    public int BooksCount { get; set; }

    [JsonPropertyName("booksReadCount")]
    public int BooksReadCount { get; set; }

    [JsonPropertyName("booksUnreadCount")]
    public int BooksUnreadCount { get; set; }

    [JsonPropertyName("booksInProgressCount")]
    public int BooksInProgressCount { get; set; }

    [JsonPropertyName("metadata")]
    public KomgaSeriesMetadata? Metadata { get; set; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("oneshot")]
    public bool Oneshot { get; set; }

    /// <summary>
    /// Gets the thumbnail URL for the series (computed from the server URL)
    /// </summary>
    [JsonIgnore]
    public string? ThumbnailUrl { get; set; }
}

