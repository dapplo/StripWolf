namespace StripWolf.Services;

/// <summary>
/// Converts EPUB files into CBZ archives by rendering each paginated HTML viewport to an image.
/// </summary>
public interface IEpubToCbzConverter
{
    /// <summary>
    /// Converts an EPUB file to a CBZ archive.
    /// </summary>
    Task<string> ConvertEpubToCbzAsync(
        string epubFilePath,
        string outputDirectory,
        int viewportWidth = 700,
        int viewportHeight = 1050,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
