using SQLite;

namespace StripWolf.Models;

public class EpubConversionState
{
    [PrimaryKey]
    public int ComicId { get; set; }

    [Indexed]
    public string SourceEpubPath { get; set; } = string.Empty;

    public string ShadowPath { get; set; } = string.Empty;

    public EpubConversionStatus Status { get; set; }

    public int ProducedPageCount { get; set; }

    public int? FinalPageCount { get; set; }

    public int NextChapterIndex { get; set; }

    public int NextPageIndexInChapter { get; set; }

    public string PaginationSignature { get; set; } = string.Empty;

    public string? LastError { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum EpubConversionStatus
{
    Pending,
    Converting,
    Paused,
    Completed,
    Failed
}
