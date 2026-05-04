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
/// Represents a reusable paginated HTML session that can load documents and capture multiple pages.
/// </summary>
public interface IWebViewPaginationSession : IAsyncDisposable
{
    /// <summary>
    /// Loads the supplied HTML into the existing native WebView session.
    /// </summary>
    Task LoadHtmlAsync(string htmlContent);

    /// <summary>
    /// Returns the number of paginated pages in the loaded document.
    /// </summary>
    Task<int> GetPageCountAsync();

    /// <summary>
    /// Captures the specified zero-based page index from the loaded document directly into the supplied stream.
    /// </summary>
    Task CapturePageToStreamAsync(int pageIndex, Stream outputStream);

    /// <summary>
    /// Captures the specified zero-based page index from the loaded document.
    /// </summary>
    Task<Stream> CapturePageAsync(int pageIndex);
}

/// <summary>
/// Extended WebView snapshot contract used by EPUB pagination to query paged layout information.
/// </summary>
public interface IWebViewPaginationService : IWebViewSnapshotService
{
    /// <summary>
    /// Creates an empty reusable pagination session for the supplied viewport.
    /// </summary>
    Task<IWebViewPaginationSession> CreatePaginationSessionAsync(int viewportWidth, int viewportHeight, double renderScale = 1);

    /// <summary>
    /// Renders the supplied HTML and returns the total number of paged columns.
    /// </summary>
    Task<int> GetPageCountAsync(string htmlContent, int viewportWidth, int viewportHeight);

    /// <summary>
    /// Loads the supplied HTML once and returns a session that can paginate and capture multiple pages.
    /// </summary>
    Task<IWebViewPaginationSession> CreatePaginationSessionAsync(string htmlContent, int viewportWidth, int viewportHeight, double renderScale = 1);
}
