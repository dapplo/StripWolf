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

using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace StripWolf.Models.Komga;

/// <summary>
/// Represents a library from Komga
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class KomgaLibrary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("root")]
    public string Root { get; set; } = string.Empty;

    [JsonPropertyName("importComicInfoBook")]
    public bool ImportComicInfoBook { get; set; }

    [JsonPropertyName("importComicInfoSeries")]
    public bool ImportComicInfoSeries { get; set; }

    [JsonPropertyName("importComicInfoCollection")]
    public bool ImportComicInfoCollection { get; set; }

    [JsonPropertyName("importComicInfoReadList")]
    public bool ImportComicInfoReadList { get; set; }

    [JsonPropertyName("importEpubBook")]
    public bool ImportEpubBook { get; set; }

    [JsonPropertyName("importEpubSeries")]
    public bool ImportEpubSeries { get; set; }

    [JsonPropertyName("importMylarSeries")]
    public bool ImportMylarSeries { get; set; }

    [JsonPropertyName("importLocalArtwork")]
    public bool ImportLocalArtwork { get; set; }

    [JsonPropertyName("importBarcodeIsbn")]
    public bool ImportBarcodeIsbn { get; set; }

    [JsonPropertyName("scanForceModifiedTime")]
    public bool ScanForceModifiedTime { get; set; }

    [JsonPropertyName("scanDeep")]
    public bool ScanDeep { get; set; }

    [JsonPropertyName("repairExtensions")]
    public bool RepairExtensions { get; set; }

    [JsonPropertyName("convertToCbz")]
    public bool ConvertToCbz { get; set; }

    [JsonPropertyName("emptyTrashAfterScan")]
    public bool EmptyTrashAfterScan { get; set; }

    [JsonPropertyName("seriesCover")]
    public string SeriesCover { get; set; } = string.Empty;

    [JsonPropertyName("hashFiles")]
    public bool HashFiles { get; set; }

    [JsonPropertyName("hashPages")]
    public bool HashPages { get; set; }

    [JsonPropertyName("analyzeFile")]
    public bool AnalyzeFile { get; set; }

    [JsonPropertyName("oneshotsDirectory")]
    public string? OneshotsDirectory { get; set; }

    [JsonPropertyName("unavailable")]
    public bool Unavailable { get; set; }
}

