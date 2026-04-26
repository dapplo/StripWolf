using SQLite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Models;

/// <summary>
/// Represents a comic book in the local library
/// </summary>
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
    private string _filePath = string.Empty;

    /// <summary>
    /// Total number of pages in the comic
    /// </summary>
    [ObservableProperty]
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
    /// Current page the user is on (0-indexed)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingProgress))]
    private int _currentPage;

    /// <summary>
    /// Whether the comic has been read completely
    /// </summary>
    [ObservableProperty]
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

    /// <summary>
    /// Format of the comic file (CBZ, CBR)
    /// </summary>
    [ObservableProperty]
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
    private bool _isDeleting;

    /// <summary>
    /// Seconds remaining for undoing deletion
    /// </summary>
    [property: SQLite.Ignore]
    [ObservableProperty]
    private int _deletionSecondsRemaining;
}

public enum ComicFormat
{
    Unknown,
    Cbz,
    Cbr,
    Cb7,
    Cbt,
    Pdf
}

public enum ComicSource
{
    Local,
    Komga
}
