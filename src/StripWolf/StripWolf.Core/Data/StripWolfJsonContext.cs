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
using StripWolf.Core.Models;
using StripWolf.Core.Models.Komga;
using StripWolf.Core.Services;

namespace StripWolf.Core.Data;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<KomgaLibrary>), TypeInfoPropertyName = "ListKomgaLibrary")]
[JsonSerializable(typeof(KomgaPage<KomgaSeries>), TypeInfoPropertyName = "KomgaPageKomgaSeries")]
[JsonSerializable(typeof(KomgaPage<KomgaBook>), TypeInfoPropertyName = "KomgaPageKomgaBook")]
[JsonSerializable(typeof(KomgaPage<KomgaReadList>), TypeInfoPropertyName = "KomgaPageKomgaReadList")]
[JsonSerializable(typeof(KomgaSeries))]
[JsonSerializable(typeof(KomgaBook))]
[JsonSerializable(typeof(List<KomgaPageInfo>), TypeInfoPropertyName = "ListKomgaPageInfo")]
[JsonSerializable(typeof(KomgaReadList))]
[JsonSerializable(typeof(List<KomgaHeader>), TypeInfoPropertyName = "ListKomgaHeader")]
[JsonSerializable(typeof(KomgaReadProgressUpdate))]
[JsonSerializable(typeof(KomgaReadListUpdate))]
[JsonSerializable(typeof(Dictionary<int, string>), TypeInfoPropertyName = "DictionaryIntString")]
[JsonSerializable(typeof(Dictionary<int, SettingsService.SensitiveServerData>), TypeInfoPropertyName = "DictionaryIntSensitiveServerData")]
[JsonSerializable(typeof(int?), TypeInfoPropertyName = "NullableInt32")]
[JsonSerializable(typeof(GitHubRelease))]
public partial class StripWolfJsonContext : JsonSerializerContext
{
}

