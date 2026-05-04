namespace StripWolf.Services;

/// <summary>
/// Fallback implementation used on platforms where no native off-screen WebView snapshot service is registered.
/// </summary>
public sealed class UnsupportedWebViewSnapshotService : IWebViewPaginationService
{
    public Task<Stream> CapturePageAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        throw new PlatformNotSupportedException("No off-screen native WebView snapshot service is registered for this platform.");
    }

    public Task<int> GetPageCountAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        throw new PlatformNotSupportedException("No off-screen native WebView snapshot service is registered for this platform.");
    }

    public Task<IWebViewPaginationSession> CreatePaginationSessionAsync(int viewportWidth, int viewportHeight)
    {
        throw new PlatformNotSupportedException("No off-screen native WebView snapshot service is registered for this platform.");
    }

    public Task<IWebViewPaginationSession> CreatePaginationSessionAsync(string htmlContent, int viewportWidth, int viewportHeight)
    {
        throw new PlatformNotSupportedException("No off-screen native WebView snapshot service is registered for this platform.");
    }
}
