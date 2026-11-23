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
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

/// <summary>
/// Installs a downloaded app update with the platform's own installer.
/// Only the Android build from GitHub releases has one, everywhere else updates go through the release page
/// (or the store).
/// </summary>
public interface IAppUpdateInstaller
{
    /// <summary>
    /// Whether this platform can install an update from a release file.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// The directory the update file is downloaded to. It is cleared before every download.
    /// </summary>
    string DownloadDirectory { get; }

    /// <summary>
    /// Picks the release file that fits this device, or null if the release has none.
    /// </summary>
    GitHubReleaseAsset? SelectAsset(IReadOnlyList<GitHubReleaseAsset> assets);

    /// <summary>
    /// Hands the downloaded file to the system installer, which asks the user to confirm the update.
    /// </summary>
    void LaunchInstaller(string filePath);
}

/// <summary>
/// Used on platforms which can't install updates themselves.
/// </summary>
public sealed class UnsupportedAppUpdateInstaller : IAppUpdateInstaller
{
    public bool IsSupported => false;

    public string DownloadDirectory => string.Empty;

    public GitHubReleaseAsset? SelectAsset(IReadOnlyList<GitHubReleaseAsset> assets) => null;

    public void LaunchInstaller(string filePath) => throw new System.NotSupportedException();
}
