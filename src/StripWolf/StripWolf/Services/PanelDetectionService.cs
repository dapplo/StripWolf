using StripWolf.Models;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace StripWolf.Services;

/// <summary>
/// Service for detecting comic panels (scenes) on comic pages.
/// Uses OpenCV for advanced image processing to find panels.
/// </summary>
public class PanelDetectionService
{
    private readonly Dictionary<string, Dictionary<int, PagePanelInfo>> _cache = new();
    private readonly object _cacheLock = new();
    
    private const double MinPanelSizeRatio = 0.04;

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
        lock (_cacheLock) { _cache.Remove(comicFilePath); }
    }
    
    public void ClearAllCache()
    {
        lock (_cacheLock) { _cache.Clear(); }
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

            int imgW = src.Width;
            int imgH = src.Height;
            double pageArea = (double)imgW * imgH;

            // 1. Pre-processing: Grayscale + Bilateral Filter
            using var grayFull = new Mat();
            Cv2.CvtColor(src, grayFull, ColorConversionCodes.BGR2GRAY);
            
            // Add white padding so edge-touching panels have a detectable boundary
            int pad = 15;
            using var gray = new Mat();
            Cv2.CopyMakeBorder(grayFull, gray, pad, pad, pad, pad, BorderTypes.Constant, Scalar.White);

            using var blurred = new Mat();
            Cv2.BilateralFilter(gray, blurred, 9, 75, 75);

            // 2. Edge & Structure Detection
            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 50, 150);

            using var thresh = new Mat();
            Cv2.AdaptiveThreshold(blurred, thresh, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.BinaryInv, 15, 4);
            
            using var combined = new Mat();
            Cv2.BitwiseOr(edges, thresh, combined);

            // 3. Morphology: Surgical kernel to bridge gaps but keep gutters
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            using var morph = new Mat();
            Cv2.MorphologyEx(combined, morph, MorphTypes.Close, kernel, iterations: 1);
            
            // 4. Hierarchical Contour Analysis (CComp)
            Cv2.FindContours(morph, out var contours, out var hierarchy, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

            var candidates = new List<ComicPanel>();
            double minW = imgW * MinPanelSizeRatio;
            double minH = imgH * MinPanelSizeRatio;

            for (int i = 0; i < contours.Length; i++)
            {
                // CComp Hierarchy: [Next, Previous, First_Child, Parent]
                // We only want top-level components (Parent == -1)
                // This ignores text/details that are inside a frame
                if (hierarchy[i].Parent != -1) continue;

                var rect = Cv2.BoundingRect(contours[i]);
                
                // Remove padding
                int adjX = rect.X - pad;
                int adjY = rect.Y - pad;
                int adjW = rect.Width;
                int adjH = rect.Height;

                // Clamp to original bounds
                if (adjX < 0) { adjW += adjX; adjX = 0; }
                if (adjY < 0) { adjH += adjY; adjY = 0; }
                if (adjX + adjW > imgW) adjW = imgW - adjX;
                if (adjY + adjH > imgH) adjH = imgH - adjY;

                double area = Cv2.ContourArea(contours[i]);
                double rectArea = (double)rect.Width * rect.Height;
                
                if (adjW < minW || adjH < minH) continue;
                if (rectArea < pageArea * 0.015) continue; // Slightly stricter area
                if (adjW > imgW * 0.98 && adjH > imgH * 0.98) continue; // Whole page is splash

                // Solidity Check: Panels are rectangular boxes
                double solidity = area / rectArea;
                if (solidity < 0.80) continue; // Stricter solidity to ignore non-boxes

                candidates.Add(new ComicPanel
                {
                    PageIndex = pageIndex,
                    X = (double)adjX / imgW,
                    Y = (double)adjY / imgH,
                    Width = (double)adjW / imgW,
                    Height = (double)adjH / imgH,
                    Confidence = solidity
                });
            }

            // 5. Filter overlaps and small artifacts
            var panels = FilterOverlappingPanels(candidates);

            // 6. Final Reading Order
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

        var sorted = candidates.OrderByDescending(p => p.Width * p.Height).ToList();
        var result = new List<ComicPanel>();

        foreach (var p in sorted)
        {
            bool keep = true;
            foreach (var existing in result)
            {
                // Exact duplication or very close overlap
                if (Math.Abs(p.X - existing.X) < 0.02 && Math.Abs(p.Y - existing.Y) < 0.02 &&
                    Math.Abs(p.Width - existing.Width) < 0.02 && Math.Abs(p.Height - existing.Height) < 0.02)
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
                    // If p is 70% inside existing, it's likely redundant
                    if (intersectionArea / pArea > 0.7)
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
            double rowCenterY = topPanel.Y + topPanel.Height / 2;
            
            // Panels in the same visual row
            var row = remaining.Where(p => p.Y < rowCenterY && (p.Y + p.Height) > rowCenterY).ToList();
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
                    PageIndex = pageIndex, PanelIndex = 0,
                    X = 0, Y = 0, Width = 1, Height = 1,
                    Confidence = confidence
                }
            }
        };
    }
}
