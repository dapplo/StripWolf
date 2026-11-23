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

namespace StripWolf.Core.Models;

/// <summary>
/// A simplified representation of a GitHub release response
/// </summary>
public record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("assets")] List<GitHubReleaseAsset>? Assets = null
);

/// <summary>
/// A file attached to a GitHub release
/// </summary>
/// <param name="Name">The file name, e.g. StripWolf-Android-arm64-v1.0.79-gc97e8f0.apk</param>
/// <param name="DownloadUrl">The public download URL</param>
/// <param name="Size">The size in bytes</param>
/// <param name="Digest">The checksum GitHub calculated on upload, e.g. "sha256:1f07..." (missing on old releases)</param>
public record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string? Digest = null
);
