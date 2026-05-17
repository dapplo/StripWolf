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

namespace StripWolf.Models.Komga;

/// <summary>
/// Represents a book (issue) from Komga
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class KomgaBook
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;


    [JsonPropertyName("seriesId")]
    public string SeriesId { get; set; } = string.Empty;

    [JsonPropertyName("seriesTitle")]
    public string SeriesTitle { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public float Number { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    [JsonPropertyName("fileLastModified")]
    public DateTime FileLastModified { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("size")]
    public string Size { get; set; } = string.Empty;

    [JsonPropertyName("media")]
    public KomgaMedia? Media { get; set; }

    [JsonPropertyName("metadata")]
    public KomgaBookMetadata? Metadata { get; set; }

    [JsonPropertyName("readProgress")]
    public KomgaReadProgress? ReadProgress { get; set; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("fileHash")]
    public string FileHash { get; set; } = string.Empty;

    [JsonPropertyName("oneshot")]
    public bool Oneshot { get; set; }

    /// <summary>
    /// Gets the thumbnail URL for the book (computed from the server URL)
    /// </summary>
    [JsonIgnore]
    public string? ThumbnailUrl { get; set; }
}

