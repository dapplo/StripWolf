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
using Avalonia.Platform.Storage;

namespace StripWolf.Core.Services;

/// <summary>
/// Implementation of the external link opening service
/// </summary>
public class ExternalLinkService : IExternalLinkService
{
    private const string KoFiUrl = "https://ko-fi.com/lakritzator";
    private const string PayPalUrl = "https://paypal.me/dapplo";
    private const string GitHubUrl = "https://github.com/dapplo/StripWolf";
    private const string GitHubReleasesUrl = "https://github.com/dapplo/StripWolf/releases";

    /// <inheritdoc />
    public void OpenKoFi() => OpenUrl(KoFiUrl);

    /// <inheritdoc />
    public void OpenPayPal() => OpenUrl(PayPalUrl);

    /// <inheritdoc />
    public void OpenGitHub() => OpenUrl(GitHubUrl);

    /// <inheritdoc />
    public void OpenGitHubReleases() => OpenUrl(GitHubReleasesUrl);

    /// <inheritdoc />
    public void OpenUrl(string url)
    {
        var launcher = App.TopLevel?.Launcher;
        if (launcher != null)
        {
            _ = launcher.LaunchUriAsync(new Uri(url));
        }
    }
}
