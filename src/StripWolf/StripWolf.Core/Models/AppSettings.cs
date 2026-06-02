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

namespace StripWolf.Core.Models;

/// <summary>
/// Application settings model
/// </summary>
public class AppSettings
{
    public List<KomgaServer> Servers { get; set; } = [];

    public int? ActiveServerId { get; set; }

    public string? LastOpenedComicPath { get; set; }

    public int? LastOpenedComicId { get; set; }

    public string? ComicsDirectory { get; set; }

    /// <summary>
    /// The preferred language code (e.g., "en", "de", "fr"), or null for system default
    /// </summary>
    public string? LanguageCode { get; set; }

    /// <summary>
    /// Whether to use the system language setting
    /// </summary>
    public bool UseSystemLanguage { get; set; } = true;

    /// <summary>
    /// Behavior when the application starts.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<StartupBehavior>))]
    public StartupBehavior StartupBehavior { get; set; } = StartupBehavior.ContinueWhereLeftOff;

    public bool WasInReader { get; set; }

    public int LastTabIndex { get; set; }

    /// <summary>
    /// Preferred reading mode for the comic reader
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ReadingMode>))]
    public ReadingMode PreferredReadingMode { get; set; } = ReadingMode.Normal;

    /// <summary>
    /// Handedness preference for zoomed/guided reading layout
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Handedness>))]
    public Handedness Handedness { get; set; } = Handedness.RightHanded;

    /// <summary>
    /// Preferred reading direction mode for page order and next/previous controls.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ReadingDirectionMode>))]
    public ReadingDirectionMode PreferredReadingDirectionMode { get; set; } = ReadingDirectionMode.Automatic;

    /// <summary>
    /// Default zoom region size for zoomed reading mode (0.1 to 0.8)
    /// </summary>
    public double DefaultZoomRegionSize { get; set; } = 0.3;

    /// <summary>
    /// Whether to use compact overview in zoomed/guided reading mode (saves screen space)
    /// </summary>
    public bool CompactOverview { get; set; } = false;

    /// <summary>
    /// Whether to automatically enter fullscreen mode when opening a comic
    /// </summary>
    public bool UseFullScreenWhenReading { get; set; } = false;

    /// <summary>
    /// Theme to use for the application UI.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AppThemePreference>))]
    public AppThemePreference AppTheme { get; set; } = AppThemePreference.System;

    /// <summary>
    /// Theme to use when converting EPUB pages into rendered images.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EpubConversionTheme>))]
    public EpubConversionTheme EpubConversionTheme { get; set; } = EpubConversionTheme.System;

    /// <summary>
    /// Output resolution to use when converting EPUB pages into rendered images.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EpubOutputResolution>))]
    public EpubOutputResolution EpubOutputResolution { get; set; } = EpubOutputResolution.Low;

    /// <summary>
    /// Controls whether unsupported formats are converted up front or rendered page-by-page while reading.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UnsupportedFormatHandlingMode>))]
    public UnsupportedFormatHandlingMode UnsupportedFormatHandlingMode { get; set; } = UnsupportedFormatHandlingMode.ConvertOnImport;

    /// <summary>
    /// When enabled, deleting an external comic skips the confirmation and only removes it from the library.
    /// </summary>
    public bool SkipExternalDeleteConfirmation { get; set; }

    public List<SectionLayoutSettings> LibrarySections { get; set; } = SectionLayoutSettings.CreateDefaultLibrarySections();

    public List<SectionLayoutSettings> KomgaSections { get; set; } = SectionLayoutSettings.CreateDefaultKomgaSections();

    public int KomgaParallelDownloads { get; set; } = 1;

    public bool AllowMeteredKomgaDownloads { get; set; }

    /// <summary>
    /// Number of books to load per page when browsing a Komga series
    /// </summary>
    public int KomgaSeriesPageSize { get; set; } = 20;

    /// <summary>
    /// Maximum number of search results (series and books) returned from Komga search
    /// </summary>
    public int KomgaSearchLimit { get; set; } = 10;

    /// <summary>
    /// Number of items shown in Keep Reading, On Deck, Recently Added Books and Recently Added Series panels
    /// </summary>
    public int KomgaSmartListSize { get; set; } = 10;

    /// <summary>
    /// Whether to synchronize reading progress with Komga
    /// </summary>
    public bool SyncReadProgress { get; set; } = true;

    /// <summary>
    /// Creates a deep copy of the settings
    /// </summary>
    public AppSettings Clone()
    {
        return new AppSettings
        {
            Servers = Servers.Select(s => new KomgaServer
            {
                Id = s.Id,
                Name = s.Name,
                BaseUrl = s.BaseUrl,
                Username = s.Username,
                Password = s.Password,
                ApiKey = s.ApiKey,
                CustomHeaders = s.CustomHeaders.Select(h => new KomgaHeader { Name = h.Name, Value = h.Value }).ToList(),
                LastConnected = s.LastConnected
            }).ToList(),
            ActiveServerId = ActiveServerId,
            LastOpenedComicPath = LastOpenedComicPath,
            LastOpenedComicId = LastOpenedComicId,
            ComicsDirectory = ComicsDirectory,
            LanguageCode = LanguageCode,
            UseSystemLanguage = UseSystemLanguage,
            StartupBehavior = StartupBehavior,
            WasInReader = WasInReader,
            LastTabIndex = LastTabIndex,
            AppTheme = AppTheme,
            PreferredReadingMode = PreferredReadingMode,
            Handedness = Handedness,
            PreferredReadingDirectionMode = PreferredReadingDirectionMode,
            DefaultZoomRegionSize = DefaultZoomRegionSize,
            CompactOverview = CompactOverview,
            UseFullScreenWhenReading = UseFullScreenWhenReading,
            EpubConversionTheme = EpubConversionTheme,
            EpubOutputResolution = EpubOutputResolution,
            UnsupportedFormatHandlingMode = UnsupportedFormatHandlingMode,
            SkipExternalDeleteConfirmation = SkipExternalDeleteConfirmation,
            LibrarySections = LibrarySections.Select(section => section.Clone()).ToList(),
            KomgaSections = KomgaSections.Select(section => section.Clone()).ToList(),
            KomgaParallelDownloads = KomgaParallelDownloads,
            AllowMeteredKomgaDownloads = AllowMeteredKomgaDownloads,
            KomgaSeriesPageSize = KomgaSeriesPageSize,
            KomgaSearchLimit = KomgaSearchLimit,
            KomgaSmartListSize = KomgaSmartListSize,
            SyncReadProgress = SyncReadProgress
        };
    }
}
