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

using SQLite;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using StripWolf.Core.Resources;

namespace StripWolf.Core.Models;

/// <summary>
/// Represents a comic book in the local library
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
public partial class Comic : ObservableObject
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// The Komga book ID if this comic was downloaded from Komga
    /// </summary>
    [ObservableProperty]
    [property: Indexed]
    private string? _komgaId;

    /// <summary>
    /// The Komga file hash if this comic was downloaded from Komga
    /// </summary>
    [ObservableProperty]
    [property: Indexed]
    private string? _komgaHash;

    /// <summary>
    /// The Komga series ID if this comic was downloaded from Komga
    /// </summary>
    [ObservableProperty]
    [property: Indexed]
    private string? _komgaSeriesId;

    /// <summary>
    /// The ID of the KomgaServer this comic was downloaded from
    /// </summary>
    [ObservableProperty]
    [property: Indexed]
    private int? _komgaServerId;

    /// <summary>
    /// Title of the comic
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Series name
    /// </summary>
    [ObservableProperty]
    private string? _seriesName;

    /// <summary>
    /// Issue number in the series
    /// </summary>
    [ObservableProperty]
    private float? _number;

    /// <summary>
    /// Summary or description of the comic
    /// </summary>
    [ObservableProperty]
    private string? _summary;

    /// <summary>
    /// Publisher name
    /// </summary>
    [ObservableProperty]
    private string? _publisher;

    /// <summary>
    /// Authors (comma-separated)
    /// </summary>
    [ObservableProperty]
    private string? _authors;

    /// <summary>
    /// Release date
    /// </summary>
    [ObservableProperty]
    private DateTime? _releaseDate;

    /// <summary>
    /// File path on the device
    /// </summary>
    [ObservableProperty]
    [property: Indexed]
    [NotifyPropertyChangedFor(nameof(IsPendingEpubConversion))]
    [NotifyPropertyChangedFor(nameof(LibraryPageStatusDisplay))]
    private string _filePath = string.Empty;

    /// <summary>
    /// Total number of pages in the comic
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingProgress))]
    [NotifyPropertyChangedFor(nameof(HasReadingProgress))]
    [NotifyPropertyChangedFor(nameof(ReadingProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(LibraryPageStatusDisplay))]
    private int _pageCount;

    /// <summary>
    /// File size in bytes
    /// </summary>
    [ObservableProperty]
    private long _fileSize;

    /// <summary>
    /// Cover image path (cached locally)
    /// </summary>
    [ObservableProperty]
    private string? _coverPath;

    /// <summary>
    /// When the comic was added to the library
    /// </summary>
    [ObservableProperty]
    private DateTime _addedDate = DateTime.UtcNow;

    /// <summary>
    /// Last time this comic was opened
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastReadDate;

    /// <summary>
    /// Last time the reading progress was modified locally
    /// </summary>
    [ObservableProperty]
    private DateTime? _readProgressLastModified;

    /// <summary>
    /// The last synchronization status with Komga
    /// </summary>
    [ObservableProperty]
    private string? _komgaSyncStatus;

    /// <summary>
    /// Current page the user is on (0-indexed)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingProgress))]
    [NotifyPropertyChangedFor(nameof(HasReadingProgress))]
    [NotifyPropertyChangedFor(nameof(ReadingProgressDisplay))]
    private int _currentPage;

    /// <summary>
    /// Whether the comic has been read completely
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReadingProgress))]
    [NotifyPropertyChangedFor(nameof(ReadingProgressDisplay))]
    private bool _isCompleted;

    /// <summary>
    /// Whether the comic is marked as a favorite
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// Reading progress as a value between 0 and 1
    /// </summary>
    [SQLite.Ignore]
    public double ReadingProgress => PageCount > 0 ? (double)CurrentPage / PageCount : 0;

    [SQLite.Ignore]
    public bool HasReadingProgress => !IsCompleted && CurrentPage > 0 && PageCount > 0;

    [SQLite.Ignore]
    public string ReadingProgressDisplay => IsConverting
        ? Loc.Instance.Converting
        : IsPendingEpubConversion
            ? Loc.Instance.NotConvertedYet
            : HasReadingProgress
                ? string.Format(Loc.Instance.PagesDisplay, Math.Min(CurrentPage + 1, PageCount), PageCount)
                : string.Format(Loc.Instance.TotalPagesDisplay, PageCount);

    /// <summary>
    /// Format of the comic file (CBZ, CBR)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingEpubConversion))]
    [NotifyPropertyChangedFor(nameof(CanConvertNow))]
    [NotifyPropertyChangedFor(nameof(LibraryPageStatusDisplay))]
    private ComicFormat _format;

    /// <summary>
    /// Source of the comic (Local or Komga)
    /// </summary>
    [ObservableProperty]
    private ComicSource _source;

    /// <summary>
    /// Whether the comic is currently being deleted (in undo state)
    /// </summary>
    [property: SQLite.Ignore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    private bool _isDeleting;

    /// <summary>
    /// Seconds remaining for undoing deletion
    /// </summary>
    [property: SQLite.Ignore]
    [ObservableProperty]
    private int _deletionSecondsRemaining;

    [property: SQLite.Ignore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanConvertNow))]
    [NotifyPropertyChangedFor(nameof(IsPendingEpubConversion))]
    [NotifyPropertyChangedFor(nameof(ReadingProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(LibraryPageStatusDisplay))]
    private bool _isConverting;

    [SQLite.Ignore]
    public bool IsPendingEpubConversion =>
        !IsConverting &&
        Format == ComicFormat.Epub &&
        Path.GetExtension(FilePath).Equals(".epub", StringComparison.OrdinalIgnoreCase);

    [SQLite.Ignore]
    public bool CanOpen => !IsDeleting && !IsConverting;

    [SQLite.Ignore]
    public bool CanConvertNow => IsPendingEpubConversion && !IsConverting;

    [SQLite.Ignore]
    public string LibraryPageStatusDisplay => IsPendingEpubConversion
        ? Loc.Instance.NotConvertedYet
        : IsConverting
            ? Loc.Instance.Converting
            : string.Format(Loc.Instance.TotalPagesDisplay, PageCount);

    [SQLite.Ignore]
    public string FormatDisplay => Format.ToString().ToUpperInvariant();

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ReadingProgressDisplay));
        OnPropertyChanged(nameof(LibraryPageStatusDisplay));
        OnPropertyChanged(nameof(FormatDisplay));
    }
}

