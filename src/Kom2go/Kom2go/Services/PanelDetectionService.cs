using Kom2go.Models;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace Kom2go.Services;

/// <summary>
/// Service for detecting comic panels (scenes) on comic pages.
/// Uses OpenCV for advanced image processing to find panels.
/// </summary>
public class PanelDetectionService
{
    // Cache for detected panels per comic file
    private readonly Dictionary<string, Dictionary<int, PagePanelInfo>> _cache = new();
    private readonly object _cacheLock = new();
    
    /// <summary>
    /// Minimum panel width/height ratio relative to page size
    /// </summary>
    private const double MinPanelSizeRatio = 0.04; // Slightly more lenient

    /// <summary>
    /// Detect panels on a comic page
    /// </summary>
    /// <param name="comicFilePath">Path to the comic file (used for caching)</param>
    /// <param name="pageIndex">Page index</param>
    /// <param name="pageData">Raw image data for the page</param>
    /// <param name="isManga">True if the comic should be read from right-to-left</param>
    /// <returns>Panel detection results</returns>
    public async Task<PagePanelInfo> DetectPanelsAsync(string comicFilePath, int pageIndex, byte[] pageData, bool isManga = false)
    {
        // Check cache first
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(comicFilePath, out var pageCache) &&
                pageCache.TryGetValue(pageIndex, out var cached))
            {
                return cached;
            }
        }
        
        // Perform detection
        var result = await Task.Run(() => DetectPanelsInternal(pageIndex, pageData, isManga));
        
        // Cache result
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(comicFilePath, out var pageCache))
            {
                pageCache = new Dictionary<int, PagePanelInfo>();
                _cache[comicFilePath] = pageCache;
            }
            pageCache[pageIndex] = result;
        }
        
        return result;
    }
    
    /// <summary>
    /// Pre-detect panels for multiple pages (for background processing)
    /// </summary>
    public async Task PreDetectPagesAsync(string comicFilePath, IEnumerable<(int pageIndex, byte[] pageData)> pages, bool isManga = false)
    {
        var tasks = pages.Select(p => DetectPanelsAsync(comicFilePath, p.pageIndex, p.pageData, isManga));
        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// Clear cache for a specific comic
    /// </summary>
    public void ClearCache(string comicFilePath)
    {
        lock (_cacheLock)
        {
            _cache.Remove(comicFilePath);
        }
    }
    
    /// <summary>
    /// Clear all cached panel detection results
    /// </summary>
    public void ClearAllCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }
    
    /// <summary>
    /// Check if panels are already cached for a page
    /// </summary>
    public bool IsCached(string comicFilePath, int pageIndex)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(comicFilePath, out var pageCache) &&
                   pageCache.ContainsKey(pageIndex);
        }
    }
    
    /// <summary>
    /// Internal panel detection implementation using OpenCV
    /// </summary>
    private PagePanelInfo DetectPanelsInternal(int pageIndex, byte[] pageData, bool isManga)
    {
        var result = new PagePanelInfo
        {
            PageIndex = pageIndex
        };
        
        try
        {
            using var src = Mat.FromImageData(pageData, ImreadModes.Color);
            if (src.Empty()) throw new Exception("Failed to load image");

            var imageWidth = src.Width;
            var imageHeight = src.Height;

            // 1. Grayscale
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            // 2. Bilateral Filter to smooth noise while keeping edges sharp
            using var blurred = new Mat();
            Cv2.BilateralFilter(gray, blurred, 9, 75, 75);

            // 3. Adaptive Thresholding to find structural lines (gutters/borders)
            // We use a larger block size to be more robust to internal panel detail
            using var binary = new Mat();
            Cv2.AdaptiveThreshold(blurred, binary, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.BinaryInv, 15, 3);

            // 4. Morphological Closing to fill gaps in panel borders
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            using var closed = new Mat();
            Cv2.MorphologyEx(binary, closed, MorphTypes.Close, kernel, iterations: 2);

            // 5. Morphological Dilation to further ensure borders are connected
            using var dilated = new Mat();
            Cv2.Dilate(closed, dilated, kernel, iterations: 1);

            // 6. Find contours
            // We use List mode to get all contours, then filter
            Cv2.FindContours(dilated, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            var candidatePanels = new List<ComicPanel>();
            var minWidth = imageWidth * MinPanelSizeRatio;
            var minHeight = imageHeight * MinPanelSizeRatio;
            var pageArea = imageWidth * imageHeight;

            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                var area = rect.Width * rect.Height;

                // Basic size filtering
                if (rect.Width < minWidth || rect.Height < minHeight) continue;
                if (area < pageArea * 0.01) continue; // Skip very small things

                // Filter out panels that are basically the whole page (border or background)
                if (rect.Width > imageWidth * 0.95 && rect.Height > imageHeight * 0.95) continue;

                candidatePanels.Add(new ComicPanel
                {
                    PageIndex = pageIndex,
                    X = (double)rect.X / imageWidth,
                    Y = (double)rect.Y / imageHeight,
                    Width = (double)rect.Width / imageWidth,
                    Height = (double)rect.Height / imageHeight,
                    Confidence = 0.8
                });
            }

            // 7. Remove overlapping candidates (keep the largest ones that don't fully contain others)
            var panels = FilterOverlappingPanels(candidatePanels);

            // 8. Advanced Sorting: Reading Order
            var sortedPanels = SortPanelsByReadingOrder(panels, isManga);

            // Re-index
            for (int i = 0; i < sortedPanels.Count; i++)
            {
                sortedPanels[i].PanelIndex = i;
            }

            if (sortedPanels.Count > 0)
            {
                result.Panels = sortedPanels;
                result.DetectionSuccessful = true;
                result.IsSplashPage = sortedPanels.Count == 1;
                return result;
            }

            // Fallback: entire page
            return CreateSplashPageResult(pageIndex);
        }
        catch
        {
            return CreateSplashPageResult(pageIndex, 0.5);
        }
    }

    private List<ComicPanel> FilterOverlappingPanels(List<ComicPanel> candidates)
    {
        if (candidates.Count <= 1) return candidates;

        // Sort by area descending to process larger ones first
        var sorted = candidates.OrderByDescending(p => p.Width * p.Height).ToList();
        var result = new List<ComicPanel>();

        foreach (var p in sorted)
        {
            bool isDuplicate = false;
            foreach (var existing in result)
            {
                // Check if p is almost the same as existing (within 5%)
                if (Math.Abs(p.X - existing.X) < 0.05 &&
                    Math.Abs(p.Y - existing.Y) < 0.05 &&
                    Math.Abs(p.Width - existing.Width) < 0.05 &&
                    Math.Abs(p.Height - existing.Height) < 0.05)
                {
                    isDuplicate = true;
                    break;
                }
                
                // Check if p is inside existing
                if (p.X >= existing.X - 0.01 && 
                    p.Y >= existing.Y - 0.01 && 
                    p.X + p.Width <= existing.X + existing.Width + 0.01 && 
                    p.Y + p.Height <= existing.Y + existing.Height + 0.01)
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
            {
                result.Add(p);
            }
        }

        return result;
    }

    private List<ComicPanel> SortPanelsByReadingOrder(List<ComicPanel> panels, bool isManga)
    {
        if (panels.Count <= 1) return panels;

        var result = new List<ComicPanel>();
        var remaining = panels.OrderBy(p => p.Y).ToList();

        while (remaining.Count > 0)
        {
            // Take the top-most panel
            var topPanel = remaining[0];
            
            // Find all panels that are in the "same row" as this one
            // A row is defined by vertical overlap or proximity
            var row = remaining.Where(p => 
                Math.Abs(p.Y - topPanel.Y) < 0.08 || // Close Y
                (p.Y < topPanel.Y + topPanel.Height * 0.5 && p.Y + p.Height > topPanel.Y + topPanel.Height * 0.5) // Center overlap
            ).ToList();

            // Sort row by X (Left-to-Right or Right-to-Left)
            var sortedRow = isManga 
                ? row.OrderByDescending(p => p.X).ToList() 
                : row.OrderBy(p => p.X).ToList();

            result.AddRange(sortedRow);
            
            // Remove processed panels
            foreach (var p in sortedRow)
            {
                remaining.Remove(p);
            }
        }

        return result;
    }

    private static PagePanelInfo CreateSplashPageResult(int pageIndex, double confidence = 1.0)
    {
        return new PagePanelInfo
        {
            PageIndex = pageIndex,
            DetectionSuccessful = true,
            IsSplashPage = true,
            Panels = new List<ComicPanel>
            {
                new ComicPanel
                {
                    PageIndex = pageIndex,
                    PanelIndex = 0,
                    X = 0,
                    Y = 0,
                    Width = 1,
                    Height = 1,
                    Confidence = confidence
                }
            }
        };
    }
}
