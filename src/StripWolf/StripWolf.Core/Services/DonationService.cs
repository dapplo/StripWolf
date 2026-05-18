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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace StripWolf.Core.Services;

/// <summary>
/// Implementation of the donation service
/// </summary>
public class DonationService : IDonationService
{
    private const string KoFiUrl = "https://ko-fi.com/lakritzator";
    private const string PayPalUrl = "https://paypal.me/dapplo";
    private const string GitHubUrl = "https://github.com/dapplo/StripWolf";

    /// <inheritdoc />
    public void OpenKoFi() => OpenUrl(KoFiUrl);

    /// <inheritdoc />
    public void OpenPayPal() => OpenUrl(PayPalUrl);

    /// <inheritdoc />
    public void OpenGitHub() => OpenUrl(GitHubUrl);

    private static void OpenUrl(string url)
    {
        var launcher = GetLauncher();
        if (launcher != null)
        {
            _ = launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private static ILauncher? GetLauncher()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Launcher;
        }
        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            return TopLevel.GetTopLevel(singleView.MainView)?.Launcher;
        }
        return null;
    }
}
