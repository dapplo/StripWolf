namespace StripWolf.Services;

/// <summary>
/// Captures a rendered snapshot of HTML content using a platform-native off-screen WebView.
/// </summary>
public interface IWebViewSnapshotService
{
    /// <summary>
    /// Renders the supplied HTML and captures the current viewport as a PNG stream.
    /// </summary>
    Task<Stream> CapturePageAsync(string htmlContent, int viewportWidth, int viewportHeight);
}

/// <summary>
/// Extended WebView snapshot contract used by EPUB pagination to query paged layout information.
/// </summary>
public interface IWebViewPaginationService : IWebViewSnapshotService
{
    /// <summary>
    /// Renders the supplied HTML and returns the total number of paged columns.
    /// </summary>
    Task<int> GetPageCountAsync(string htmlContent, int viewportWidth, int viewportHeight);
}
