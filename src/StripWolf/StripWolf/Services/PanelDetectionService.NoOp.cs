using StripWolf.Models;

namespace StripWolf.Services;

/// <summary>
/// AOT publish fallback when OpenCV panel detection is excluded from the build.
/// Guided reading still works, but every page is treated as a single splash panel.
/// </summary>
public class PanelDetectionService
{
    private readonly Dictionary<string, Dictionary<int, PagePanelInfo>> _cache = new();
    private readonly object _cacheLock = new();

    public bool IsAvailable => false;

    public Task<PagePanelInfo> DetectPanelsAsync(string comicFilePath, int pageIndex, byte[] pageData, bool isManga = false)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(comicFilePath, out var pageCache) &&
                pageCache.TryGetValue(pageIndex, out var cached))
            {
                return Task.FromResult(cached);
            }
        }

        var result = CreateFallbackResult(pageIndex);

        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(comicFilePath, out var pageCache))
            {
                pageCache = new Dictionary<int, PagePanelInfo>();
                _cache[comicFilePath] = pageCache;
            }

            pageCache[pageIndex] = result;
        }

        return Task.FromResult(result);
    }

    public async Task PreDetectPagesAsync(string comicFilePath, IEnumerable<(int pageIndex, byte[] pageData)> pages, bool isManga = false)
    {
        foreach (var (pageIndex, pageData) in pages)
        {
            await DetectPanelsAsync(comicFilePath, pageIndex, pageData, isManga);
        }
    }

    public void ClearCache(string comicFilePath)
    {
        lock (_cacheLock)
        {
            _cache.Remove(comicFilePath);
        }
    }

    public void ClearAllCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    public bool IsCached(string comicFilePath, int pageIndex)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(comicFilePath, out var pageCache) &&
                   pageCache.ContainsKey(pageIndex);
        }
    }

    private static PagePanelInfo CreateFallbackResult(int pageIndex)
    {
        return new PagePanelInfo
        {
            PageIndex = pageIndex,
            DetectionSuccessful = true,
            IsSplashPage = true,
            Panels =
            [
                new ComicPanel
                {
                    PageIndex = pageIndex,
                    PanelIndex = 0,
                    X = 0,
                    Y = 0,
                    Width = 1,
                    Height = 1,
                    Confidence = 1
                }
            ]
        };
    }
}
