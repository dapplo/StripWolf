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

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StripWolf.Models.Komga;

/// <summary>
/// Paginated response from Komga
/// </summary>
public class KomgaPage<T>
{
    [JsonPropertyName("content")]
    public List<T> Content { get; set; } = [];

    [JsonPropertyName("pageable")]
    public KomgaPageable? Pageable { get; set; }

    [JsonPropertyName("totalElements")]
    public int TotalElements { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("numberOfElements")]
    public int NumberOfElements { get; set; }

    [JsonPropertyName("first")]
    public bool First { get; set; }

    [JsonPropertyName("last")]
    public bool Last { get; set; }

    [JsonPropertyName("empty")]
    public bool Empty { get; set; }
}

