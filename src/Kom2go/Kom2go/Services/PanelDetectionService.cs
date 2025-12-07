using Kom2go.Models;
using OpenCvSharp;

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
    /// Internal panel detection implementation using OpenCV
    /// </summary>
    private PagePanelInfo DetectPanelsInternal(int pageIndex, byte[] pageData)
    {
        var result = new PagePanelInfo
        {
            PageIndex = pageIndex
        };
        
        try
        {
            // Decode image from byte array using OpenCV
            using var mat = Mat.FromImageData(pageData, ImreadModes.Color);
            
            var imageWidth = mat.Width;
            var imageHeight = mat.Height;
            
            // Convert to grayscale
            using var gray = new Mat();
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
            
            // Apply Gaussian blur to reduce noise
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);
            
            // Apply adaptive thresholding to detect gutters (white areas)
            using var thresh = new Mat();
            Cv2.Threshold(blurred, thresh, GutterBrightnessThreshold, 255, ThresholdTypes.Binary);
            
            // Detect contours
            using var contours = new Mat();
            using var hierarchy = new Mat();
            Cv2.FindContours(thresh, out var contoursArray, out var hierarchyArray, 
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            
            // Find rectangular panels by analyzing contours
            var panels = new List<ComicPanel>();
            var panelIndex = 0;
            var minPanelWidth = imageWidth * MinPanelSizeRatio;
            var minPanelHeight = imageHeight * MinPanelSizeRatio;
            
            // Try contour-based detection first
            var detectedRects = new List<Rect>();
            foreach (var contour in contoursArray)
            {
                var rect = Cv2.BoundingRect(contour);
                
                // Filter out panels that are too small
                if (rect.Width >= minPanelWidth && rect.Height >= minPanelHeight)
                {
                    detectedRects.Add(rect);
                }
            }
            
            // If contour detection didn't find enough panels, try gutter-based detection
            if (detectedRects.Count == 0)
            {
                detectedRects = DetectPanelsViaGutters(blurred, imageWidth, imageHeight);
            }
            
            // Sort rectangles by position (top to bottom, left to right)
            detectedRects = detectedRects
                .OrderBy(r => r.Y)
                .ThenBy(r => r.X)
                .ToList();
            
            // Create panels from detected rectangles
            foreach (var rect in detectedRects)
            {
                // Trim the panel boundaries slightly to exclude gutter white space
                var trimX = Math.Min(MaxPanelTrimPixels, rect.Width / PanelTrimDivisor);
                var trimY = Math.Min(MaxPanelTrimPixels, rect.Height / PanelTrimDivisor);
                
                panels.Add(new ComicPanel
                {
                    PageIndex = pageIndex,
                    PanelIndex = panelIndex++,
                    X = (double)(rect.X + trimX) / imageWidth,
                    Y = (double)(rect.Y + trimY) / imageHeight,
                    Width = (double)(rect.Width - 2 * trimX) / imageWidth,
                    Height = (double)(rect.Height - 2 * trimY) / imageHeight,
                    Confidence = CalculateConfidence(detectedRects.Count)
                });
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
    /// Detect panels via gutter-based approach (fallback method)
    /// </summary>
    private List<Rect> DetectPanelsViaGutters(Mat grayImage, int width, int height)
    {
        var panels = new List<Rect>();
        
        // Find horizontal gutters to identify rows
        var horizontalGutters = FindHorizontalGutters(grayImage, width, height);
        
        // Get row boundaries
        var rowBoundaries = new List<(int top, int bottom)>();
        var prevY = 0;
        foreach (var (start, end) in horizontalGutters)
        {
            var gutterMiddle = (start + end) / 2;
            if (gutterMiddle > prevY + height * MinPanelSizeRatio)
            {
                rowBoundaries.Add((prevY, start));
            }
            prevY = end;
        }
        // Add the last row
        if (height > prevY + height * MinPanelSizeRatio)
        {
            rowBoundaries.Add((prevY, height));
        }
        
        // If no rows found, treat entire page as one row
        if (rowBoundaries.Count == 0)
        {
            rowBoundaries.Add((0, height));
        }
        
        // For each row, find vertical gutters within that row
        foreach (var (rowTop, rowBottom) in rowBoundaries)
        {
            // Find vertical gutters within this row
            var verticalGuttersInRow = FindVerticalGuttersInRow(grayImage, width, rowTop, rowBottom);
            
            // Get column boundaries within this row
            var colBoundaries = new List<(int left, int right)>();
            var prevX = 0;
            foreach (var (start, end) in verticalGuttersInRow)
            {
                var gutterMiddle = (start + end) / 2;
                if (gutterMiddle > prevX + width * MinPanelSizeRatio)
                {
                    colBoundaries.Add((prevX, start));
                }
                prevX = end;
            }
            // Add the last column
            if (width > prevX + width * MinPanelSizeRatio)
            {
                colBoundaries.Add((prevX, width));
            }
            
            // If no columns found, treat entire row as one panel
            if (colBoundaries.Count == 0)
            {
                colBoundaries.Add((0, width));
            }
            
            // Create rectangles for each cell in this row
            foreach (var (colLeft, colRight) in colBoundaries)
            {
                var panelWidth = colRight - colLeft;
                var panelHeight = rowBottom - rowTop;
                
                // Skip if panel is too small
                if (panelWidth >= width * MinPanelSizeRatio && 
                    panelHeight >= height * MinPanelSizeRatio)
                {
                    panels.Add(new Rect(colLeft, rowTop, panelWidth, panelHeight));
                }
            }
        }
        
        return panels;
    }
    
    /// <summary>
    /// Find horizontal gutters (white/light horizontal bands)
    /// </summary>
    private List<(int start, int end)> FindHorizontalGutters(Mat grayImage, int width, int height)
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
                if (grayImage.At<byte>(y, x) >= GutterBrightnessThreshold)
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
    private List<(int start, int end)> FindVerticalGuttersInRow(Mat grayImage, int width, int rowTop, int rowBottom)
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
                if (grayImage.At<byte>(y, x) >= GutterBrightnessThreshold)
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
    /// Calculate confidence score based on number of detected panels
    /// </summary>
    private static double CalculateConfidence(int panelCount)
    {
        // More panels found = higher confidence, up to a point
        return panelCount switch
        {
            0 => 0.3,
            1 => 0.5,
            2 => 0.7,
            3 => 0.85,
            >= 4 => 0.95,
            _ => 0.3
        };
    }
}
