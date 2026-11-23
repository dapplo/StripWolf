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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
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
    private readonly IAppUpdateInstaller _appUpdateInstaller;
    private readonly TrialService _trialService;
    private static readonly HttpClient HttpClient = new();
    private const string LatestReleaseUrl = "https://api.github.com/repos/dapplo/StripWolf/releases/latest";

    /// <summary>
    /// The release found by the last check, so installing it doesn't need to ask GitHub again
    /// </summary>
    private GitHubRelease? _latestRelease;

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StripWolf");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallAvailableUpdate))]
    private bool _isNewerVersionAvailable;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string? _updateStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartUpdateInstall))]
    private bool _isDownloadingUpdate;

    /// <summary>
    /// Progress or error of downloading and installing an update, null when nothing happened yet
    /// </summary>
    [ObservableProperty]
    private string? _updateInstallStatus;

    /// <summary>
    /// Event raised when a newer version has been detected.
    /// Passes the version tag string.
    /// </summary>
    public event EventHandler<string>? NewVersionDetected;

    public UpdateService(
        SettingsService settingsService,
        IExternalLinkService externalLinkService,
        IAppUpdateInstaller appUpdateInstaller,
        TrialService trialService)
    {
        _settingsService = settingsService;
        _externalLinkService = externalLinkService;
        _appUpdateInstaller = appUpdateInstaller;
        _trialService = trialService;

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
    /// Whether this build can download and install an update itself: only the unlocked Android build from the
    /// GitHub releases. The Play Store build is updated by the store, other platforms use the release page.
    /// </summary>
    public bool CanInstallUpdate
    {
        get
        {
#if PLAY_STORE_BUILD
            return false;
#else
            return _appUpdateInstaller.IsSupported && _trialService.IsUnlimitedUnlocked;
#endif
        }
    }

    /// <summary>
    /// Whether the install button should be enabled right now
    /// </summary>
    public bool CanStartUpdateInstall => CanInstallUpdate && !IsDownloadingUpdate;

    /// <summary>
    /// Whether there is a newer version which this build can install itself
    /// </summary>
    public bool CanInstallAvailableUpdate => CanInstallUpdate && IsNewerVersionAvailable;

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
            UpdateStatus = Resources.Loc.Instance.UpdateStatusChecking;
        });

        try
        {
            var response = await HttpClient.GetAsync(LatestReleaseUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize(json, StripWolfJsonContext.Default.GitHubRelease);

                if (release != null && !string.IsNullOrEmpty(release.TagName))
                {
                    _latestRelease = release;
                    var currentVersion = GetRawCurrentVersion();
                    var isNewer = IsNewerVersion(currentVersion, release.TagName);

                    // Only update the update-check fields, saving a whole settings snapshot could overwrite
                    // concurrent changes made elsewhere
                    var latestVersion = release.TagName;
                    await _settingsService.UpdateSettingsAsync(s =>
                    {
                        s.LastUpdateCheckTime = DateTime.UtcNow;
                        s.LatestAvailableVersion = latestVersion;
                    });

                    Dispatcher.UIThread.Post(() =>
                    {
                        LatestVersion = release.TagName;
                        IsNewerVersionAvailable = isNewer;
                        UpdateStatus = isNewer ? Resources.Loc.Instance.NewerVersionAvailable : Resources.Loc.Instance.UpdateStatusUpToDate;

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
                        UpdateStatus = Resources.Loc.Instance.UpdateStatusCheckFailed;
                    });
                }
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateStatus = $"{Resources.Loc.Instance.UpdateStatusCheckFailed} (HTTP {(int)response.StatusCode})";
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Update check failed", ex);
            Dispatcher.UIThread.Post(() =>
            {
                UpdateStatus = manual ? Resources.Loc.Instance.UpdateStatusConnectionError : null;
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
    /// Downloads the update for this device from the latest GitHub release and hands it to the system installer,
    /// which asks the user to confirm. Falls back to the release page when that isn't possible.
    /// </summary>
    /// <returns>True when the system installer was started</returns>
    public async Task<bool> DownloadAndInstallUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInstallUpdate)
        {
            GoToReleases();
            return false;
        }
        if (IsDownloadingUpdate) return false;

        IsDownloadingUpdate = true;
        UpdateInstallStatus = string.Format(Resources.Loc.Instance.UpdateDownloading, 0);
        try
        {
            var release = _latestRelease ?? await FetchLatestReleaseAsync(cancellationToken);
            if (release?.Assets is not { Count: > 0 } assets || _appUpdateInstaller.SelectAsset(assets) is not { } asset)
            {
                // The release has no file for this device (yet), let the user look for themselves
                UpdateInstallStatus = Resources.Loc.Instance.UpdateNoDownloadFound;
                GoToReleases();
                return false;
            }

            // Download off the UI thread, the progress is posted back to it
            var downloadDirectory = _appUpdateInstaller.DownloadDirectory;
            var filePath = await Task.Run(() => DownloadAssetAsync(asset, downloadDirectory, percent =>
                Dispatcher.UIThread.Post(() => UpdateInstallStatus = string.Format(Resources.Loc.Instance.UpdateDownloading, percent)),
                cancellationToken), cancellationToken);

            UpdateInstallStatus = Resources.Loc.Instance.UpdateReadyToInstall;
            _appUpdateInstaller.LaunchInstaller(filePath);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error("Downloading the update failed", ex);
            UpdateInstallStatus = Resources.Loc.Instance.UpdateDownloadFailed;
            return false;
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize(json, StripWolfJsonContext.Default.GitHubRelease);
    }

    /// <summary>
    /// Downloads a release file into the (cleared) directory and checks its size and checksum
    /// </summary>
    /// <returns>The path of the downloaded file</returns>
    internal static async Task<string> DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string directory,
        Action<int>? progress,
        CancellationToken cancellationToken,
        HttpClient? httpClient = null)
    {
        // Only one update is kept, remove what earlier downloads left behind
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        Directory.CreateDirectory(directory);

        // The name comes from GitHub, don't let it point outside the directory
        var filePath = Path.Combine(directory, Path.GetFileName(asset.Name));
        var client = httpClient ?? HttpClient;
        using (var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(filePath);
            var buffer = new byte[81920];
            long received = 0;
            var lastPercent = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total > 0)
                {
                    var percent = (int)Math.Min(100, received * 100 / total);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Invoke(percent);
                    }
                }
            }
        }

        var length = new FileInfo(filePath).Length;
        if (asset.Size > 0 && length != asset.Size)
        {
            File.Delete(filePath);
            throw new InvalidDataException($"The download of {asset.Name} has {length} bytes, the release says {asset.Size}");
        }
        if (!await MatchesDigestAsync(filePath, asset.Digest, cancellationToken))
        {
            File.Delete(filePath);
            throw new InvalidDataException($"The checksum of {asset.Name} doesn't match {asset.Digest}");
        }
        return filePath;
    }

    /// <summary>
    /// Checks a file against a GitHub asset digest ("sha256:hex"). Old releases have no digest, those pass.
    /// </summary>
    internal static async Task<bool> MatchesDigestAsync(string filePath, string? digest, CancellationToken cancellationToken = default)
    {
        const string sha256Prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith(sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), digest[sha256Prefix.Length..].Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Picks the Android APK of a release that matches the processor architecture.
    /// The release workflow names them StripWolf-Android-arm64-{version}.apk and StripWolf-Android-x86_64-{version}.apk.
    /// </summary>
    public static GitHubReleaseAsset? SelectAndroidApkAsset(IReadOnlyList<GitHubReleaseAsset> assets, Architecture architecture)
    {
        var abi = architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x86_64",
            _ => null
        };
        if (abi is null)
        {
            return null;
        }

        var prefix = $"StripWolf-Android-{abi}-";
        return assets.FirstOrDefault(a =>
            a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Navigates to the GitHub releases page
    /// </summary>
    public void GoToReleases()
    {
        _externalLinkService.OpenGitHubReleases();
    }
}
