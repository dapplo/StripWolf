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
    private const double MinPanelSizeRatio = 0.04;

    /// <summary>
    /// Detect panels on a comic page
    /// </summary>
    public async Task<PagePanelInfo> DetectPanelsAsync(string comicFilePath, int pageIndex, byte[] pageData, bool isManga = false)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(comicFilePath, out var pageCache) &&
                pageCache.TryGetValue(pageIndex, out var cached))
            {
                return cached;
            }
        }
        
        var result = await Task.Run(() => DetectPanelsInternal(pageIndex, pageData, isManga));
        
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
    
    public async Task PreDetectPagesAsync(string comicFilePath, IEnumerable<(int pageIndex, byte[] pageData)> pages, bool isManga = false)
    {
        var tasks = pages.Select(p => DetectPanelsAsync(comicFilePath, p.pageIndex, p.pageData, isManga));
        await Task.WhenAll(tasks);
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
    
    private PagePanelInfo DetectPanelsInternal(int pageIndex, byte[] pageData, bool isManga)
    {
        var result = new PagePanelInfo { PageIndex = pageIndex };
        
        try
        {
            using var src = Mat.FromImageData(pageData, ImreadModes.Color);
            if (src.Empty()) throw new Exception("Failed to load image");

            int imageWidth = src.Width;
            int imageHeight = src.Height;
            double pageArea = imageWidth * imageHeight;

            // 1. Grayscale & Noise Reduction
            using var grayFull = new Mat();
            Cv2.CvtColor(src, grayFull, ColorConversionCodes.BGR2GRAY);
            
            // Add a small white border to ensure edge-touching panels have a "gutter" to detect
            int padding = 10;
            using var gray = new Mat();
            Cv2.CopyMakeBorder(grayFull, gray, padding, padding, padding, padding, BorderTypes.Constant, Scalar.White);

            using var blurred = new Mat();
            Cv2.BilateralFilter(gray, blurred, 9, 75, 75);

            // 2. Edge & Structure Detection
            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 50, 150);

            using var thresh = new Mat();
            Cv2.AdaptiveThreshold(blurred, thresh, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.BinaryInv, 15, 4);
            
            using var combined = new Mat();
            Cv2.BitwiseOr(edges, thresh, combined);

            // 3. Precise Morphology
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            using var morph = new Mat();
            Cv2.MorphologyEx(combined, morph, MorphTypes.Close, kernel, iterations: 1);
            
            using var dilated = new Mat();
            Cv2.Dilate(morph, dilated, kernel, iterations: 1);

            // 4. Contour Analysis
            // Use CComp to get all contours, then filter by size and solidity. 
            // This is more robust against edge-touching panels that might be wrapped in a page-level contour.
            Cv2.FindContours(dilated, out var contours, out var hierarchy, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

            var candidates = new List<ComicPanel>();
            double minW = imageWidth * MinPanelSizeRatio;
            double minH = imageHeight * MinPanelSizeRatio;

            for (int i = 0; i < contours.Length; i++)
            {
                var contour = contours[i];
                var rect = Cv2.BoundingRect(contour);
                
                // Adjust for padding
                int adjX = rect.X - padding;
                int adjY = rect.Y - padding;
                int adjW = rect.Width;
                int adjH = rect.Height;

                // Clamp to image bounds
                if (adjX < 0) { adjW += adjX; adjX = 0; }
                if (adjY < 0) { adjH += adjY; adjY = 0; }
                if (adjX + adjW > imageWidth) adjW = imageWidth - adjX;
                if (adjY + adjH > imageHeight) adjH = imageHeight - adjY;

                double area = Cv2.ContourArea(contour);
                double rectArea = (double)rect.Width * rect.Height;
                
                // Filtering
                if (adjW < minW || adjH < minH) continue;
                if (rectArea < pageArea * 0.01) continue;
                
                // Filter out panels that are basically the whole page
                if (adjW > imageWidth * 0.98 && adjH > imageHeight * 0.98) continue;

                // Solidity Check: Panels are rectangular. 
                // We use a slightly lower threshold (0.65) to be inclusive of stylized panels.
                double solidity = area / rectArea;
                if (solidity < 0.65) continue; 

                candidates.Add(new ComicPanel
                {
                    PageIndex = pageIndex,
                    X = (double)adjX / imageWidth,
                    Y = (double)adjY / imageHeight,
                    Width = (double)adjW / imageWidth,
                    Height = (double)adjH / imageHeight,
                    Confidence = solidity * 0.9
                });
            }

            // 5. Clean up Overlaps and Nested Panels
            var panels = FilterOverlappingPanels(candidates);

            // 6. Sort and Index
            var sortedPanels = SortPanelsByReadingOrder(panels, isManga);
            for (int i = 0; i < sortedPanels.Count; i++) sortedPanels[i].PanelIndex = i;

            if (sortedPanels.Count > 0)
            {
                result.Panels = sortedPanels;
                result.DetectionSuccessful = true;
                result.IsSplashPage = sortedPanels.Count == 1;
            }
            else
            {
                return CreateSplashPageResult(pageIndex);
            }
        }
        catch
        {
            return CreateSplashPageResult(pageIndex, 0.5);
        }
        
        return result;
    }

    private List<ComicPanel> FilterOverlappingPanels(List<ComicPanel> candidates)
    {
        if (candidates.Count <= 1) return candidates;

        // Sort by area descending
        var sorted = candidates.OrderByDescending(p => p.Width * p.Height).ToList();
        var result = new List<ComicPanel>();

        foreach (var p in sorted)
        {
            bool keep = true;
            foreach (var existing in result)
            {
                // Is p almost the same as existing?
                if (Math.Abs(p.X - existing.X) < 0.03 &&
                    Math.Abs(p.Y - existing.Y) < 0.03 &&
                    Math.Abs(p.Width - existing.Width) < 0.03 &&
                    Math.Abs(p.Height - existing.Height) < 0.03)
                {
                    keep = false;
                    break;
                }
                
                // Is p entirely inside existing?
                if (p.X >= existing.X - 0.005 && 
                    p.Y >= existing.Y - 0.005 && 
                    p.X + p.Width <= existing.X + existing.Width + 0.005 && 
                    p.Y + p.Height <= existing.Y + existing.Height + 0.005)
                {
                    keep = false;
                    break;
                }

                // Intersection over Area check
                double x1 = Math.Max(p.X, existing.X);
                double y1 = Math.Max(p.Y, existing.Y);
                double x2 = Math.Min(p.X + p.Width, existing.X + existing.Width);
                double y2 = Math.Min(p.Y + p.Height, existing.Y + existing.Height);

                if (x2 > x1 && y2 > y1)
                {
                    double intersectionArea = (x2 - x1) * (y2 - y1);
                    double pArea = p.Width * p.Height;
                    // If more than 80% of p is covered by existing, it's likely a sub-panel or artifact
                    if (intersectionArea / pArea > 0.8)
                    {
                        keep = false;
                        break;
                    }
                }
            }

            if (keep) result.Add(p);
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
            var topPanel = remaining[0];
            // Identify panels in the same row (vertical overlap with the center of the top-most panel)
            double rowCenterY = topPanel.Y + topPanel.Height / 2;
            var row = remaining.Where(p => p.Y < rowCenterY && (p.Y + p.Height) > rowCenterY).ToList();
            
            // If row logic fails, just take the top one
            if (row.Count == 0) row = new List<ComicPanel> { topPanel };

            var sortedRow = isManga 
                ? row.OrderByDescending(p => p.X).ToList() 
                : row.OrderBy(p => p.X).ToList();

            result.AddRange(sortedRow);
            foreach (var p in sortedRow) remaining.Remove(p);
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
