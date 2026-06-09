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

namespace StripWolf.Core.Models.Komga;

/// <summary>
/// Display model for a Komga book with pre-loaded thumbnail and download status
/// </summary>
public partial class KomgaBookDisplay : ObservableObject
{
    /// <summary>
    /// The underlying Komga book data
    /// </summary>
    public KomgaBook Book { get; set; } = new();

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isQueued;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isCancelling;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private double _downloadProgress;

    // Convenience properties for binding
    public string Id => Book.Id;
    public string Name => Book.Name;
    public string SeriesTitle => Book.SeriesTitle;
    public int? PagesCount => Book.Media?.PagesCount;
    public string Summary => Book.Metadata?.Summary ?? string.Empty;
    public string ReleaseDate => Book.Metadata?.ReleaseDate ?? string.Empty;
    public string NumberLabel => !string.IsNullOrWhiteSpace(Book.Metadata?.Number) ? Book.Metadata.Number : Book.Number.ToString("0.##");
    public string AuthorsDisplay => Book.Metadata?.Authors is { Count: > 0 }
        ? string.Join(", ", Book.Metadata.Authors.Select(author => string.IsNullOrWhiteSpace(author.Role) ? author.Name : $"{author.Name} ({author.Role})"))
        : string.Empty;
 
    // Reading progress properties
    public bool IsRead => Book.ReadProgress?.Completed ?? false;
    public bool IsReading => Book.ReadProgress != null && !IsRead;
    public int? CurrentPage => Book.ReadProgress?.Page;
    
    public double ReadingProgress => (PagesCount > 0 && CurrentPage.HasValue) 
        ? (double)CurrentPage.Value / PagesCount.Value 
        : 0;

    /// <summary>
    /// Human-readable file size from Komga (e.g. "14.5 MB")
    /// </summary>
    public string FileSize => Book.Size;

    /// <summary>
    /// Display format derived from the media MIME type (e.g. "CBZ", "PDF")
    /// </summary>
    public string Format => Book.Media?.MediaType switch
    {
        "application/x-cbz"  => "CBZ",
        "application/x-cbr"  => "CBR",
        "application/x-cb7"  => "CB7",
        "application/x-cbt"  => "CBT",
        "application/pdf"    => "PDF",
        "application/epub+zip" => "EPUB",
        var t when !string.IsNullOrEmpty(t) => t.Split('/').Last().ToUpperInvariant(),
        _ => string.Empty
    };

    public void RefreshComputedProperties()
    {
        OnPropertyChanged(nameof(IsRead));
        OnPropertyChanged(nameof(IsReading));
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ReadingProgress));
    }
}

