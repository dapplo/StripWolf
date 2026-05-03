using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StripWolf.Models.Komga;

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
/// Display model for a Komga book with pre-loaded thumbnail and download status
/// </summary>
public partial class KomgaBookDisplay : ObservableObject
{
    /// <summary>
    /// The underlying Komga book data
    /// </summary>
    public KomgaBook Book { get; set; } = new();

    /// <summary>
    /// Pre-loaded thumbnail bitmap
    /// </summary>
    public Bitmap? Thumbnail { get; set; }

    [ObservableProperty]
    private bool _isQueued;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private double _downloadProgress;

    // Convenience properties for binding
    public string Id => Book.Id;
    public string Name => Book.Name;
    public int? PagesCount => Book.Media?.PagesCount;

    // Reading progress properties
    public bool IsRead => Book.ReadProgress?.Completed ?? false;
    public bool IsReading => Book.ReadProgress != null && !IsRead;
    public int? CurrentPage => Book.ReadProgress?.Page;
    
    public double ReadingProgress => (PagesCount > 0 && CurrentPage.HasValue) 
        ? (double)CurrentPage.Value / PagesCount.Value 
        : 0;
}

/// <summary>
/// Display model for a Komga read list with pre-loaded thumbnail
/// </summary>
public class KomgaReadListDisplay
{
    /// <summary>
    /// The underlying Komga read list data
    /// </summary>
    public KomgaReadList ReadList { get; set; } = new();

    /// <summary>
    /// Pre-loaded thumbnail bitmap
    /// </summary>
    public Bitmap? Thumbnail { get; set; }

    // Convenience properties for binding
    public string Id => ReadList.Id;
    public string Name => ReadList.Name;
    public int BookCount => ReadList.BookIds.Count;
}
