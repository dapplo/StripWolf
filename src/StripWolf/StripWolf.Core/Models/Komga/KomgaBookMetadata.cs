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
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace StripWolf.Models.Komga;

/// <summary>
/// Metadata for a book
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class KomgaBookMetadata
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;


    [JsonPropertyName("titleLock")]
    public bool TitleLock { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("summaryLock")]
    public bool SummaryLock { get; set; }

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("numberLock")]
    public bool NumberLock { get; set; }

    [JsonPropertyName("numberSort")]
    public float NumberSort { get; set; }

    [JsonPropertyName("numberSortLock")]
    public bool NumberSortLock { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("releaseDateLock")]
    public bool ReleaseDateLock { get; set; }

    [JsonPropertyName("authors")]
    public List<KomgaAuthor> Authors { get; set; } = [];

    [JsonPropertyName("authorsLock")]
    public bool AuthorsLock { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("tagsLock")]
    public bool TagsLock { get; set; }

    [JsonPropertyName("isbn")]
    public string Isbn { get; set; } = string.Empty;

    [JsonPropertyName("isbnLock")]
    public bool IsbnLock { get; set; }

    [JsonPropertyName("links")]
    public List<KomgaWebLink> Links { get; set; } = [];

    [JsonPropertyName("linksLock")]
    public bool LinksLock { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }
}

