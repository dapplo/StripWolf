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
/// Metadata for a series
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class KomgaSeriesMetadata
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;


    [JsonPropertyName("statusLock")]
    public bool StatusLock { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("titleLock")]
    public bool TitleLock { get; set; }

    [JsonPropertyName("titleSort")]
    public string TitleSort { get; set; } = string.Empty;

    [JsonPropertyName("titleSortLock")]
    public bool TitleSortLock { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("summaryLock")]
    public bool SummaryLock { get; set; }

    [JsonPropertyName("readingDirection")]
    public string? ReadingDirection { get; set; }

    [JsonPropertyName("readingDirectionLock")]
    public bool ReadingDirectionLock { get; set; }

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = string.Empty;

    [JsonPropertyName("publisherLock")]
    public bool PublisherLock { get; set; }

    [JsonPropertyName("ageRating")]
    public int? AgeRating { get; set; }

    [JsonPropertyName("ageRatingLock")]
    public bool AgeRatingLock { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("languageLock")]
    public bool LanguageLock { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = [];

    [JsonPropertyName("genresLock")]
    public bool GenresLock { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("tagsLock")]
    public bool TagsLock { get; set; }

    [JsonPropertyName("totalBookCount")]
    public int? TotalBookCount { get; set; }

    [JsonPropertyName("totalBookCountLock")]
    public bool TotalBookCountLock { get; set; }

    [JsonPropertyName("sharingLabels")]
    public List<string> SharingLabels { get; set; } = [];

    [JsonPropertyName("sharingLabelsLock")]
    public bool SharingLabelsLock { get; set; }

    [JsonPropertyName("links")]
    public List<KomgaWebLink> Links { get; set; } = [];

    [JsonPropertyName("linksLock")]
    public bool LinksLock { get; set; }

    [JsonPropertyName("alternateTitles")]
    public List<KomgaAlternateTitle> AlternateTitles { get; set; } = [];

    [JsonPropertyName("alternateTitlesLock")]
    public bool AlternateTitlesLock { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }
}

