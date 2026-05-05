using StripWolf.Models;

namespace StripWolf.Services;

/// <summary>
/// Import-ready comic metadata captured during a single conversion or archive inspection pass.
/// </summary>
public sealed class ComicImportData : IDisposable
{
    public required string FilePath { get; init; }

    public required ComicFormat Format { get; init; }

    public ComicInfo? ComicInfo { get; init; }

    public int PageCount { get; init; }

    public long FileSize { get; init; }

    public Stream? CoverImageStream { get; init; }

    public void Dispose()
    {
        CoverImageStream?.Dispose();
    }
}
