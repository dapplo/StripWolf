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

    public void RefreshComputedProperties()
    {
        OnPropertyChanged(nameof(IsRead));
        OnPropertyChanged(nameof(IsReading));
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ReadingProgress));
    }
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
