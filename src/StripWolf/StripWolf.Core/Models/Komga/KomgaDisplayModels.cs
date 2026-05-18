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

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Models.Komga;

/// <summary>
/// Display model for a Komga series with pre-loaded thumbnail
/// </summary>
public partial class KomgaSeriesDisplay : ObservableObject
{
    /// <summary>
    /// The underlying Komga series data
    /// </summary>
    public KomgaSeries Series { get; set; } = new();

    /// <summary>
    /// Pre-loaded thumbnail bitmap
    /// </summary>
    public Bitmap? Thumbnail { get; set; }

    // Convenience properties for binding
    public string Id => Series.Id;
    public string Name => Series.Name;
    public int BooksCount => Series.BooksCount;
    public int BooksReadCount => Series.BooksReadCount;
    public string BooksSummary => BooksCount > 0
        ? $"{BooksReadCount}/{BooksCount} read"
        : $"{BooksReadCount} read";
    public string Summary => Series.Metadata?.Summary ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    private bool _isQueuedForDownload;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueuedForDownload))]
    private bool _isDownloading;
}

