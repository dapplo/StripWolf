using System.Text.Json.Serialization;

namespace StripWolf.Models.Komga;

/// <summary>
/// Represents a library from Komga
/// </summary>
public class KomgaLibrary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("root")]
    public string Root { get; set; } = string.Empty;

    [JsonPropertyName("importComicInfoBook")]
    public bool ImportComicInfoBook { get; set; }

    [JsonPropertyName("importComicInfoSeries")]
    public bool ImportComicInfoSeries { get; set; }

    [JsonPropertyName("importComicInfoCollection")]
    public bool ImportComicInfoCollection { get; set; }

    [JsonPropertyName("importComicInfoReadList")]
    public bool ImportComicInfoReadList { get; set; }

    [JsonPropertyName("importEpubBook")]
    public bool ImportEpubBook { get; set; }

    [JsonPropertyName("importEpubSeries")]
    public bool ImportEpubSeries { get; set; }

    [JsonPropertyName("importMylarSeries")]
    public bool ImportMylarSeries { get; set; }

    [JsonPropertyName("importLocalArtwork")]
    public bool ImportLocalArtwork { get; set; }

    [JsonPropertyName("importBarcodeIsbn")]
    public bool ImportBarcodeIsbn { get; set; }

    [JsonPropertyName("scanForceModifiedTime")]
    public bool ScanForceModifiedTime { get; set; }

    [JsonPropertyName("scanDeep")]
    public bool ScanDeep { get; set; }

    [JsonPropertyName("repairExtensions")]
    public bool RepairExtensions { get; set; }

    [JsonPropertyName("convertToCbz")]
    public bool ConvertToCbz { get; set; }

    [JsonPropertyName("emptyTrashAfterScan")]
    public bool EmptyTrashAfterScan { get; set; }

    [JsonPropertyName("seriesCover")]
    public string SeriesCover { get; set; } = string.Empty;

    [JsonPropertyName("hashFiles")]
    public bool HashFiles { get; set; }

    [JsonPropertyName("hashPages")]
    public bool HashPages { get; set; }

    [JsonPropertyName("analyzeFile")]
    public bool AnalyzeFile { get; set; }

    [JsonPropertyName("oneshotsDirectory")]
    public string? OneshotsDirectory { get; set; }

    [JsonPropertyName("unavailable")]
    public bool Unavailable { get; set; }
}

/// <summary>
/// Represents a series from Komga
/// </summary>
public class KomgaSeries
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    [JsonPropertyName("fileLastModified")]
    public DateTime FileLastModified { get; set; }

    [JsonPropertyName("booksCount")]
    public int BooksCount { get; set; }

    [JsonPropertyName("booksReadCount")]
    public int BooksReadCount { get; set; }

    [JsonPropertyName("booksUnreadCount")]
    public int BooksUnreadCount { get; set; }

    [JsonPropertyName("booksInProgressCount")]
    public int BooksInProgressCount { get; set; }

    [JsonPropertyName("metadata")]
    public KomgaSeriesMetadata? Metadata { get; set; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("oneshot")]
    public bool Oneshot { get; set; }

    /// <summary>
    /// Gets the thumbnail URL for the series (computed from the server URL)
    /// </summary>
    [JsonIgnore]
    public string? ThumbnailUrl { get; set; }
}

/// <summary>
/// Series metadata from Komga
/// </summary>
public class KomgaSeriesMetadata
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("statusLock")]
    public bool StatusLock { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("titleLock")]
    public bool TitleLock { get; set; }

    [JsonPropertyName("titleSort")]
    public string TitleSort { get; set; } = string.Empty;

    [JsonPropertyName("titleSortLock")]
    public bool TitleSortLock { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("summaryLock")]
    public bool SummaryLock { get; set; }

    [JsonPropertyName("readingDirection")]
    public string? ReadingDirection { get; set; }

    [JsonPropertyName("readingDirectionLock")]
    public bool ReadingDirectionLock { get; set; }

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = string.Empty;

    [JsonPropertyName("publisherLock")]
    public bool PublisherLock { get; set; }

    [JsonPropertyName("ageRating")]
    public int? AgeRating { get; set; }

    [JsonPropertyName("ageRatingLock")]
    public bool AgeRatingLock { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("languageLock")]
    public bool LanguageLock { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = [];

    [JsonPropertyName("genresLock")]
    public bool GenresLock { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("tagsLock")]
    public bool TagsLock { get; set; }

    [JsonPropertyName("totalBookCount")]
    public int? TotalBookCount { get; set; }

    [JsonPropertyName("totalBookCountLock")]
    public bool TotalBookCountLock { get; set; }

    [JsonPropertyName("sharingLabels")]
    public List<string> SharingLabels { get; set; } = [];

    [JsonPropertyName("sharingLabelsLock")]
    public bool SharingLabelsLock { get; set; }

    [JsonPropertyName("links")]
    public List<KomgaWebLink> Links { get; set; } = [];

    [JsonPropertyName("linksLock")]
    public bool LinksLock { get; set; }

    [JsonPropertyName("alternateTitles")]
    public List<KomgaAlternateTitle> AlternateTitles { get; set; } = [];

    [JsonPropertyName("alternateTitlesLock")]
    public bool AlternateTitlesLock { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Represents a book (comic) from Komga
/// </summary>
public class KomgaBook
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("seriesId")]
    public string SeriesId { get; set; } = string.Empty;

    [JsonPropertyName("seriesTitle")]
    public string SeriesTitle { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public float Number { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    [JsonPropertyName("fileLastModified")]
    public DateTime FileLastModified { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("size")]
    public string Size { get; set; } = string.Empty;

    [JsonPropertyName("media")]
    public KomgaMedia? Media { get; set; }

    [JsonPropertyName("metadata")]
    public KomgaBookMetadata? Metadata { get; set; }

    [JsonPropertyName("readProgress")]
    public KomgaReadProgress? ReadProgress { get; set; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("fileHash")]
    public string FileHash { get; set; } = string.Empty;

    [JsonPropertyName("oneshot")]
    public bool Oneshot { get; set; }

    /// <summary>
    /// Gets the thumbnail URL for the book (computed from the server URL)
    /// </summary>
    [JsonIgnore]
    public string? ThumbnailUrl { get; set; }
}

/// <summary>
/// Book metadata from Komga
/// </summary>
public class KomgaBookMetadata
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("titleLock")]
    public bool TitleLock { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("summaryLock")]
    public bool SummaryLock { get; set; }

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("numberLock")]
    public bool NumberLock { get; set; }

    [JsonPropertyName("numberSort")]
    public float NumberSort { get; set; }

    [JsonPropertyName("numberSortLock")]
    public bool NumberSortLock { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("releaseDateLock")]
    public bool ReleaseDateLock { get; set; }

    [JsonPropertyName("authors")]
    public List<KomgaAuthor> Authors { get; set; } = [];

    [JsonPropertyName("authorsLock")]
    public bool AuthorsLock { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("tagsLock")]
    public bool TagsLock { get; set; }

    [JsonPropertyName("isbn")]
    public string Isbn { get; set; } = string.Empty;

    [JsonPropertyName("isbnLock")]
    public bool IsbnLock { get; set; }

    [JsonPropertyName("links")]
    public List<KomgaWebLink> Links { get; set; } = [];

    [JsonPropertyName("linksLock")]
    public bool LinksLock { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Media information from Komga
/// </summary>
public class KomgaMedia
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("pagesCount")]
    public int PagesCount { get; set; }

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;

    [JsonPropertyName("epubDivinaCompatible")]
    public bool EpubDivinaCompatible { get; set; }

    [JsonPropertyName("pagesSavedState")]
    public string PagesSavedState { get; set; } = string.Empty;
}

/// <summary>
/// Read progress from Komga
/// </summary>
public class KomgaReadProgress
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("readDate")]
    public DateTime? ReadDate { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;
}

/// <summary>
/// Author information from Komga
/// </summary>
public class KomgaAuthor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Web link from Komga
/// </summary>
public class KomgaWebLink
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Alternate title from Komga
/// </summary>
public class KomgaAlternateTitle
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Paginated response from Komga
/// </summary>
public class KomgaPage<T>
{
    [JsonPropertyName("content")]
    public List<T> Content { get; set; } = [];

    [JsonPropertyName("pageable")]
    public KomgaPageable? Pageable { get; set; }

    [JsonPropertyName("totalElements")]
    public int TotalElements { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("numberOfElements")]
    public int NumberOfElements { get; set; }

    [JsonPropertyName("first")]
    public bool First { get; set; }

    [JsonPropertyName("last")]
    public bool Last { get; set; }

    [JsonPropertyName("empty")]
    public bool Empty { get; set; }
}

/// <summary>
/// Pageable information from Komga
/// </summary>
public class KomgaPageable
{
    [JsonPropertyName("sort")]
    public KomgaSort? Sort { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("paged")]
    public bool Paged { get; set; }

    [JsonPropertyName("unpaged")]
    public bool Unpaged { get; set; }
}

/// <summary>
/// Sort information from Komga
/// </summary>
public class KomgaSort
{
    [JsonPropertyName("sorted")]
    public bool Sorted { get; set; }

    [JsonPropertyName("unsorted")]
    public bool Unsorted { get; set; }

    [JsonPropertyName("empty")]
    public bool Empty { get; set; }
}

/// <summary>
/// Page (single page of a book) from Komga
/// </summary>
public class KomgaPageInfo
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("size")]
    public string Size { get; set; } = string.Empty;
}

/// <summary>
/// Represents a read list from Komga
/// </summary>
public class KomgaReadList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("ordered")]
    public bool Ordered { get; set; }

    [JsonPropertyName("bookIds")]
    public List<string> BookIds { get; set; } = [];

    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; set; }

    [JsonPropertyName("lastModifiedDate")]
    public DateTime LastModifiedDate { get; set; }

    [JsonPropertyName("filtered")]
    public bool Filtered { get; set; }
}
