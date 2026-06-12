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
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StripWolf.Core.Data;
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

/// <summary>
/// Service that checks for new releases on GitHub
/// </summary>
public partial class UpdateService : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly IExternalLinkService _externalLinkService;
    private static readonly HttpClient HttpClient = new();

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StripWolf");
    }

    [ObservableProperty]
    private bool _isNewerVersionAvailable;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string? _updateStatus;

    /// <summary>
    /// Event raised when a newer version has been detected.
    /// Passes the version tag string.
    /// </summary>
    public event EventHandler<string>? NewVersionDetected;

    public UpdateService(SettingsService settingsService, IExternalLinkService externalLinkService)
    {
        _settingsService = settingsService;
        _externalLinkService = externalLinkService;

        // Load initial state from settings
        var settings = _settingsService.LoadSettings();
        _latestVersion = settings.LatestAvailableVersion;
        if (!string.IsNullOrEmpty(_latestVersion))
        {
            var currentVersion = GetRawCurrentVersion();
            _isNewerVersionAvailable = IsNewerVersion(currentVersion, _latestVersion);
        }
    }

    /// <summary>
    /// Gets the current running version string stripped of metadata
    /// </summary>
    public static string GetRawCurrentVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var infoVersion = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;

        var fullVersion = infoVersion?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "1.0.0";
        if (fullVersion.Contains('+'))
        {
            return fullVersion.Split('+')[0];
        }
        return fullVersion;
    }

    /// <summary>
    /// Compares two version strings to check if latest is newer than current
    /// </summary>
    public static bool IsNewerVersion(string currentVersionStr, string latestVersionStr)
    {
        if (string.IsNullOrWhiteSpace(latestVersionStr)) return false;

        static string NormalizeVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[1..];
            }

            // Ignore metadata and prerelease labels for update prompts to avoid false positives.
            normalized = normalized.Split('+')[0];
            normalized = normalized.Split('-')[0];
            return normalized;
        }

        var currentClean = NormalizeVersion(currentVersionStr);
        var latestClean = NormalizeVersion(latestVersionStr);

        if (!Version.TryParse(currentClean, out var currentVer) ||
            !Version.TryParse(latestClean, out var latestVer))
        {
            return false;
        }

        var normalizedCurrent = new Version(
            currentVer.Major,
            currentVer.Minor,
            Math.Max(currentVer.Build, 0),
            Math.Max(currentVer.Revision, 0));

        var normalizedLatest = new Version(
            latestVer.Major,
            latestVer.Minor,
            Math.Max(latestVer.Build, 0),
            Math.Max(latestVer.Revision, 0));

        return normalizedLatest > normalizedCurrent;
    }

    /// <summary>
    /// Runs a background check for updates only if a week has passed since the last check
    /// </summary>
    public async Task CheckForUpdatesIfNeededAsync()
    {
        var settings = _settingsService.LoadSettings();
        var now = DateTime.UtcNow;
        bool shouldCheck = false;

        if (settings.LastUpdateCheckTime == null)
        {
            shouldCheck = true;
        }
        else if (now - settings.LastUpdateCheckTime.Value > TimeSpan.FromDays(7))
        {
            shouldCheck = true;
        }

        if (shouldCheck)
        {
            await CheckForUpdatesInternalAsync(manual: false);
        }
    }

    /// <summary>
    /// Manually checks for updates, updating the status
    /// </summary>
    public async Task CheckForUpdatesManualAsync()
    {
        await CheckForUpdatesInternalAsync(manual: true);
    }

    private async Task CheckForUpdatesInternalAsync(bool manual)
    {
        if (IsChecking) return;

        Dispatcher.UIThread.Post(() =>
        {
            IsChecking = true;
            UpdateStatus = "Checking for updates...";
        });

        try
        {
            var response = await HttpClient.GetAsync("https://api.github.com/repos/dapplo/StripWolf/releases/latest");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize(json, StripWolfJsonContext.Default.GitHubRelease);

                if (release != null && !string.IsNullOrEmpty(release.TagName))
                {
                    var currentVersion = GetRawCurrentVersion();
                    var isNewer = IsNewerVersion(currentVersion, release.TagName);

                    var settings = _settingsService.LoadSettings();
                    settings.LastUpdateCheckTime = DateTime.UtcNow;
                    settings.LatestAvailableVersion = release.TagName;
                    await _settingsService.SaveSettingsAsync(settings);

                    Dispatcher.UIThread.Post(() =>
                    {
                        LatestVersion = release.TagName;
                        IsNewerVersionAvailable = isNewer;
                        UpdateStatus = isNewer ? "Newer version available!" : "Up to date";

                        if (isNewer)
                        {
                            NewVersionDetected?.Invoke(this, release.TagName);
                        }
                    });
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        UpdateStatus = "Failed to parse update info";
                    });
                }
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateStatus = $"Failed to check updates (HTTP {response.StatusCode})";
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Update check failed", ex);
            Dispatcher.UIThread.Post(() =>
            {
                UpdateStatus = manual ? "Connection error" : null;
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsChecking = false;
                // Clear status after a few seconds if up to date or error
                if (!IsNewerVersionAvailable)
                {
                    _ = ClearStatusDelayedAsync();
                }
            });
        }
    }

    private async Task ClearStatusDelayedAsync()
    {
        await Task.Delay(3000);
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsChecking && !IsNewerVersionAvailable)
            {
                UpdateStatus = null;
            }
        });
    }

    /// <summary>
    /// Navigates to the GitHub releases page
    /// </summary>
    public void GoToReleases()
    {
        _externalLinkService.OpenGitHubReleases();
    }
}
