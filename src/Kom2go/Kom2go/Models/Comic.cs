using SQLite;

namespace Kom2go.Models;

/// <summary>
/// Represents a comic book in the local library
/// </summary>
public class Comic
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// The Komga book ID if this comic was downloaded from Komga
    /// </summary>
    public string? KomgaId { get; set; }

    /// <summary>
    /// Title of the comic
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Series name
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Issue number in the series
    /// </summary>
    public float? Number { get; set; }

    /// <summary>
    /// Summary or description of the comic
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Publisher name
    /// </summary>
    public string? Publisher { get; set; }

    /// <summary>
    /// Authors (comma-separated)
    /// </summary>
    public string? Authors { get; set; }

    /// <summary>
    /// Release date
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// File path on the device
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Total number of pages in the comic
    /// </summary>
    public int PageCount { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Cover image path (cached locally)
    /// </summary>
    public string? CoverPath { get; set; }

    /// <summary>
    /// When the comic was added to the library
    /// </summary>
    public DateTime AddedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time this comic was opened
    /// </summary>
    public DateTime? LastReadDate { get; set; }

    /// <summary>
    /// Current page the user is on (0-indexed)
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Whether the comic has been read completely
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Whether the comic is marked as a favorite
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Reading progress as a value between 0 and 1
    /// </summary>
    [SQLite.Ignore]
    public double ReadingProgress => PageCount > 0 ? (double)CurrentPage / PageCount : 0;

    /// <summary>
    /// Format of the comic file (CBZ, CBR)
    /// </summary>
    public ComicFormat Format { get; set; }

    /// <summary>
    /// Source of the comic (Local or Komga)
    /// </summary>
    public ComicSource Source { get; set; }
}

public enum ComicFormat
{
    Unknown,
    Cbz,
    Cbr,
    Pdf
}

public enum ComicSource
{
    Local,
    Komga
}
