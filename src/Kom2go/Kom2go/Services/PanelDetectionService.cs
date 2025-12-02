using Kom2go.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Kom2go.Services;

/// <summary>
/// Service for detecting comic panels (scenes) on comic pages.
/// Uses image processing to find rectangular regions separated by gutters.
/// </summary>
public class PanelDetectionService
{
    // Cache for detected panels per comic file
    private readonly Dictionary<string, Dictionary<int, PagePanelInfo>> _cache = new();
    private readonly object _cacheLock = new();
    
    /// <summary>
    /// Minimum panel width/height ratio relative to page size
    /// </summary>
    private const double MinPanelSizeRatio = 0.08;
    
    /// <summary>
    /// Minimum gutter width to consider as separation between panels (as ratio of page width)
    /// </summary>
    private const double MinGutterRatio = 0.005;
    
    /// <summary>
    /// Maximum gutter width (as ratio of page width)
    /// </summary>
    private const double MaxGutterRatio = 0.05;
    
    /// <summary>
    /// Brightness threshold for detecting white/light gutters (0-255)
    /// </summary>
    private const byte GutterBrightnessThreshold = 230;
    
    /// <summary>
    /// Detect panels on a comic page
    /// </summary>
    /// <param name="comicFilePath">Path to the comic file (used for caching)</param>
    /// <param name="pageIndex">Page index</param>
    /// <param name="pageData">Raw image data for the page</param>
    /// <returns>Panel detection results</returns>
    public async Task<PagePanelInfo> DetectPanelsAsync(string comicFilePath, int pageIndex, byte[] pageData)
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
        var result = await Task.Run(() => DetectPanelsInternal(pageIndex, pageData));
        
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
    public async Task PreDetectPagesAsync(string comicFilePath, IEnumerable<(int pageIndex, byte[] pageData)> pages)
    {
        var tasks = pages.Select(p => DetectPanelsAsync(comicFilePath, p.pageIndex, p.pageData));
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
    /// Internal panel detection implementation
    /// </summary>
    private PagePanelInfo DetectPanelsInternal(int pageIndex, byte[] pageData)
    {
        var result = new PagePanelInfo
        {
            PageIndex = pageIndex
        };
        
        try
        {
            using var image = Image.Load<Rgba32>(pageData);
            
            var imageWidth = image.Width;
            var imageHeight = image.Height;
            
            // Convert to grayscale for processing
            var grayImage = ConvertToGrayscale(image);
            
            // Find horizontal and vertical gutters (white/light lines)
            var horizontalGutters = FindHorizontalGutters(grayImage, imageWidth, imageHeight);
            var verticalGutters = FindVerticalGutters(grayImage, imageWidth, imageHeight);
            
            // If we found meaningful gutters, extract panels
            if (horizontalGutters.Count > 0 || verticalGutters.Count > 0)
            {
                var panels = ExtractPanelsFromGutters(
                    horizontalGutters, 
                    verticalGutters, 
                    imageWidth, 
                    imageHeight,
                    pageIndex);
                
                if (panels.Count > 0)
                {
                    result.Panels = panels;
                    result.DetectionSuccessful = true;
                    result.IsSplashPage = panels.Count == 1;
                    return result;
                }
            }
            
            // Fallback: if no panels detected, treat entire page as one panel
            result.IsSplashPage = true;
            result.DetectionSuccessful = true;
            result.Panels.Add(new ComicPanel
            {
                PageIndex = pageIndex,
                PanelIndex = 0,
                X = 0,
                Y = 0,
                Width = 1,
                Height = 1,
                Confidence = 1.0
            });
        }
        catch
        {
            // On error, treat page as splash page
            result.DetectionSuccessful = false;
            result.IsSplashPage = true;
            result.Panels.Add(new ComicPanel
            {
                PageIndex = pageIndex,
                PanelIndex = 0,
                X = 0,
                Y = 0,
                Width = 1,
                Height = 1,
                Confidence = 0.5
            });
        }
        
        return result;
    }
    
    /// <summary>
    /// Convert image to grayscale byte array
    /// </summary>
    private byte[,] ConvertToGrayscale(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;
        var gray = new byte[height, width];
        
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var pixel = row[x];
                    // Standard grayscale conversion
                    gray[y, x] = (byte)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
                }
            }
        });
        
        return gray;
    }
    
    /// <summary>
    /// Find horizontal gutters (white/light horizontal bands)
    /// </summary>
    private List<(int start, int end)> FindHorizontalGutters(byte[,] grayImage, int width, int height)
    {
        var gutters = new List<(int start, int end)>();
        var minGutterHeight = (int)(height * MinGutterRatio);
        var maxGutterHeight = (int)(height * MaxGutterRatio);
        
        // Check for the minimum gutter size to be at least 2 pixels
        if (minGutterHeight < 2) minGutterHeight = 2;
        if (maxGutterHeight < minGutterHeight) maxGutterHeight = minGutterHeight + 1;
        
        // Sample columns to check (avoid edge artifacts)
        var sampleStartX = width / 10;
        var sampleEndX = width - width / 10;
        var sampleWidth = sampleEndX - sampleStartX;
        
        var inGutter = false;
        var gutterStart = 0;
        
        for (var y = 0; y < height; y++)
        {
            // Count light pixels in this row
            var lightPixelCount = 0;
            for (var x = sampleStartX; x < sampleEndX; x++)
            {
                if (grayImage[y, x] >= GutterBrightnessThreshold)
                {
                    lightPixelCount++;
                }
            }
            
            // If most of the row is light, it's part of a gutter
            var isLightRow = lightPixelCount > sampleWidth * 0.85;
            
            if (isLightRow && !inGutter)
            {
                gutterStart = y;
                inGutter = true;
            }
            else if (!isLightRow && inGutter)
            {
                var gutterHeight = y - gutterStart;
                if (gutterHeight >= minGutterHeight && gutterHeight <= maxGutterHeight)
                {
                    gutters.Add((gutterStart, y));
                }
                inGutter = false;
            }
        }
        
        return gutters;
    }
    
    /// <summary>
    /// Find vertical gutters (white/light vertical bands)
    /// </summary>
    private List<(int start, int end)> FindVerticalGutters(byte[,] grayImage, int width, int height)
    {
        var gutters = new List<(int start, int end)>();
        var minGutterWidth = (int)(width * MinGutterRatio);
        var maxGutterWidth = (int)(width * MaxGutterRatio);
        
        // Check for the minimum gutter size to be at least 2 pixels
        if (minGutterWidth < 2) minGutterWidth = 2;
        if (maxGutterWidth < minGutterWidth) maxGutterWidth = minGutterWidth + 1;
        
        // Sample rows to check (avoid edge artifacts)
        var sampleStartY = height / 10;
        var sampleEndY = height - height / 10;
        var sampleHeight = sampleEndY - sampleStartY;
        
        var inGutter = false;
        var gutterStart = 0;
        
        for (var x = 0; x < width; x++)
        {
            // Count light pixels in this column
            var lightPixelCount = 0;
            for (var y = sampleStartY; y < sampleEndY; y++)
            {
                if (grayImage[y, x] >= GutterBrightnessThreshold)
                {
                    lightPixelCount++;
                }
            }
            
            // If most of the column is light, it's part of a gutter
            var isLightColumn = lightPixelCount > sampleHeight * 0.85;
            
            if (isLightColumn && !inGutter)
            {
                gutterStart = x;
                inGutter = true;
            }
            else if (!isLightColumn && inGutter)
            {
                var gutterWidth = x - gutterStart;
                if (gutterWidth >= minGutterWidth && gutterWidth <= maxGutterWidth)
                {
                    gutters.Add((gutterStart, x));
                }
                inGutter = false;
            }
        }
        
        return gutters;
    }
    
    /// <summary>
    /// Extract panel regions from detected gutters
    /// </summary>
    private List<ComicPanel> ExtractPanelsFromGutters(
        List<(int start, int end)> horizontalGutters,
        List<(int start, int end)> verticalGutters,
        int imageWidth,
        int imageHeight,
        int pageIndex)
    {
        var panels = new List<ComicPanel>();
        
        // Add image edges as implicit gutters
        var hBoundaries = new List<int> { 0 };
        foreach (var (start, end) in horizontalGutters)
        {
            hBoundaries.Add((start + end) / 2);
        }
        hBoundaries.Add(imageHeight);
        
        var vBoundaries = new List<int> { 0 };
        foreach (var (start, end) in verticalGutters)
        {
            vBoundaries.Add((start + end) / 2);
        }
        vBoundaries.Add(imageWidth);
        
        // Create panels from grid cells
        var panelIndex = 0;
        var minPanelWidth = imageWidth * MinPanelSizeRatio;
        var minPanelHeight = imageHeight * MinPanelSizeRatio;
        
        for (var row = 0; row < hBoundaries.Count - 1; row++)
        {
            for (var col = 0; col < vBoundaries.Count - 1; col++)
            {
                var x = vBoundaries[col];
                var y = hBoundaries[row];
                var w = vBoundaries[col + 1] - x;
                var h = hBoundaries[row + 1] - y;
                
                // Skip if panel is too small
                if (w < minPanelWidth || h < minPanelHeight)
                {
                    continue;
                }
                
                panels.Add(new ComicPanel
                {
                    PageIndex = pageIndex,
                    PanelIndex = panelIndex++,
                    X = (double)x / imageWidth,
                    Y = (double)y / imageHeight,
                    Width = (double)w / imageWidth,
                    Height = (double)h / imageHeight,
                    Confidence = CalculateConfidence(horizontalGutters.Count, verticalGutters.Count)
                });
            }
        }
        
        return panels;
    }
    
    /// <summary>
    /// Calculate confidence score based on gutter detection
    /// </summary>
    private static double CalculateConfidence(int hGutterCount, int vGutterCount)
    {
        // More gutters found = higher confidence, up to a point
        var totalGutters = hGutterCount + vGutterCount;
        return totalGutters switch
        {
            0 => 0.3,
            1 => 0.5,
            2 => 0.7,
            3 => 0.85,
            _ => 0.95
        };
    }
}
