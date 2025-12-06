using Kom2go.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Kom2go.Services;

/// <summary>
/// Service for detecting comic panels (scenes) on comic pages.
/// Uses YOLO model for detection if available, otherwise falls back to image processing algorithm.
/// </summary>
public partial class PanelDetectionService : IDisposable
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
    private const double MinGutterRatio = 0.003;
    
    /// <summary>
    /// Maximum gutter width (as ratio of page width)
    /// </summary>
    private const double MaxGutterRatio = 0.08;
    
    /// <summary>
    /// Brightness threshold for detecting white/light gutters (0-255)
    /// </summary>
    private const byte GutterBrightnessThreshold = 220;
    
    /// <summary>
    /// Minimum percentage of light pixels in a line to be considered a gutter
    /// </summary>
    private const double GutterLightPixelThreshold = 0.80;
    
    /// <summary>
    /// Maximum trim size in pixels to exclude gutter white space from panel boundaries
    /// </summary>
    private const int MaxPanelTrimPixels = 5;
    
    /// <summary>
    /// Panel trim ratio (trim size as fraction of panel dimension)
    /// </summary>
    private const int PanelTrimDivisor = 20;
    
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
        
        // Perform detection using YOLO if available, otherwise use traditional algorithm
        var result = await Task.Run(() => _useYolo 
            ? DetectPanelsWithYolo(pageIndex, pageData) 
            : DetectPanelsInternal(pageIndex, pageData));
        
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
            
            // First, find horizontal gutters to identify rows
            var horizontalGutters = FindHorizontalGutters(grayImage, imageWidth, imageHeight);
            
            // Get row boundaries
            var rowBoundaries = new List<(int top, int bottom)>();
            var prevY = 0;
            foreach (var (start, end) in horizontalGutters)
            {
                var gutterMiddle = (start + end) / 2;
                if (gutterMiddle > prevY + imageHeight * MinPanelSizeRatio)
                {
                    rowBoundaries.Add((prevY, start));
                }
                prevY = end;
            }
            // Add the last row
            if (imageHeight > prevY + imageHeight * MinPanelSizeRatio)
            {
                rowBoundaries.Add((prevY, imageHeight));
            }
            
            // If no rows found, treat entire page as one row
            if (rowBoundaries.Count == 0)
            {
                rowBoundaries.Add((0, imageHeight));
            }
            
            // For each row, find vertical gutters within that row
            var panels = new List<ComicPanel>();
            var panelIndex = 0;
            
            foreach (var (rowTop, rowBottom) in rowBoundaries)
            {
                // Find vertical gutters within this row
                var verticalGuttersInRow = FindVerticalGuttersInRow(grayImage, imageWidth, rowTop, rowBottom);
                
                // Get column boundaries within this row
                var colBoundaries = new List<(int left, int right)>();
                var prevX = 0;
                foreach (var (start, end) in verticalGuttersInRow)
                {
                    var gutterMiddle = (start + end) / 2;
                    if (gutterMiddle > prevX + imageWidth * MinPanelSizeRatio)
                    {
                        colBoundaries.Add((prevX, start));
                    }
                    prevX = end;
                }
                // Add the last column
                if (imageWidth > prevX + imageWidth * MinPanelSizeRatio)
                {
                    colBoundaries.Add((prevX, imageWidth));
                }
                
                // If no columns found, treat entire row as one panel
                if (colBoundaries.Count == 0)
                {
                    colBoundaries.Add((0, imageWidth));
                }
                
                // Create panels for each cell in this row
                foreach (var (colLeft, colRight) in colBoundaries)
                {
                    var panelWidth = colRight - colLeft;
                    var panelHeight = rowBottom - rowTop;
                    
                    // Skip if panel is too small
                    if (panelWidth < imageWidth * MinPanelSizeRatio || 
                        panelHeight < imageHeight * MinPanelSizeRatio)
                    {
                        continue;
                    }
                    
                    // Trim the panel boundaries slightly to exclude gutter white space
                    var trimX = Math.Min(MaxPanelTrimPixels, panelWidth / PanelTrimDivisor);
                    var trimY = Math.Min(MaxPanelTrimPixels, panelHeight / PanelTrimDivisor);
                    
                    panels.Add(new ComicPanel
                    {
                        PageIndex = pageIndex,
                        PanelIndex = panelIndex++,
                        X = (double)(colLeft + trimX) / imageWidth,
                        Y = (double)(rowTop + trimY) / imageHeight,
                        Width = (double)(panelWidth - 2 * trimX) / imageWidth,
                        Height = (double)(panelHeight - 2 * trimY) / imageHeight,
                        Confidence = CalculateConfidence(horizontalGutters.Count, verticalGuttersInRow.Count)
                    });
                }
            }
            
            if (panels.Count > 0)
            {
                result.Panels = panels;
                result.DetectionSuccessful = true;
                result.IsSplashPage = panels.Count == 1;
                return result;
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
        var (minGutterSize, maxGutterSize) = CalculateGutterSizeBounds(height);
        
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
            var isLightRow = lightPixelCount > sampleWidth * GutterLightPixelThreshold;
            
            if (isLightRow && !inGutter)
            {
                gutterStart = y;
                inGutter = true;
            }
            else if (!isLightRow && inGutter)
            {
                var gutterHeight = y - gutterStart;
                if (gutterHeight >= minGutterSize && gutterHeight <= maxGutterSize)
                {
                    gutters.Add((gutterStart, y));
                }
                inGutter = false;
            }
        }
        
        return gutters;
    }
    
    /// <summary>
    /// Find vertical gutters within a specific row (white/light vertical bands)
    /// </summary>
    private List<(int start, int end)> FindVerticalGuttersInRow(byte[,] grayImage, int width, int rowTop, int rowBottom)
    {
        var gutters = new List<(int start, int end)>();
        var (minGutterSize, maxGutterSize) = CalculateGutterSizeBounds(width);
        
        var rowHeight = rowBottom - rowTop;
        if (rowHeight <= 0) return gutters;
        
        // Sample most of the row height (avoid edge artifacts)
        var sampleStartY = rowTop + rowHeight / 10;
        var sampleEndY = rowBottom - rowHeight / 10;
        var sampleHeight = sampleEndY - sampleStartY;
        
        if (sampleHeight <= 0) return gutters;
        
        var inGutter = false;
        var gutterStart = 0;
        
        for (var x = 0; x < width; x++)
        {
            // Count light pixels in this column within the row
            var lightPixelCount = 0;
            for (var y = sampleStartY; y < sampleEndY; y++)
            {
                if (grayImage[y, x] >= GutterBrightnessThreshold)
                {
                    lightPixelCount++;
                }
            }
            
            // If most of the column (within this row) is light, it's part of a gutter
            var isLightColumn = lightPixelCount > sampleHeight * GutterLightPixelThreshold;
            
            if (isLightColumn && !inGutter)
            {
                gutterStart = x;
                inGutter = true;
            }
            else if (!isLightColumn && inGutter)
            {
                var gutterWidth = x - gutterStart;
                if (gutterWidth >= minGutterSize && gutterWidth <= maxGutterSize)
                {
                    gutters.Add((gutterStart, x));
                }
                inGutter = false;
            }
        }
        
        return gutters;
    }
    
    /// <summary>
    /// Calculate min and max gutter size bounds for a given dimension
    /// </summary>
    private (int min, int max) CalculateGutterSizeBounds(int dimension)
    {
        var min = (int)(dimension * MinGutterRatio);
        var max = (int)(dimension * MaxGutterRatio);
        
        // Ensure minimum gutter size is at least 2 pixels
        if (min < 2) min = 2;
        if (max < min) max = min + 1;
        
        return (min, max);
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
