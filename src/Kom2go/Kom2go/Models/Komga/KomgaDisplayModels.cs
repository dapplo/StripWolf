using Avalonia.Media.Imaging;

namespace Kom2go.Models.Komga;

/// <summary>
/// Display model for a Komga series with pre-loaded thumbnail
/// </summary>
public class KomgaSeriesDisplay
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
}

/// <summary>
/// Display model for a Komga book with pre-loaded thumbnail
/// </summary>
public class KomgaBookDisplay
{
    /// <summary>
    /// The underlying Komga book data
    /// </summary>
    public KomgaBook Book { get; set; } = new();

    /// <summary>
    /// Pre-loaded thumbnail bitmap
    /// </summary>
    public Bitmap? Thumbnail { get; set; }

    // Convenience properties for binding
    public string Id => Book.Id;
    public string Name => Book.Name;
    public int? PagesCount => Book.Media?.PagesCount;
}
