using StripWolf.Models;
using OpenCvSharp;

namespace StripWolf.Services;

/// <summary>
/// Service for detecting comic panels (scenes) on comic pages.
/// Uses OpenCV for advanced image processing to find panels.
/// </summary>
public class PanelDetectionService
{
    private readonly Dictionary<string, Dictionary<int, PagePanelInfo>> _cache = new();
    private readonly object _cacheLock = new();

    public bool IsAvailable => true;
    
    private const double MinPanelSizeRatio = 0.04;
    private const double MinPanelAreaRatio = 0.015;
    private const double MinPanelConfidence = 0.48;
    private const double LowConfidenceFallbackThreshold = 0.66;

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
            using var edgesFull = new Mat(edges, new Rect(pad, pad, imgW, imgH));

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

            var contourCandidates = new List<ComicPanel>();

            for (int i = 0; i < contours.Length; i++)
            {
                // CComp Hierarchy: [Next, Previous, First_Child, Parent]
                var rect = Cv2.BoundingRect(contours[i]);
                double area = Cv2.ContourArea(contours[i]);
                double rectArea = (double)rect.Width * rect.Height;

                // Prefer top-level contours, but still allow large child contours.
                // Border-touching or broken-frame panels can end up nested in CComp.
                if (hierarchy[i].Parent != -1 && rectArea < pageArea * 0.025 && area < pageArea * 0.015)
                {
                    continue;
                }
                
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

                var adjustedRect = new Rect(adjX, adjY, adjW, adjH);
                var contourCandidate = CreateContourCandidate(pageIndex, adjustedRect, area, rectArea, grayFull, edgesFull, imgW, imgH, pageArea);
                if (contourCandidate is not null)
                {
                    contourCandidates.Add(contourCandidate);
                }
            }

            var panels = FilterOverlappingPanels(contourCandidates);

            if (ShouldRunGutterFallback(panels))
            {
                var gutterCandidates = DetectGutterCandidates(pageIndex, grayFull, edgesFull, imgW, imgH, pageArea);
                panels = FilterOverlappingPanels(panels.Concat(gutterCandidates).ToList());
            }

            var evidenceLayoutCandidates = DetectEvidenceLayoutCandidates(pageIndex, grayFull, edgesFull, imgW, imgH, pageArea);
            if (ShouldUseEvidenceLayout(panels, evidenceLayoutCandidates))
            {
                panels = FilterOverlappingPanels(evidenceLayoutCandidates);
            }

            bool usedBorderLayoutFallback = false;
            bool shouldRunBorderLayoutFallback = ShouldRunPageLayoutFallback(panels);
            bool shouldConsiderBorderLayout = shouldRunBorderLayoutFallback || ShouldConsiderBorderLayout(panels);
            if (shouldConsiderBorderLayout)
            {
                var borderLayoutCandidates = DetectBorderLayoutCandidates(pageIndex, grayFull, imgW, imgH, pageArea);
                if (borderLayoutCandidates.Count > 0 &&
                    (shouldRunBorderLayoutFallback || ShouldUseBorderLayout(panels, borderLayoutCandidates)))
                {
                    panels = FilterOverlappingPanels(borderLayoutCandidates);
                    usedBorderLayoutFallback = true;
                }
                else if (shouldRunBorderLayoutFallback && evidenceLayoutCandidates.Count > 0)
                {
                    panels = FilterOverlappingPanels(evidenceLayoutCandidates);
                }
            }

            panels = RemoveNestedInsetPanels(panels);
            var recoveredPanels = RecoverMissingPanelsFromSparseRows(pageIndex, panels, grayFull, edgesFull, imgW, imgH, pageArea);
            if (recoveredPanels.Count > 0)
            {
                panels = RemoveNestedInsetPanels(FilterOverlappingPanels(panels.Concat(recoveredPanels).ToList()));
            }
            panels = RefinePanelsToLocalGutters(panels, grayFull, edgesFull, imgW, imgH, pageArea);
            panels = RefinePanelsToValidatedSeparators(panels, grayFull, edgesFull, imgW, imgH, pageArea);
            if (usedBorderLayoutFallback)
            {
                panels = RefineBorderLayoutColumns(panels, grayFull, edgesFull, imgW, imgH, pageArea);
                panels = RefineRowsToHorizontalBorders(panels, grayFull, edgesFull, imgW, imgH);
                panels = RefinePanelsToHorizontalBorders(panels, grayFull, edgesFull, imgW, imgH);
            }
            panels = RefinePanelsAcrossVerticalGaps(panels, grayFull, edgesFull, imgW, imgH);
            panels = RefinePanelsFromUncoveredRegions(panels, grayFull, edgesFull, imgW, imgH, pageArea);
            panels = RefineRowsFromUncoveredBands(panels, grayFull, edgesFull, imgW, imgH, pageArea);
            panels = RefineWidePanelsToInternalGutters(panels, grayFull, edgesFull, imgW, imgH, pageArea);

            // 6. Final Reading Order
            var sortedPanels = SortPanelsByReadingOrder(panels, isManga);
            for (int i = 0; i < sortedPanels.Count; i++) sortedPanels[i].PanelIndex = i;

            if (ShouldCollapseToSplashPage(sortedPanels) || ShouldCollapseToPartitionedSplash(sortedPanels))
            {
                double splashConfidence = sortedPanels.Count > 0
                    ? Math.Clamp(sortedPanels.Average(panel => panel.Confidence), 0.5, 1.0)
                    : 1.0;
                return CreateSplashPageResult(pageIndex, splashConfidence);
            }

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

    private List<ComicPanel> DetectGutterCandidates(int pageIndex, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        using var gutterMask = new Mat();
        Cv2.AdaptiveThreshold(grayFull, gutterMask, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 31, 8);

        int kernelWidth = Math.Max(5, MakeOdd(Math.Max(imgW / 90, 5)));
        int kernelHeight = Math.Max(5, MakeOdd(Math.Max(imgH / 90, 5)));

        using var mergeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kernelWidth, kernelHeight));
        using var mergedContent = new Mat();
        Cv2.MorphologyEx(gutterMask, mergedContent, MorphTypes.Close, mergeKernel, iterations: 2);

        using var cleanupKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        using var cleanedContent = new Mat();
        Cv2.MorphologyEx(mergedContent, cleanedContent, MorphTypes.Open, cleanupKernel, iterations: 1);

        Cv2.FindContours(cleanedContent, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var candidates = new List<ComicPanel>();
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            double area = Cv2.ContourArea(contour);
            double rectArea = (double)rect.Width * rect.Height;
            var candidate = CreateGutterCandidate(pageIndex, rect, area, rectArea, grayFull, edges, imgW, imgH, pageArea);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private List<ComicPanel> RecoverMissingPanelsFromSparseRows(int pageIndex, List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        var candidates = new List<ComicPanel>();
        foreach (var row in BuildRows(panels))
        {
            int rowStart = (int)Math.Round(row.MinY * imgH);
            int rowEnd = (int)Math.Round(row.MaxY * imgH);
            int rowHeight = rowEnd - rowStart;
            if (rowHeight < Math.Max(40, imgH / 18))
            {
                continue;
            }

            if (row.PanelCount > 1 && row.Coverage >= 0.82)
            {
                continue;
            }

            var verticalGutters = FindVerticalGutters(grayFull, edges, imgW, rowStart, rowEnd);
            var columnBands = BuildSegments(verticalGutters, imgW, Math.Max(40, imgW / 10));
            if (columnBands.Count <= row.PanelCount)
            {
                continue;
            }

            foreach (var (columnStart, columnEnd) in columnBands)
            {
                int width = columnEnd - columnStart;
                int height = rowHeight;
                if (width < Math.Max(40, imgW / 12))
                {
                    continue;
                }

                var rect = new Rect(columnStart, rowStart, width, height);
                var candidate = CreateRecoveredCandidate(pageIndex, rect, panels, 0.55, 0.20, 0.45, grayFull, edges, imgW, imgH, pageArea);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates;
    }

    private ComicPanel? CreateContourCandidate(int pageIndex, Rect rect, double contourArea, double rectArea, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        if (!IsSensibleCandidate(rect, imgW, imgH, pageArea))
        {
            return null;
        }

        double solidity = rectArea > 0 ? contourArea / rectArea : 0;
        if (solidity < 0.65)
        {
            return null;
        }

        var stats = MeasureCandidateStats(rect, grayFull, edges, imgW, imgH, pageArea);
        if (stats.InteriorStdDev < 6 && stats.AreaRatio < 0.08)
        {
            return null;
        }

        double confidence =
            (solidity * 0.32) +
            (stats.BorderEdgeDensity * 0.23) +
            (stats.GutterContrast * 0.20) +
            (stats.InteriorVarianceScore * 0.15) +
            (stats.AreaScore * 0.05) +
            (stats.EdgeTouchScore * 0.05);

        if (confidence < MinPanelConfidence)
        {
            return null;
        }

        return CreatePanel(pageIndex, rect, imgW, imgH, confidence);
    }

    private ComicPanel? CreateGutterCandidate(int pageIndex, Rect rect, double contourArea, double rectArea, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        if (!IsSensibleCandidate(rect, imgW, imgH, pageArea))
        {
            return null;
        }

        double fillRatio = rectArea > 0 ? contourArea / rectArea : 0;
        if (fillRatio < 0.25)
        {
            return null;
        }

        var stats = MeasureCandidateStats(rect, grayFull, edges, imgW, imgH, pageArea);
        if (stats.GutterContrast < 0.10 && stats.BorderEdgeDensity < 0.08 && stats.EdgeTouchScore < 0.25)
        {
            return null;
        }

        double confidence =
            (fillRatio * 0.15) +
            (stats.GutterContrast * 0.35) +
            (stats.BorderEdgeDensity * 0.15) +
            (stats.InteriorVarianceScore * 0.20) +
            (stats.AreaScore * 0.10) +
            (stats.EdgeTouchScore * 0.05);

        if (confidence < 0.52)
        {
            return null;
        }

        return CreatePanel(pageIndex, rect, imgW, imgH, confidence);
    }

    private static bool IsSensibleCandidate(Rect rect, int imgW, int imgH, double pageArea)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        double minW = imgW * MinPanelSizeRatio;
        double minH = imgH * MinPanelSizeRatio;
        if (rect.Width < minW || rect.Height < minH)
        {
            return false;
        }

        double areaRatio = (rect.Width * (double)rect.Height) / pageArea;
        if (areaRatio < MinPanelAreaRatio)
        {
            return false;
        }

        if (rect.Width > imgW * 0.98 && rect.Height > imgH * 0.98)
        {
            return false;
        }

        double aspect = Math.Max(rect.Width / (double)rect.Height, rect.Height / (double)rect.Width);
        bool spansPage = rect.Width > imgW * 0.78 || rect.Height > imgH * 0.78;
        if (aspect > 8.5 && !spansPage)
        {
            return false;
        }

        return true;
    }

    private static ComicPanel CreatePanel(int pageIndex, Rect rect, int imgW, int imgH, double confidence)
    {
        return new ComicPanel
        {
            PageIndex = pageIndex,
            X = (double)rect.X / imgW,
            Y = (double)rect.Y / imgH,
            Width = (double)rect.Width / imgW,
            Height = (double)rect.Height / imgH,
            Confidence = Math.Clamp(confidence, 0.0, 1.0)
        };
    }

    private static CandidateStats MeasureCandidateStats(Rect rect, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        using var roi = new Mat(grayFull, rect);
        Cv2.MeanStdDev(roi, out var mean, out var stdDev);

        double innerMean = mean.Val0;
        double innerStdDev = stdDev.Val0;
        double outerMean = ComputeOuterRingMean(grayFull, rect, imgW, imgH);
        double gutterContrast = Math.Clamp((outerMean - innerMean) / 64.0, 0.0, 1.0);
        double borderEdgeDensity = ComputeBorderEdgeDensity(edges, rect, imgW, imgH);
        double edgeTouchScore = ComputeEdgeTouchScore(rect, imgW, imgH);
        double areaRatio = (rect.Width * (double)rect.Height) / pageArea;
        double areaScore = Math.Clamp((areaRatio - MinPanelAreaRatio) / 0.12, 0.0, 1.0);
        double interiorVarianceScore = Math.Clamp((innerStdDev - 8.0) / 32.0, 0.0, 1.0);

        return new CandidateStats
        {
            AreaRatio = areaRatio,
            AreaScore = areaScore,
            BorderEdgeDensity = borderEdgeDensity,
            EdgeTouchScore = edgeTouchScore,
            GutterContrast = gutterContrast,
            InteriorStdDev = innerStdDev,
            InteriorVarianceScore = interiorVarianceScore
        };
    }

    private ComicPanel? CreateRecoveredCandidate(int pageIndex, Rect rect, List<ComicPanel> existingPanels, double overlapThreshold, double confidenceBias, double confidenceThreshold, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        if (!IsSensibleCandidate(rect, imgW, imgH, pageArea) ||
            existingPanels.Any(existing => GetIntersectionRatio(existing, rect, imgW, imgH) > overlapThreshold))
        {
            return null;
        }

        if (!HasStrongCandidateBorderSupport(rect, rect.Y, rect.Bottom, grayFull, edges, imgW, imgH))
        {
            return null;
        }

        var stats = MeasureCandidateStats(rect, grayFull, edges, imgW, imgH, pageArea);
        double confidence = ComputeRecoveredCandidateConfidence(stats, confidenceBias);
        if (confidence < confidenceThreshold)
        {
            return null;
        }

        return CreatePanel(pageIndex, rect, imgW, imgH, confidence);
    }

    private static double ComputeRecoveredCandidateConfidence(CandidateStats stats, double confidenceBias)
    {
        return
            (stats.GutterContrast * 0.22) +
            (stats.BorderEdgeDensity * 0.20) +
            (stats.InteriorVarianceScore * 0.14) +
            (stats.AreaScore * 0.10) +
            (stats.EdgeTouchScore * 0.06) +
            confidenceBias;
    }

    private static double ComputeOuterRingMean(Mat grayFull, Rect rect, int imgW, int imgH)
    {
        int marginX = Math.Max(4, rect.Width / 14);
        int marginY = Math.Max(4, rect.Height / 14);

        double weightedSum = 0;
        double totalArea = 0;
        AddRegionMean(ExpandTop(rect, marginY), grayFull, ref weightedSum, ref totalArea, imgW, imgH, countMissingAsWhite: true);
        AddRegionMean(ExpandBottom(rect, marginY), grayFull, ref weightedSum, ref totalArea, imgW, imgH, countMissingAsWhite: true);
        AddRegionMean(ExpandLeft(rect, marginX), grayFull, ref weightedSum, ref totalArea, imgW, imgH, countMissingAsWhite: true);
        AddRegionMean(ExpandRight(rect, marginX), grayFull, ref weightedSum, ref totalArea, imgW, imgH, countMissingAsWhite: true);

        if (totalArea <= 0)
        {
            return 255;
        }

        return weightedSum / totalArea;
    }

    private static void AddRegionMean(Rect rect, Mat grayFull, ref double weightedSum, ref double totalArea, int imgW, int imgH, bool countMissingAsWhite = false)
    {
        double expectedArea = Math.Max(0, rect.Width) * (double)Math.Max(0, rect.Height);
        var clipped = ClipRect(rect, imgW, imgH);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            if (countMissingAsWhite && expectedArea > 0)
            {
                weightedSum += 255 * expectedArea;
                totalArea += expectedArea;
            }

            return;
        }

        using var roi = new Mat(grayFull, clipped);
        double area = clipped.Width * (double)clipped.Height;
        weightedSum += Cv2.Mean(roi).Val0 * area;
        totalArea += area;

        if (countMissingAsWhite && expectedArea > area)
        {
            double missingArea = expectedArea - area;
            weightedSum += 255 * missingArea;
            totalArea += missingArea;
        }
    }

    private static Rect ExpandTop(Rect rect, int marginY) => new(rect.X, rect.Y - marginY, rect.Width, marginY);
    private static Rect ExpandBottom(Rect rect, int marginY) => new(rect.X, rect.Bottom, rect.Width, marginY);
    private static Rect ExpandLeft(Rect rect, int marginX) => new(rect.X - marginX, rect.Y, marginX, rect.Height);
    private static Rect ExpandRight(Rect rect, int marginX) => new(rect.Right, rect.Y, marginX, rect.Height);

    private static double ComputeEdgeTouchScore(Rect rect, int imgW, int imgH)
    {
        const int edgeMargin = 3;
        double score = 0;

        if (rect.X <= edgeMargin || rect.Right >= imgW - edgeMargin)
        {
            score += 0.5;
        }

        if (rect.Y <= edgeMargin || rect.Bottom >= imgH - edgeMargin)
        {
            score += 0.5;
        }

        return score;
    }

    private static double ComputeBorderEdgeDensity(Mat edges, Rect rect, int imgW, int imgH)
    {
        int thickness = Math.Max(2, Math.Min(6, Math.Min(rect.Width, rect.Height) / 18));
        double edgePixels = 0;
        double totalPixels = 0;

        AddEdgeDensity(ExpandTop(rect, thickness), edges, ref edgePixels, ref totalPixels, imgW, imgH);
        AddEdgeDensity(ExpandBottom(rect, thickness), edges, ref edgePixels, ref totalPixels, imgW, imgH);
        AddEdgeDensity(ExpandLeft(rect, thickness), edges, ref edgePixels, ref totalPixels, imgW, imgH);
        AddEdgeDensity(ExpandRight(rect, thickness), edges, ref edgePixels, ref totalPixels, imgW, imgH);

        if (totalPixels <= 0)
        {
            return 0;
        }

        return Math.Clamp(edgePixels / totalPixels, 0.0, 1.0);
    }

    private static void AddEdgeDensity(Rect rect, Mat edges, ref double edgePixels, ref double totalPixels, int imgW, int imgH)
    {
        var clipped = ClipRect(rect, imgW, imgH);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        using var roi = new Mat(edges, clipped);
        double area = clipped.Width * (double)clipped.Height;
        edgePixels += Cv2.CountNonZero(roi);
        totalPixels += area;
    }

    private static Rect ClipRect(Rect rect, int imgW, int imgH)
    {
        int x = Math.Max(0, rect.X);
        int y = Math.Max(0, rect.Y);
        int right = Math.Min(imgW, rect.Right);
        int bottom = Math.Min(imgH, rect.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static int MakeOdd(int value) => value % 2 == 0 ? value + 1 : value;

    private static bool ShouldRunGutterFallback(List<ComicPanel> contourPanels)
    {
        if (contourPanels.Count < 2)
        {
            return true;
        }

        return contourPanels.Average(panel => panel.Confidence) < LowConfidenceFallbackThreshold;
    }

    private static bool ShouldRunPageLayoutFallback(List<ComicPanel> panels)
    {
        return panels.Count == 1 &&
               panels[0].Width >= 0.90 &&
               panels[0].Height >= 0.85;
    }

    private bool ShouldUseEvidenceLayout(List<ComicPanel> currentPanels, List<ComicPanel> evidencePanels)
    {
        if (evidencePanels.Count < 2)
        {
            return false;
        }

        double currentCoverage = CalculatePanelCoverage(currentPanels);
        double evidenceCoverage = CalculatePanelCoverage(evidencePanels);
        double currentScore = ScoreLayoutSupport(currentPanels);
        double evidenceScore = ScoreLayoutSupport(evidencePanels);
        bool currentMissesTop = !currentPanels.Any(panel => panel.Y <= 0.05);
        bool evidenceRestoresTop = evidencePanels.Any(panel => panel.Y <= 0.02 && panel.Width >= 0.85);
        bool currentHasLargeGap = GetLargestVerticalGap(currentPanels) >= 0.14;
        bool currentSparse = currentPanels.Count <= 2 || currentCoverage < 0.72;

        if (evidenceScore <= currentScore + 0.05)
        {
            return false;
        }

        return evidenceCoverage >= currentCoverage + 0.12 ||
               (currentMissesTop && evidenceRestoresTop) ||
               currentHasLargeGap ||
               currentSparse;
    }

    private bool ShouldUseBorderLayout(List<ComicPanel> currentPanels, List<ComicPanel> borderPanels)
    {
        if (borderPanels.Count < 3)
        {
            return false;
        }

        var currentRows = BuildRows(currentPanels);
        var borderRows = BuildRows(borderPanels);
        bool borderRestoresRows = borderRows.Count >= currentRows.Count + 1;
        double currentScore = ScoreLayoutSupport(currentPanels);
        double borderScore = ScoreLayoutSupport(borderPanels);

        return borderScore >= currentScore - 0.02 &&
               borderRestoresRows;
    }

    private static bool ShouldConsiderBorderLayout(List<ComicPanel> panels)
    {
        if (panels.Count < 5)
        {
            return false;
        }

        return HasSevereRowMisalignment(panels) ||
               (BuildRows(panels).Count <= 2 && panels.Any(panel => panel.Height >= 0.45 && panel.Width <= 0.40));
    }

    private static bool HasSevereRowMisalignment(List<ComicPanel> panels)
    {
        var rows = GroupPanelsIntoRows(panels)
            .OrderBy(row => row.MinY)
            .ToList();

        foreach (var row in rows)
        {
            if (row.Panels.Count < 2 || row.Coverage < 0.65)
            {
                continue;
            }

            double minTop = row.Panels.Min(panel => panel.Y);
            double maxTop = row.Panels.Max(panel => panel.Y);
            double minBottom = row.Panels.Min(panel => panel.Y + panel.Height);
            double maxBottom = row.Panels.Max(panel => panel.Y + panel.Height);
            if ((maxTop - minTop) >= 0.08 || (maxBottom - minBottom) >= 0.16)
            {
                return true;
            }
        }

        return false;
    }

    private static double CalculatePanelCoverage(List<ComicPanel> panels)
    {
        return panels.Sum(panel => panel.Width * panel.Height);
    }

    private static double ScoreLayoutSupport(List<ComicPanel> panels)
    {
        if (panels.Count == 0)
        {
            return 0;
        }

        double coverage = CalculatePanelCoverage(panels);
        double averageConfidence = panels.Average(panel => panel.Confidence);
        bool hasTopPanel = panels.Any(panel => panel.Y <= 0.03 && panel.Width >= 0.55);
        double rowCountScore = Math.Min(0.08, BuildRows(panels).Count * 0.02);

        return (coverage * 0.48) +
               (averageConfidence * 0.36) +
               (hasTopPanel ? 0.08 : 0.0) +
               rowCountScore;
    }

    private static double GetLargestVerticalGap(List<ComicPanel> panels)
    {
        var rows = BuildRows(panels)
            .OrderBy(row => row.MinY)
            .ToList();
        if (rows.Count == 0)
        {
            return 1.0;
        }

        double largestGap = rows[0].MinY;
        for (int i = 0; i < rows.Count - 1; i++)
        {
            largestGap = Math.Max(largestGap, rows[i + 1].MinY - rows[i].MaxY);
        }

        return Math.Max(largestGap, 1.0 - rows[^1].MaxY);
    }

    private static bool ShouldCollapseToSplashPage(List<ComicPanel> panels)
    {
        if (panels.Count == 0 || panels.Count > 3)
        {
            return false;
        }

        if (panels.Any(panel => panel.Width >= 0.45 || panel.Height >= 0.45))
        {
            return false;
        }

        double totalCoverage = panels.Sum(panel => panel.Width * panel.Height);
        double averageConfidence = panels.Average(panel => panel.Confidence);
        int edgeTouchingPanels = panels.Count(TouchesPageEdge);

        return totalCoverage <= 0.22 &&
               averageConfidence < 0.62 &&
               edgeTouchingPanels == panels.Count;
    }

    private static bool ShouldCollapseToPartitionedSplash(List<ComicPanel> panels)
    {
        if (panels.Count < 2 || panels.Count > 4)
        {
            return false;
        }

        double totalCoverage = panels.Sum(panel => panel.Width * panel.Height);
        double averageConfidence = panels.Average(panel => panel.Confidence);
        int largePanels = panels.Count(panel => (panel.Width * panel.Height) >= 0.18);
        int edgeTouchingPanels = panels.Count(TouchesPageEdge);

        return totalCoverage >= 0.88 &&
               averageConfidence >= 0.78 &&
               largePanels >= 2 &&
               edgeTouchingPanels == panels.Count;
    }

    private List<ComicPanel> FilterOverlappingPanels(List<ComicPanel> candidates)
    {
        if (candidates.Count <= 1) return candidates;

        var sorted = candidates
            .OrderByDescending(p => p.Confidence)
            .ThenByDescending(p => p.Width * p.Height)
            .ToList();
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
                    double existingArea = existing.Width * existing.Height;
                    if (intersectionArea / pArea > 0.7 || intersectionArea / existingArea > 0.85)
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

    private List<ComicPanel> DetectEvidenceLayoutCandidates(int pageIndex, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        using var darkMask = CreateDarkSeparatorMask(grayFull);
        var horizontalSeparators = FindEvidenceHorizontalSeparators(grayFull, edges, darkMask, imgW, imgH);
        var rowBands = BuildSegments(horizontalSeparators, imgH, Math.Max(40, imgH / 12));
        return CreateLayoutCandidatesFromBands(
            rowBands,
            imgH,
            (rowStart, rowEnd) =>
            {
                var rowSupport = GetEvidenceRowSupport(rowStart, rowEnd, grayFull, edges, imgW, imgH);
                if (!rowSupport.HasStrongSupport)
                {
                    return [];
                }

                var verticalSeparators = FindEvidenceVerticalSeparators(grayFull, edges, darkMask, imgW, rowStart, rowEnd);
                double separatorSupport = rowSupport.SeparatorSupport;
                return CreateLayoutPanelsForRow(
                    pageIndex,
                    rowStart,
                    rowEnd,
                    verticalSeparators,
                    imgW,
                    imgH,
                    pageArea,
                    rect =>
                    {
                        var stats = MeasureCandidateStats(rect, grayFull, edges, imgW, imgH, pageArea);
                        double confidence =
                            (stats.GutterContrast * 0.24) +
                            (stats.BorderEdgeDensity * 0.20) +
                            (stats.InteriorVarianceScore * 0.15) +
                            (stats.AreaScore * 0.10) +
                            (stats.EdgeTouchScore * 0.06) +
                            (separatorSupport * 0.15) +
                            0.16;

                        return confidence >= 0.43 ? confidence : null;
                    });
            },
            normalizeRows: true,
            minBandCount: 2,
            minRowCount: 2,
            minPanelCount: 0);
    }

    private List<ComicPanel> DetectBorderLayoutCandidates(int pageIndex, Mat grayFull, int imgW, int imgH, double pageArea)
    {
        using var darkMask = CreateDarkSeparatorMask(grayFull);

        var horizontalSeparators = FindHorizontalProjectionSeparators(darkMask, imgW, imgH);
        var rowBands = BuildSegments(horizontalSeparators, imgH, Math.Max(40, imgH / 12));
        return CreateLayoutCandidatesFromBands(
            rowBands,
            imgH,
            (rowStart, rowEnd) =>
            {
                var verticalSeparators = FindVerticalProjectionSeparators(darkMask, imgW, rowStart, rowEnd);
                return CreateLayoutPanelsForRow(
                    pageIndex,
                    rowStart,
                    rowEnd,
                    verticalSeparators,
                    imgW,
                    imgH,
                    pageArea,
                    _ => 0.80);
            },
            normalizeRows: false,
            minBandCount: 2,
            minRowCount: 0,
            minPanelCount: 3);
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

    private static List<(int Start, int End)> FindHorizontalGutters(Mat grayFull, Mat edges, int imgW, int imgH)
    {
        var gutters = new List<(int Start, int End)>();
        int? runStart = null;
        for (int y = 0; y < imgH; y++)
        {
            bool isGutter = IsHorizontalGutterRow(grayFull, edges, y, imgW);
            if (isGutter)
            {
                runStart ??= y;
            }
            else if (runStart.HasValue)
            {
                int runEnd = y;
                if (runEnd - runStart.Value >= Math.Max(6, imgH / 120))
                {
                    gutters.Add((runStart.Value, runEnd));
                }

                runStart = null;
            }
        }

        if (runStart.HasValue)
        {
            int runEnd = imgH;
            if (runEnd - runStart.Value >= Math.Max(6, imgH / 120))
            {
                gutters.Add((runStart.Value, runEnd));
            }
        }

        return gutters;
    }

    private static List<(int Start, int End)> FindStrongProjectionSeparators(Mat darkMask, bool horizontal, int scanLength, int sliceStart, int sliceLength, double minPeakRatio)
    {
        if (scanLength <= 0 || sliceLength <= 0)
        {
            return [];
        }

        var ratios = new double[scanLength];
        for (int i = 0; i < scanLength; i++)
        {
            if (horizontal)
            {
                using var line = new Mat(darkMask, new Rect(0, i, sliceLength, 1));
                ratios[i] = Cv2.CountNonZero(line) / (double)sliceLength;
            }
            else
            {
                using var line = new Mat(darkMask, new Rect(i, sliceStart, 1, sliceLength));
                ratios[i] = Cv2.CountNonZero(line) / (double)sliceLength;
            }
        }

        var smoothed = SmoothProjection(ratios);
        var bands = new List<(int Start, int End)>();

        for (int i = 1; i < smoothed.Length - 1; i++)
        {
            double value = smoothed[i];
            if (value < minPeakRatio || value < smoothed[i - 1] || value < smoothed[i + 1])
            {
                continue;
            }

            double bandThreshold = Math.Max(minPeakRatio * 0.60, 0.25);
            int bandStart = i;
            while (bandStart > 0 && smoothed[bandStart - 1] >= bandThreshold)
            {
                bandStart--;
            }

            int bandEnd = i;
            while (bandEnd < smoothed.Length - 1 && smoothed[bandEnd + 1] >= bandThreshold)
            {
                bandEnd++;
            }

            var band = (bandStart, bandEnd + 1);
            if (bands.Count == 0 || band.Item1 > bands[^1].End)
            {
                bands.Add(band);
            }
        }

        if (bands.Count == 0)
        {
            int? runStart = null;
            for (int i = 0; i < smoothed.Length; i++)
            {
                if (smoothed[i] >= minPeakRatio)
                {
                    runStart ??= i;
                }
                else if (runStart.HasValue)
                {
                    bands.Add((runStart.Value, i));
                    runStart = null;
                }
            }

            if (runStart.HasValue)
            {
                bands.Add((runStart.Value, scanLength));
            }
        }

        return MergeNearbyBands(bands, horizontal ? 12 : 18);
    }

    private static double[] SmoothProjection(double[] values)
    {
        if (values.Length <= 2)
        {
            return values;
        }

        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double sum = 0;
            int count = 0;
            for (int offset = -2; offset <= 2; offset++)
            {
                int index = i + offset;
                if (index < 0 || index >= values.Length)
                {
                    continue;
                }

                sum += values[index];
                count++;
            }

            result[i] = sum / count;
        }

        return result;
    }

    private static List<(int Start, int End)> MergeNearbyBands(List<(int Start, int End)> bands, int maxGap)
    {
        if (bands.Count <= 1)
        {
            return bands;
        }

        var merged = new List<(int Start, int End)>();
        var ordered = bands.OrderBy(band => band.Start).ToList();
        var current = ordered[0];
        for (int i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            if (next.Start - current.End <= maxGap)
            {
                current = (current.Start, Math.Max(current.End, next.End));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    private static Mat CreateDarkSeparatorMask(Mat grayFull)
    {
        var darkMask = new Mat();
        Cv2.AdaptiveThreshold(grayFull, darkMask, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 21, 7);

        using var cleanupKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.MorphologyEx(darkMask, darkMask, MorphTypes.Close, cleanupKernel, iterations: 1);
        return darkMask;
    }

    private static List<(int Start, int End)> FindHorizontalProjectionSeparators(Mat darkMask, int imgW, int imgH)
    {
        return FindStrongProjectionSeparators(darkMask, horizontal: true, scanLength: imgH, sliceStart: 0, sliceLength: imgW, minPeakRatio: 0.55);
    }

    private static List<(int Start, int End)> FindVerticalProjectionSeparators(Mat darkMask, int imgW, int rowStart, int rowEnd)
    {
        return FindStrongProjectionSeparators(
            darkMask,
            horizontal: false,
            scanLength: imgW,
            sliceStart: rowStart,
            sliceLength: Math.Max(1, rowEnd - rowStart),
            minPeakRatio: 0.60);
    }

    private List<(int Start, int End)> FindEvidenceHorizontalSeparators(Mat grayFull, Mat edges, Mat darkMask, int imgW, int imgH)
    {
        var separators = new List<(int Start, int End)>();
        separators.AddRange(FindHorizontalGuttersForLayout(grayFull, edges, imgW, imgH));
        separators.AddRange(FindStrongScoreBands(
            imgH,
            y => ScoreHorizontalBorderRow(grayFull, edges, y, 0, imgW),
            threshold: 0.34,
            minRunLength: Math.Max(2, imgH / 220),
            mergeGap: 10));
        separators.AddRange(FindHorizontalProjectionSeparators(darkMask, imgW, imgH));
        return MergeNearbyBands(separators.OrderBy(band => band.Start).ToList(), 12);
    }

    private List<(int Start, int End)> FindEvidenceVerticalSeparators(Mat grayFull, Mat edges, Mat darkMask, int imgW, int rowStart, int rowEnd)
    {
        var separators = new List<(int Start, int End)>();
        separators.AddRange(FindVerticalGuttersForLayout(grayFull, edges, imgW, rowStart, rowEnd));
        separators.AddRange(FindStrongScoreBands(
            imgW,
            x => ScoreVerticalBorderColumn(grayFull, edges, x, rowStart, rowEnd),
            threshold: 0.31,
            minRunLength: Math.Max(2, imgW / 220),
            mergeGap: 8));
        separators.AddRange(FindVerticalProjectionSeparators(darkMask, imgW, rowStart, rowEnd));

        var merged = MergeNearbyBands(separators.OrderBy(band => band.Start).ToList(), 10);
        return merged
            .Where(band => (band.End - band.Start) >= Math.Max(2, imgW / 280))
            .ToList();
    }

    private static List<(int Start, int End)> FindStrongScoreBands(int scanLength, Func<int, double> scorer, double threshold, int minRunLength, int mergeGap)
    {
        var bands = new List<(int Start, int End)>();
        int? runStart = null;
        for (int i = 0; i < scanLength; i++)
        {
            if (scorer(i) >= threshold)
            {
                runStart ??= i;
            }
            else if (runStart.HasValue)
            {
                if (i - runStart.Value >= minRunLength)
                {
                    bands.Add((runStart.Value, i));
                }

                runStart = null;
            }
        }

        if (runStart.HasValue && scanLength - runStart.Value >= minRunLength)
        {
            bands.Add((runStart.Value, scanLength));
        }

        return MergeNearbyBands(bands, mergeGap);
    }

    private static List<(int Start, int End)> FindHorizontalGuttersForLayout(Mat grayFull, Mat edges, int imgW, int imgH)
    {
        var gutters = new List<(int Start, int End)>();
        int? runStart = null;
        for (int y = 0; y < imgH; y++)
        {
            bool isGutter = IsHorizontalLayoutGutterRow(grayFull, edges, y, imgW);
            if (isGutter)
            {
                runStart ??= y;
            }
            else if (runStart.HasValue)
            {
                int runEnd = y;
                if (runEnd - runStart.Value >= Math.Max(4, imgH / 180))
                {
                    gutters.Add((runStart.Value, runEnd));
                }

                runStart = null;
            }
        }

        if (runStart.HasValue)
        {
            int runEnd = imgH;
            if (runEnd - runStart.Value >= Math.Max(4, imgH / 180))
            {
                gutters.Add((runStart.Value, runEnd));
            }
        }

        return gutters;
    }

    private static List<(int Start, int End)> FindVerticalGutters(Mat grayFull, Mat edges, int imgW, int rowStart, int rowEnd)
    {
        var gutters = new List<(int Start, int End)>();
        int? runStart = null;
        for (int x = 0; x < imgW; x++)
        {
            bool isGutter = IsVerticalGutterColumn(grayFull, edges, x, rowStart, rowEnd);
            if (isGutter)
            {
                runStart ??= x;
            }
            else if (runStart.HasValue)
            {
                int runEnd = x;
                if (runEnd - runStart.Value >= Math.Max(6, imgW / 160))
                {
                    gutters.Add((runStart.Value, runEnd));
                }

                runStart = null;
            }
        }

        if (runStart.HasValue)
        {
            int runEnd = imgW;
            if (runEnd - runStart.Value >= Math.Max(6, imgW / 160))
            {
                gutters.Add((runStart.Value, runEnd));
            }
        }

        return gutters;
    }

    private static List<(int Start, int End)> FindVerticalGuttersForLayout(Mat grayFull, Mat edges, int imgW, int rowStart, int rowEnd)
    {
        var gutters = new List<(int Start, int End)>();
        int? runStart = null;
        for (int x = 0; x < imgW; x++)
        {
            bool isGutter = IsVerticalLayoutGutterColumn(grayFull, edges, x, rowStart, rowEnd);
            if (isGutter)
            {
                runStart ??= x;
            }
            else if (runStart.HasValue)
            {
                int runEnd = x;
                if (runEnd - runStart.Value >= Math.Max(4, imgW / 220))
                {
                    gutters.Add((runStart.Value, runEnd));
                }

                runStart = null;
            }
        }

        if (runStart.HasValue)
        {
            int runEnd = imgW;
            if (runEnd - runStart.Value >= Math.Max(4, imgW / 220))
            {
                gutters.Add((runStart.Value, runEnd));
            }
        }

        return gutters;
    }

    private static bool IsHorizontalGutterRow(Mat grayFull, Mat edges, int y, int imgW)
    {
        int brightPixels = 0;
        int edgePixels = 0;

        for (int x = 0; x < imgW; x++)
        {
            if (grayFull.At<byte>(y, x) >= 220)
            {
                brightPixels++;
            }

            if (edges.At<byte>(y, x) > 0)
            {
                edgePixels++;
            }
        }

        double brightRatio = brightPixels / (double)imgW;
        double edgeRatio = edgePixels / (double)imgW;
        return brightRatio >= 0.85 && edgeRatio <= 0.06;
    }

    private static bool IsHorizontalLayoutGutterRow(Mat grayFull, Mat edges, int y, int imgW)
    {
        int brightPixels = 0;
        int edgePixels = 0;
        int veryDarkPixels = 0;

        for (int x = 0; x < imgW; x++)
        {
            byte pixel = grayFull.At<byte>(y, x);
            if (pixel >= 200)
            {
                brightPixels++;
            }

            if (pixel <= 80)
            {
                veryDarkPixels++;
            }

            if (edges.At<byte>(y, x) > 0)
            {
                edgePixels++;
            }
        }

        double brightRatio = brightPixels / (double)imgW;
        double darkRatio = veryDarkPixels / (double)imgW;
        double edgeRatio = edgePixels / (double)imgW;
        return brightRatio >= 0.68 && darkRatio <= 0.14 && edgeRatio <= 0.18;
    }

    private static bool IsVerticalGutterColumn(Mat grayFull, Mat edges, int x, int rowStart, int rowEnd)
    {
        int brightPixels = 0;
        int edgePixels = 0;
        int height = Math.Max(1, rowEnd - rowStart);

        for (int y = rowStart; y < rowEnd; y++)
        {
            if (grayFull.At<byte>(y, x) >= 220)
            {
                brightPixels++;
            }

            if (edges.At<byte>(y, x) > 0)
            {
                edgePixels++;
            }
        }

        double brightRatio = brightPixels / (double)height;
        double edgeRatio = edgePixels / (double)height;
        return brightRatio >= 0.80 && edgeRatio <= 0.08;
    }

    private static bool IsVerticalLayoutGutterColumn(Mat grayFull, Mat edges, int x, int rowStart, int rowEnd)
    {
        int brightPixels = 0;
        int edgePixels = 0;
        int veryDarkPixels = 0;
        int height = Math.Max(1, rowEnd - rowStart);

        for (int y = rowStart; y < rowEnd; y++)
        {
            byte pixel = grayFull.At<byte>(y, x);
            if (pixel >= 195)
            {
                brightPixels++;
            }

            if (pixel <= 80)
            {
                veryDarkPixels++;
            }

            if (edges.At<byte>(y, x) > 0)
            {
                edgePixels++;
            }
        }

        double brightRatio = brightPixels / (double)height;
        double darkRatio = veryDarkPixels / (double)height;
        double edgeRatio = edgePixels / (double)height;
        return brightRatio >= 0.64 && darkRatio <= 0.18 && edgeRatio <= 0.22;
    }

    private static List<(int Start, int End)> BuildSegments(List<(int Start, int End)> gutters, int totalLength, int minSegmentSize)
    {
        var segments = new List<(int Start, int End)>();
        int currentStart = 0;

        foreach (var (gutterStart, gutterEnd) in gutters.OrderBy(gutter => gutter.Start))
        {
            if (gutterStart - currentStart >= minSegmentSize)
            {
                segments.Add((currentStart, gutterStart));
            }

            currentStart = gutterEnd;
        }

        if (totalLength - currentStart >= minSegmentSize)
        {
            segments.Add((currentStart, totalLength));
        }

        return segments;
    }

    private List<ComicPanel> CreateLayoutPanelsForRow(
        int pageIndex,
        int rowStart,
        int rowEnd,
        List<(int Start, int End)> verticalSeparators,
        int imgW,
        int imgH,
        double pageArea,
        Func<Rect, double?> confidenceSelector)
    {
        int rowHeight = rowEnd - rowStart;
        var columnBands = BuildSegments(verticalSeparators, imgW, Math.Max(40, imgW / 10));
        if (columnBands.Count == 0)
        {
            return [];
        }

        var rowPanels = new List<ComicPanel>();
        foreach (var (columnStart, columnEnd) in columnBands)
        {
            var rect = new Rect(columnStart, rowStart, columnEnd - columnStart, rowHeight);
            if (!IsSensibleCandidate(rect, imgW, imgH, pageArea))
            {
                continue;
            }

            double? confidence = confidenceSelector(rect);
            if (confidence.HasValue)
            {
                rowPanels.Add(CreatePanel(pageIndex, rect, imgW, imgH, confidence.Value));
            }
        }

        return rowPanels;
    }

    private static List<LayoutRowCandidate> CreateLayoutRowsFromBands(
        List<(int Start, int End)> rowBands,
        int imgH,
        Func<int, int, List<ComicPanel>> rowPanelFactory)
    {
        var rows = new List<LayoutRowCandidate>();
        foreach (var (rowStart, rowEnd) in rowBands)
        {
            int rowHeight = rowEnd - rowStart;
            if (rowHeight < Math.Max(40, imgH / 14))
            {
                continue;
            }

            var rowPanels = rowPanelFactory(rowStart, rowEnd);
            if (rowPanels.Count > 0)
            {
                rows.Add(new LayoutRowCandidate(rowStart / (double)imgH, rowHeight / (double)imgH, rowPanels));
            }
        }

        return rows;
    }

    private static List<ComicPanel> CreateLayoutCandidatesFromBands(
        List<(int Start, int End)> rowBands,
        int imgH,
        Func<int, int, List<ComicPanel>> rowPanelFactory,
        bool normalizeRows,
        int minBandCount,
        int minRowCount,
        int minPanelCount)
    {
        if (rowBands.Count < minBandCount)
        {
            return [];
        }

        var rows = CreateLayoutRowsFromBands(rowBands, imgH, rowPanelFactory);
        if (rows.Count < minRowCount)
        {
            return [];
        }

        if (normalizeRows)
        {
            rows = NormalizeLayoutRows(rows);
        }

        var candidates = rows.SelectMany(row => row.Panels).ToList();
        return candidates.Count >= minPanelCount ? candidates : [];
    }


    private List<ComicPanel> RefinePanelsToLocalGutters(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        if (panels.Count < 5)
        {
            return panels;
        }

        var rows = GroupPanelsIntoRows(panels);
        var adjustedPanels = new List<ComicPanel>();
        foreach (var row in rows)
        {
            row.Panels.Sort((left, right) => left.X.CompareTo(right.X));
            int rowStart = Math.Max(0, (int)Math.Round(row.MinY * imgH));
            int rowEnd = Math.Min(imgH, (int)Math.Round(row.MaxY * imgH));
            var rowPanels = row.Panels
                .Select(panel => new ComicPanel
                {
                    PageIndex = panel.PageIndex,
                    PanelIndex = panel.PanelIndex,
                    X = panel.X,
                    Y = panel.Y,
                    Width = panel.Width,
                    Height = panel.Height,
                    Confidence = panel.Confidence
                })
                .ToList();

            for (int i = 0; i < rowPanels.Count - 1; i++)
            {
                var leftPanel = rowPanels[i];
                var rightPanel = rowPanels[i + 1];
                var gutterRun = FindLocalGutterRun(leftPanel, rightPanel, rowStart, rowEnd, grayFull, edges, imgW, imgH, pageArea);
                if (gutterRun is null)
                {
                    continue;
                }

                var (gutterStart, gutterEnd) = gutterRun.Value;
                double leftX = leftPanel.X;
                double leftY = leftPanel.Y;
                double leftHeight = leftPanel.Height;
                double leftNewWidth = gutterStart - leftX;
                double rightRight = rightPanel.X + rightPanel.Width;
                double rightNewX = gutterEnd;
                double rightNewWidth = rightRight - rightNewX;

                if (leftNewWidth <= MinPanelSizeRatio || rightNewWidth <= MinPanelSizeRatio)
                {
                    continue;
                }

                rowPanels[i] = CreateAdjustedPanel(leftPanel, leftX, leftY, leftNewWidth, leftHeight);
                rowPanels[i + 1] = CreateAdjustedPanel(rightPanel, rightNewX, rightPanel.Y, rightNewWidth, rightPanel.Height);
            }

            adjustedPanels.AddRange(rowPanels);
        }

        return RemoveNestedInsetPanels(FilterOverlappingPanels(adjustedPanels));
    }

    private List<ComicPanel> RefineRowsToHorizontalBorders(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH)
    {
        var rows = GroupPanelsIntoRows(panels)
            .OrderBy(row => row.MinY)
            .ToList();
        if (rows.Count <= 1)
        {
            return panels;
        }

        int searchPadding = Math.Max(18, imgH / 12);
        var topAnchors = new int[rows.Count];
        var bottomAnchors = new int[rows.Count];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int currentTop = Math.Max(0, (int)Math.Round(row.MinY * imgH));
            int currentBottom = Math.Min(imgH, (int)Math.Round(row.MaxY * imgH));
            int previousBottom = i > 0
                ? Math.Max(0, (int)Math.Round(rows[i - 1].MaxY * imgH))
                : currentTop;
            int nextTop = i < rows.Count - 1
                ? Math.Min(imgH, (int)Math.Round(rows[i + 1].MinY * imgH))
                : currentBottom;
            int xStart = Math.Max(0, (int)Math.Round(row.Panels.Min(panel => panel.X) * imgW) - Math.Max(12, imgW / 40));
            int xEnd = Math.Min(imgW, (int)Math.Round(row.Panels.Max(panel => panel.X + panel.Width) * imgW) + Math.Max(12, imgW / 40));

            int topSearchStart = i > 0
                ? Math.Max(0, previousBottom - searchPadding)
                : Math.Max(0, currentTop - searchPadding);
            int topSearchEnd = Math.Min(imgH, currentTop + searchPadding);
            int bottomSearchStart = Math.Max(0, currentBottom - searchPadding);
            int bottomSearchEnd = i < rows.Count - 1
                ? Math.Min(imgH, nextTop + searchPadding)
                : Math.Min(imgH, currentBottom + searchPadding);

            topAnchors[i] = FindBestHorizontalBorder(topSearchStart, topSearchEnd, xStart, xEnd, grayFull, edges) ?? currentTop;
            bottomAnchors[i] = FindBestHorizontalBorder(bottomSearchStart, bottomSearchEnd, xStart, xEnd, grayFull, edges) ?? currentBottom;
        }

        var boundaries = new int[rows.Count + 1];
        boundaries[0] = topAnchors[0];
        boundaries[^1] = bottomAnchors[^1];
        for (int i = 0; i < rows.Count - 1; i++)
        {
            int sharedBoundary = (int)Math.Round((bottomAnchors[i] + topAnchors[i + 1]) / 2.0);
            sharedBoundary = Math.Max(boundaries[i] + 1, sharedBoundary);
            boundaries[i + 1] = sharedBoundary;
        }

        for (int i = 1; i < boundaries.Length; i++)
        {
            boundaries[i] = Math.Max(boundaries[i], boundaries[i - 1] + 1);
        }

        var adjustedPanels = new List<ComicPanel>();
        for (int i = 0; i < rows.Count; i++)
        {
            double newY = boundaries[i] / (double)imgH;
            double newHeight = (boundaries[i + 1] - boundaries[i]) / (double)imgH;
            if (newHeight <= MinPanelSizeRatio)
            {
                adjustedPanels.AddRange(rows[i].Panels);
                continue;
            }

            adjustedPanels.AddRange(rows[i].Panels.Select(panel =>
                CreateAdjustedPanel(panel, panel.X, newY, panel.Width, newHeight)));
        }

        return FilterOverlappingPanels(adjustedPanels);
    }

    private List<ComicPanel> RefineBorderLayoutColumns(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        var refinedPanels = new List<ComicPanel>();
        foreach (var row in GroupPanelsIntoRowsForWideSplit(panels).OrderBy(row => row.MinY))
        {
            var rowPanels = row.Panels
                .OrderBy(panel => panel.X)
                .Select(panel => CreateAdjustedPanel(panel, panel.X, panel.Y, panel.Width, panel.Height))
                .ToList();
            if (rowPanels.Count == 0)
            {
                continue;
            }

            int rowStart = Math.Max(0, (int)Math.Round(rowPanels.Min(panel => panel.Y) * imgH));
            int rowEnd = Math.Min(imgH, (int)Math.Round(rowPanels.Max(panel => panel.Y + panel.Height) * imgH));

            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < rowPanels.Count - 1; i++)
                {
                    var leftPanel = rowPanels[i];
                    var rightPanel = rowPanels[i + 1];
                    var gutterRun = FindLocalGutterRun(leftPanel, rightPanel, rowStart, rowEnd, grayFull, edges, imgW, imgH, pageArea);
                    if (gutterRun is not null || !ShouldMergeAdjacentBorderPanels(leftPanel, rightPanel, rowPanels.Count))
                    {
                        continue;
                    }

                    double mergedX = leftPanel.X;
                    double mergedY = Math.Min(leftPanel.Y, rightPanel.Y);
                    double mergedRight = Math.Max(leftPanel.X + leftPanel.Width, rightPanel.X + rightPanel.Width);
                    double mergedBottom = Math.Max(leftPanel.Y + leftPanel.Height, rightPanel.Y + rightPanel.Height);
                    rowPanels[i] = CreateAdjustedPanel(leftPanel, mergedX, mergedY, mergedRight - mergedX, mergedBottom - mergedY);
                    rowPanels.RemoveAt(i + 1);
                    merged = true;
                    break;
                }
            } while (merged);

            int searchPadding = Math.Max(20, imgW / 8);
            int leftEdge = (int)Math.Round(rowPanels[0].X * imgW);
            int rightEdge = (int)Math.Round((rowPanels[^1].X + rowPanels[^1].Width) * imgW);

            int snappedLeft = FindBestVerticalBorder(
                Math.Max(0, leftEdge - searchPadding),
                Math.Min(imgW, leftEdge + Math.Max(20, searchPadding / 2)),
                rowStart,
                rowEnd,
                grayFull,
                edges) ?? leftEdge;

            int snappedRight = FindBestVerticalBorder(
                Math.Max(0, rightEdge - Math.Max(20, searchPadding / 2)),
                Math.Min(imgW, rightEdge + searchPadding),
                rowStart,
                rowEnd,
                grayFull,
                edges) ?? rightEdge;

            if (snappedLeft < leftEdge)
            {
                var first = rowPanels[0];
                double newX = snappedLeft / (double)imgW;
                double newWidth = (first.X + first.Width) - newX;
                if (newWidth > MinPanelSizeRatio)
                {
                    rowPanels[0] = CreateAdjustedPanel(first, newX, first.Y, newWidth, first.Height);
                }
            }

            if (snappedRight > rightEdge)
            {
                var last = rowPanels[^1];
                double newRight = snappedRight / (double)imgW;
                double newWidth = newRight - last.X;
                if (newWidth > MinPanelSizeRatio)
                {
                    rowPanels[^1] = CreateAdjustedPanel(last, last.X, last.Y, newWidth, last.Height);
                }
            }

            refinedPanels.AddRange(rowPanels);
        }

        return RemoveNestedInsetPanels(FilterOverlappingPanels(refinedPanels));
    }

    private List<ComicPanel> RefinePanelsToValidatedSeparators(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        var refinedPanels = new List<ComicPanel>();
        foreach (var row in GroupPanelsIntoRowsForWideSplit(panels).OrderBy(row => row.MinY))
        {
            var rowPanels = row.Panels
                .OrderBy(panel => panel.X)
                .Select(panel => CreateAdjustedPanel(panel, panel.X, panel.Y, panel.Width, panel.Height))
                .ToList();
            if (rowPanels.Count <= 1)
            {
                refinedPanels.AddRange(rowPanels);
                continue;
            }

            int rowStart = Math.Max(0, (int)Math.Round(rowPanels.Min(panel => panel.Y) * imgH));
            int rowEnd = Math.Min(imgH, (int)Math.Round(rowPanels.Max(panel => panel.Y + panel.Height) * imgH));

            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < rowPanels.Count - 1; i++)
                {
                    var leftPanel = rowPanels[i];
                    var rightPanel = rowPanels[i + 1];
                    if (HasValidatedVerticalSeparator(leftPanel, rightPanel, rowStart, rowEnd, grayFull, edges, imgW, imgH, pageArea))
                    {
                        continue;
                    }

                    bool shouldMerge = rowPanels.Count >= 3 || ShouldMergeAdjacentBorderPanels(leftPanel, rightPanel, rowPanels.Count);
                    if (!shouldMerge)
                    {
                        continue;
                    }

                    double mergedX = leftPanel.X;
                    double mergedY = Math.Min(leftPanel.Y, rightPanel.Y);
                    double mergedRight = Math.Max(leftPanel.X + leftPanel.Width, rightPanel.X + rightPanel.Width);
                    double mergedBottom = Math.Max(leftPanel.Y + leftPanel.Height, rightPanel.Y + rightPanel.Height);
                    rowPanels[i] = CreateAdjustedPanel(leftPanel, mergedX, mergedY, mergedRight - mergedX, mergedBottom - mergedY);
                    rowPanels.RemoveAt(i + 1);
                    merged = true;
                    break;
                }
            } while (merged);

            refinedPanels.AddRange(rowPanels);
        }

        return RemoveNestedInsetPanels(FilterOverlappingPanels(refinedPanels));
    }

    private List<ComicPanel> RefinePanelsToHorizontalBorders(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH)
    {
        var rows = new List<PanelRow>();
        const int rowSnapTolerance = 3;
        foreach (var panel in panels.OrderBy(panel => panel.Y).ThenBy(panel => panel.X))
        {
            int panelTop = (int)Math.Round(panel.Y * imgH);
            int panelBottom = (int)Math.Round((panel.Y + panel.Height) * imgH);
            var existingRow = rows.FirstOrDefault(row =>
                Math.Abs((int)Math.Round(row.MinY * imgH) - panelTop) <= rowSnapTolerance &&
                Math.Abs((int)Math.Round(row.MaxY * imgH) - panelBottom) <= rowSnapTolerance);
            if (existingRow is null)
            {
                rows.Add(new PanelRow(panel));
            }
            else
            {
                existingRow.MinY = Math.Min(existingRow.MinY, panel.Y);
                existingRow.MaxY = Math.Max(existingRow.MaxY, panel.Y + panel.Height);
                existingRow.Coverage += panel.Width;
                existingRow.Panels.Add(panel);
            }
        }

        rows = rows
            .OrderBy(row => row.MinY)
            .ToList();
        if (rows.Count == 0)
        {
            return panels;
        }

        int minPanelHeightPixels = Math.Max(24, (int)Math.Round(MinPanelSizeRatio * imgH));
        var adjustedPanels = new List<ComicPanel>();

        foreach (var row in rows)
        {
            int rowTop = Math.Max(0, (int)Math.Round(row.MinY * imgH));
            int rowBottom = Math.Min(imgH, (int)Math.Round(row.MaxY * imgH));
            int rowHeight = Math.Max(1, rowBottom - rowTop);
            int topPadding = Math.Max(18, rowHeight / 3);
            int bottomPadding = Math.Max(18, rowHeight / 3);

            foreach (var panel in row.Panels)
            {
                int panelLeft = Math.Max(0, (int)Math.Round(panel.X * imgW));
                int panelRight = Math.Min(imgW, (int)Math.Round((panel.X + panel.Width) * imgW));
                int panelWidth = Math.Max(1, panelRight - panelLeft);
                int xPadding = Math.Max(8, panelWidth / 14);
                int xStart = Math.Max(0, panelLeft + xPadding / 2);
                int xEnd = Math.Min(imgW, panelRight - xPadding / 2);
                if (xEnd <= xStart)
                {
                    adjustedPanels.Add(panel);
                    continue;
                }

                int currentTop = Math.Max(rowTop, (int)Math.Round(panel.Y * imgH));
                int currentBottom = Math.Min(rowBottom, (int)Math.Round((panel.Y + panel.Height) * imgH));

                int topSearchStart = rowTop;
                int topSearchEnd = Math.Min(rowBottom - minPanelHeightPixels, currentTop + topPadding);
                int bottomSearchStart = Math.Max(rowTop + minPanelHeightPixels, currentBottom - bottomPadding);
                int bottomSearchEnd = rowBottom;

                int snappedTop = FindBestHorizontalBorder(topSearchStart, topSearchEnd, xStart, xEnd, grayFull, edges) ?? currentTop;
                int snappedBottom = FindBestHorizontalBorder(bottomSearchStart, bottomSearchEnd, xStart, xEnd, grayFull, edges) ?? currentBottom;

                snappedTop = Math.Max(rowTop, Math.Min(snappedTop, rowBottom - minPanelHeightPixels));
                snappedBottom = Math.Min(rowBottom, Math.Max(snappedBottom, snappedTop + minPanelHeightPixels));

                double newY = snappedTop / (double)imgH;
                double newHeight = (snappedBottom - snappedTop) / (double)imgH;
                if (newHeight <= MinPanelSizeRatio)
                {
                    adjustedPanels.Add(panel);
                    continue;
                }

                adjustedPanels.Add(CreateAdjustedPanel(panel, panel.X, newY, panel.Width, newHeight));
            }
        }

        return RemoveNestedInsetPanels(FilterOverlappingPanels(adjustedPanels));
    }

    private List<ComicPanel> RefinePanelsAcrossVerticalGaps(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH)
    {
        if (panels.Count <= 1)
        {
            return panels;
        }

        const double minGapRatio = 0.08;
        const double minConfidence = 0.62;
        int minPanelHeightPixels = Math.Max(24, (int)Math.Round(MinPanelSizeRatio * imgH));

        var adjustedPanels = panels
            .Select(panel => CreateAdjustedPanel(panel, panel.X, panel.Y, panel.Width, panel.Height))
            .ToList();

        for (int i = 0; i < adjustedPanels.Count; i++)
        {
            var panel = adjustedPanels[i];
            if (panel.Confidence < minConfidence)
            {
                continue;
            }

            double panelBottom = panel.Y + panel.Height;
            bool hasAlignedRowPeer = panels
                .Select((other, index) => (other, index))
                .Any(candidate => candidate.index != i &&
                    GetHorizontalOverlapRatio(panel, candidate.other) <= 0.10 &&
                    Math.Abs(candidate.other.Y - panel.Y) <= 0.04 &&
                    Math.Abs((candidate.other.Y + candidate.other.Height) - panelBottom) <= 0.05);
            if (hasAlignedRowPeer)
            {
                continue;
            }

            var nextOverlappingPanel = panels
                .Where((other, index) => index != i &&
                    other.Y >= panelBottom - 0.01 &&
                    GetHorizontalOverlapRatio(panel, other) >= 0.20)
                .OrderBy(other => other.Y)
                .FirstOrDefault();

            if (nextOverlappingPanel is null)
            {
                continue;
            }

            double nextTop = nextOverlappingPanel.Y;
            double gap = nextTop - panelBottom;
            if (gap < minGapRatio)
            {
                continue;
            }

            int panelTopPx = Math.Max(0, (int)Math.Round(panel.Y * imgH));
            int panelBottomPx = Math.Min(imgH, (int)Math.Round(panelBottom * imgH));
            int nextTopPx = Math.Max(panelBottomPx + 1, (int)Math.Round(nextTop * imgH));
            int panelLeftPx = Math.Max(0, (int)Math.Round(panel.X * imgW));
            int panelRightPx = Math.Min(imgW, (int)Math.Round((panel.X + panel.Width) * imgW));
            int xPadding = Math.Max(8, (panelRightPx - panelLeftPx) / 14);
            int xStart = Math.Max(0, panelLeftPx + xPadding / 2);
            int xEnd = Math.Min(imgW, panelRightPx - xPadding / 2);
            if (xEnd <= xStart || nextTopPx <= panelBottomPx + 1)
            {
                continue;
            }

            double currentBottomSupport = GetMaxHorizontalBorderScore(
                Math.Max(0, panelBottomPx - Math.Max(6, imgH / 240)),
                Math.Min(imgH, panelBottomPx + Math.Max(7, imgH / 220)),
                xStart,
                xEnd,
                grayFull,
                edges);
            if (currentBottomSupport >= 0.24)
            {
                continue;
            }

            int? snappedBottom = FindBestHorizontalBorder(panelBottomPx, nextTopPx, xStart, xEnd, grayFull, edges);
            if (!snappedBottom.HasValue || snappedBottom.Value <= panelBottomPx + minPanelHeightPixels / 3)
            {
                continue;
            }

            int newBottomPx = Math.Min(nextTopPx - 1, snappedBottom.Value);
            if (newBottomPx - panelTopPx < minPanelHeightPixels)
            {
                continue;
            }

            adjustedPanels[i] = CreateAdjustedPanel(
                panel,
                panel.X,
                panel.Y,
                panel.Width,
                (newBottomPx - panelTopPx) / (double)imgH);
        }

        return RemoveNestedInsetPanels(FilterOverlappingPanels(adjustedPanels));
    }

    private List<ComicPanel> RefinePanelsFromUncoveredRegions(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        if (panels.Count < 3)
        {
            return panels;
        }

        var adjustedPanels = new List<ComicPanel>(panels);
        foreach (var row in GroupPanelsIntoRowsForWideSplit(panels).OrderBy(row => row.MinY))
        {
            var rowPanels = row.Panels
                .OrderBy(panel => panel.X)
                .ToList();
            if (rowPanels.Count < 2 || row.Coverage >= 0.84)
            {
                continue;
            }

            int rowStart = Math.Max(0, (int)Math.Round(row.MinY * imgH));
            int rowEnd = Math.Min(imgH, (int)Math.Round(row.MaxY * imgH));
            foreach (var gap in FindRowGapCandidates(rowPanels))
            {
                int gapStart = Math.Max(0, (int)Math.Round(gap.Start * imgW));
                int gapEnd = Math.Min(imgW, (int)Math.Round(gap.End * imgW));
                var rect = new Rect(gapStart, rowStart, Math.Max(1, gapEnd - gapStart), Math.Max(1, rowEnd - rowStart));
                var candidate = CreateRecoveredCandidate(rowPanels[0].PageIndex, rect, adjustedPanels, 0.45, 0.22, 0.48, grayFull, edges, imgW, imgH, pageArea);
                if (candidate is not null)
                {
                    adjustedPanels.Add(candidate);
                    break;
                }
            }
        }

        return RemoveNestedInsetPanels(FilterOverlappingPanels(adjustedPanels));
    }

    private List<ComicPanel> RefineRowsFromUncoveredBands(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        if (panels.Count < 4)
        {
            return panels;
        }

        var rows = GroupPanelsIntoRowsForWideSplit(panels)
            .OrderBy(row => row.MinY)
            .ToList();
        if (rows.Count < 2)
        {
            return panels;
        }

        var adjustedPanels = new List<ComicPanel>(panels);
        foreach (var band in FindUncoveredRowBands(rows))
        {
            int rowStart = Math.Max(0, (int)Math.Round(band.StartY * imgH));
            int rowEnd = Math.Min(imgH, (int)Math.Round(band.EndY * imgH));
            int xStart = Math.Max(0, (int)Math.Round(band.X * imgW));
            int xEnd = Math.Min(imgW, (int)Math.Round((band.X + band.Width) * imgW));
            var rect = new Rect(xStart, rowStart, Math.Max(1, xEnd - xStart), Math.Max(1, rowEnd - rowStart));
            if (!IsSensibleCandidate(rect, imgW, imgH, pageArea) ||
                adjustedPanels.Any(existing => GetIntersectionRatio(existing, rect, imgW, imgH) > 0.40))
            {
                continue;
            }

            if (!HasStrongRowBandSupport(band, grayFull, edges, imgW, imgH))
            {
                continue;
            }

            var stats = MeasureCandidateStats(rect, grayFull, edges, imgW, imgH, pageArea);
            double confidence =
                (stats.GutterContrast * 0.22) +
                (stats.BorderEdgeDensity * 0.20) +
                (stats.InteriorVarianceScore * 0.15) +
                (stats.AreaScore * 0.10) +
                (stats.EdgeTouchScore * 0.07) +
                0.20;
            if (confidence < 0.47)
            {
                continue;
            }

            adjustedPanels.Add(CreatePanel(rows[0].Panels[0].PageIndex, rect, imgW, imgH, confidence));
        }

        return RemoveNestedInsetPanels(FilterOverlappingPanels(adjustedPanels));
    }

    private List<ComicPanel> RefineWidePanelsToInternalGutters(List<ComicPanel> panels, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        var adjustedPanels = new List<ComicPanel>();
        foreach (var row in GroupPanelsIntoRowsForWideSplit(panels).OrderBy(row => row.MinY))
        {
            var rowPanels = row.Panels
                .OrderBy(panel => panel.X)
                .Select(panel => CreateAdjustedPanel(panel, panel.X, panel.Y, panel.Width, panel.Height))
                .ToList();

            if (rowPanels.Count == 2)
            {
                var narrowPanel = rowPanels.OrderBy(panel => panel.Width).First();
                var widePanel = rowPanels.OrderByDescending(panel => panel.Width).First();
                if (widePanel.Width >= 0.50 && narrowPanel.Width >= 0.20 && widePanel.Width >= narrowPanel.Width * 1.65)
                {
                    int rowStart = Math.Max(0, (int)Math.Round(rowPanels.Min(panel => panel.Y) * imgH));
                    int rowEnd = Math.Min(imgH, (int)Math.Round(rowPanels.Max(panel => panel.Y + panel.Height) * imgH));
                    var splitRun = FindInternalVerticalSplitRun(widePanel, rowStart, rowEnd, grayFull, edges, imgW, imgH, pageArea);
                    if (splitRun is not null)
                    {
                        var (gutterStart, gutterEnd) = splitRun.Value;
                        double leftWidth = gutterStart - widePanel.X;
                        double rightX = gutterEnd;
                        double rightWidth = (widePanel.X + widePanel.Width) - rightX;
                        if (leftWidth > MinPanelSizeRatio && rightWidth > MinPanelSizeRatio)
                        {
                            rowPanels.Remove(widePanel);
                            rowPanels.Add(CreateAdjustedPanel(widePanel, widePanel.X, widePanel.Y, leftWidth, widePanel.Height));
                            rowPanels.Add(CreateAdjustedPanel(widePanel, rightX, widePanel.Y, rightWidth, widePanel.Height));
                            rowPanels = rowPanels.OrderBy(panel => panel.X).ToList();
                        }
                    }
                }
            }

            adjustedPanels.AddRange(rowPanels);
        }

        return RemoveNestedInsetPanels(FilterOverlappingPanels(adjustedPanels));
    }

    private static List<RowBand> BuildRows(List<ComicPanel> panels)
    {
        var rows = new List<RowBand>();
        foreach (var panel in panels.OrderBy(panel => panel.Y))
        {
            var panelTop = panel.Y;
            var panelBottom = panel.Y + panel.Height;
            var existingRow = rows.FirstOrDefault(row => panelTop < row.MaxY && panelBottom > row.MinY);
            if (existingRow is null)
            {
                rows.Add(new RowBand(panelTop, panelBottom, panel.Width));
            }
            else
            {
                existingRow.MinY = Math.Min(existingRow.MinY, panelTop);
                existingRow.MaxY = Math.Max(existingRow.MaxY, panelBottom);
                existingRow.Coverage += panel.Width;
                existingRow.PanelCount++;
            }
        }

        return rows;
    }

    private static List<PanelRow> GroupPanelsIntoRows(List<ComicPanel> panels)
    {
        var rows = new List<PanelRow>();
        foreach (var panel in panels.OrderBy(panel => panel.Y))
        {
            var panelTop = panel.Y;
            var panelBottom = panel.Y + panel.Height;
            var row = rows.FirstOrDefault(existing => panelTop < existing.MaxY && panelBottom > existing.MinY);
            if (row is null)
            {
                rows.Add(new PanelRow(panel));
            }
            else
            {
                row.MinY = Math.Min(row.MinY, panelTop);
                row.MaxY = Math.Max(row.MaxY, panelBottom);
                row.Coverage += panel.Width;
                row.Panels.Add(panel);
            }
        }

        return rows;
    }

    private static List<PanelRow> GroupPanelsIntoRowsForWideSplit(List<ComicPanel> panels)
    {
        var rows = new List<PanelRow>();
        foreach (var panel in panels.OrderBy(panel => panel.Y))
        {
            var panelTop = panel.Y;
            var panelBottom = panel.Y + panel.Height;
            var row = rows.FirstOrDefault(existing =>
            {
                double overlap = Math.Min(panelBottom, existing.MaxY) - Math.Max(panelTop, existing.MinY);
                double minRequiredOverlap = Math.Max(0.01, Math.Min(panel.Height, existing.MaxY - existing.MinY) * 0.15);
                return overlap >= minRequiredOverlap;
            });

            if (row is null)
            {
                rows.Add(new PanelRow(panel));
            }
            else
            {
                row.MinY = Math.Min(row.MinY, panelTop);
                row.MaxY = Math.Max(row.MaxY, panelBottom);
                row.Coverage += panel.Width;
                row.Panels.Add(panel);
            }
        }

        return rows;
    }

    private static List<ComicPanel> RemoveNestedInsetPanels(List<ComicPanel> panels)
    {
        if (panels.Count <= 1)
        {
            return panels;
        }

        var result = new List<ComicPanel>();
        foreach (var panel in panels.OrderByDescending(candidate => candidate.Width * candidate.Height))
        {
            bool isNestedInset = panels.Any(other =>
                !ReferenceEquals(other, panel) &&
                (other.Width * other.Height) > (panel.Width * panel.Height) * 2.0 &&
                GetIntersectionRatio(panel, other) > 0.92 &&
                !TouchesPageEdge(panel) &&
                panel.Confidence <= other.Confidence + 0.10);

            if (!isNestedInset)
            {
                result.Add(panel);
            }
        }

        return result;
    }

    private static double GetIntersectionRatio(ComicPanel panel, Rect rect, int imgW, int imgH)
    {
        double panelX = panel.X * imgW;
        double panelY = panel.Y * imgH;
        double panelW = panel.Width * imgW;
        double panelH = panel.Height * imgH;

        double x1 = Math.Max(panelX, rect.X);
        double y1 = Math.Max(panelY, rect.Y);
        double x2 = Math.Min(panelX + panelW, rect.X + rect.Width);
        double y2 = Math.Min(panelY + panelH, rect.Y + rect.Height);

        if (x2 <= x1 || y2 <= y1)
        {
            return 0;
        }

        double intersectionArea = (x2 - x1) * (y2 - y1);
        return intersectionArea / (rect.Width * (double)rect.Height);
    }

    private static double GetIntersectionRatio(ComicPanel panel, ComicPanel other)
    {
        double x1 = Math.Max(panel.X, other.X);
        double y1 = Math.Max(panel.Y, other.Y);
        double x2 = Math.Min(panel.X + panel.Width, other.X + other.Width);
        double y2 = Math.Min(panel.Y + panel.Height, other.Y + other.Height);

        if (x2 <= x1 || y2 <= y1)
        {
            return 0;
        }

        double intersectionArea = (x2 - x1) * (y2 - y1);
        return intersectionArea / (panel.Width * panel.Height);
    }

    private static double GetHorizontalOverlapRatio(ComicPanel panel, ComicPanel other)
    {
        double overlap = Math.Min(panel.X + panel.Width, other.X + other.Width) - Math.Max(panel.X, other.X);
        if (overlap <= 0)
        {
            return 0;
        }

        return overlap / Math.Max(0.0001, Math.Min(panel.Width, other.Width));
    }

    private static List<(double Start, double End)> FindRowGapCandidates(List<ComicPanel> rowPanels)
    {
        var gaps = new List<(double Start, double End)>();
        double leftGap = rowPanels[0].X;
        if (leftGap >= 0.16)
        {
            gaps.Add((0, rowPanels[0].X));
        }

        for (int i = 0; i < rowPanels.Count - 1; i++)
        {
            double start = rowPanels[i].X + rowPanels[i].Width;
            double end = rowPanels[i + 1].X;
            if (end - start >= 0.18)
            {
                gaps.Add((start, end));
            }
        }

        double rightStart = rowPanels[^1].X + rowPanels[^1].Width;
        if (1.0 - rightStart >= 0.16)
        {
            gaps.Add((rightStart, 1.0));
        }

        return gaps
            .OrderByDescending(gap => gap.End - gap.Start)
            .ToList();
    }

    private static bool HasStrongCandidateBorderSupport(Rect rect, int rowStart, int rowEnd, Mat grayFull, Mat edges, int imgW, int imgH)
    {
        int xPadding = Math.Max(6, rect.Width / 16);
        int yPadding = Math.Max(6, rect.Height / 16);
        int xStart = Math.Max(0, rect.X + xPadding / 2);
        int xEnd = Math.Min(imgW, rect.Right - xPadding / 2);
        if (xEnd <= xStart)
        {
            return false;
        }

        double topSupport = GetMaxHorizontalBorderScore(
            Math.Max(0, rowStart - yPadding),
            Math.Min(imgH, rowStart + yPadding + 1),
            xStart,
            xEnd,
            grayFull,
            edges);
        double bottomSupport = GetMaxHorizontalBorderScore(
            Math.Max(0, rowEnd - yPadding),
            Math.Min(imgH, rowEnd + yPadding + 1),
            xStart,
            xEnd,
            grayFull,
            edges);

        bool touchesLeftEdge = rect.X <= Math.Max(3, imgW / 300);
        bool touchesRightEdge = rect.Right >= imgW - Math.Max(3, imgW / 300);
        double leftSupport = touchesLeftEdge
            ? 0.35
            : GetMaxVerticalBorderScore(Math.Max(0, rect.X - xPadding), Math.Min(imgW, rect.X + xPadding + 1), rowStart, rowEnd, grayFull, edges);
        double rightSupport = touchesRightEdge
            ? 0.35
            : GetMaxVerticalBorderScore(Math.Max(0, rect.Right - xPadding), Math.Min(imgW, rect.Right + xPadding + 1), rowStart, rowEnd, grayFull, edges);

        return topSupport >= 0.24 &&
               bottomSupport >= 0.24 &&
               leftSupport >= 0.22 &&
               rightSupport >= 0.22;
    }

    private static bool TouchesPageEdge(ComicPanel panel)
    {
        return panel.X <= 0.01 || panel.Y <= 0.01 || panel.X + panel.Width >= 0.99 || panel.Y + panel.Height >= 0.99;
    }

    private static List<RowBandCandidate> FindUncoveredRowBands(List<PanelRow> rows)
    {
        var bands = new List<RowBandCandidate>();

        var firstRow = rows[0];
        double firstMinX = firstRow.Panels.Min(panel => panel.X);
        double firstMaxRight = firstRow.Panels.Max(panel => panel.X + panel.Width);
        if (firstRow.MinY >= 0.16 &&
            firstRow.MinY <= 0.38 &&
            firstRow.Coverage >= 0.85 &&
            firstMinX <= 0.06 &&
            firstMaxRight >= 0.94)
        {
            bands.Add(new RowBandCandidate(0, firstRow.MinY, firstMinX, firstMaxRight - firstMinX, touchesTopEdge: true, touchesBottomEdge: false));
        }

        for (int i = 0; i < rows.Count - 1; i++)
        {
            var upper = rows[i];
            var lower = rows[i + 1];
            double gapStart = upper.MaxY;
            double gapEnd = lower.MinY;
            double gapHeight = gapEnd - gapStart;
            if (gapHeight < 0.12 || gapHeight > 0.34)
            {
                continue;
            }

            double upperMinX = upper.Panels.Min(panel => panel.X);
            double upperMaxRight = upper.Panels.Max(panel => panel.X + panel.Width);
            double lowerMinX = lower.Panels.Min(panel => panel.X);
            double lowerMaxRight = lower.Panels.Max(panel => panel.X + panel.Width);
            double overlapLeft = Math.Max(upperMinX, lowerMinX);
            double overlapRight = Math.Min(upperMaxRight, lowerMaxRight);
            double overlapWidth = overlapRight - overlapLeft;
            if (upper.Coverage < 0.85 || lower.Coverage < 0.85 || overlapWidth < 0.85)
            {
                continue;
            }

            bands.Add(new RowBandCandidate(gapStart, gapEnd, overlapLeft, overlapWidth, touchesTopEdge: false, touchesBottomEdge: false));
        }

        return bands;
    }

    private static bool HasStrongRowBandSupport(RowBandCandidate band, Mat grayFull, Mat edges, int imgW, int imgH)
    {
        int rowStart = Math.Max(0, (int)Math.Round(band.StartY * imgH));
        int rowEnd = Math.Min(imgH, (int)Math.Round(band.EndY * imgH));
        int xStart = Math.Max(0, (int)Math.Round(band.X * imgW));
        int xEnd = Math.Min(imgW, (int)Math.Round((band.X + band.Width) * imgW));
        if (rowEnd <= rowStart || xEnd <= xStart)
        {
            return false;
        }

        int yPadding = Math.Max(8, (rowEnd - rowStart) / 10);
        int xPadding = Math.Max(8, (xEnd - xStart) / 14);
        double topSupport = band.TouchesTopEdge
            ? 0.35
            : GetMaxHorizontalBorderScore(Math.Max(0, rowStart - yPadding), Math.Min(imgH, rowStart + yPadding + 1), xStart, xEnd, grayFull, edges);
        double bottomSupport = band.TouchesBottomEdge
            ? 0.35
            : GetMaxHorizontalBorderScore(Math.Max(0, rowEnd - yPadding), Math.Min(imgH, rowEnd + yPadding + 1), xStart, xEnd, grayFull, edges);
        double leftSupport = band.X <= 0.06
            ? 0.30
            : GetMaxVerticalBorderScore(Math.Max(0, xStart - xPadding), Math.Min(imgW, xStart + xPadding + 1), rowStart, rowEnd, grayFull, edges);
        double rightEdge = band.X + band.Width;
        double rightSupport = rightEdge >= 0.94
            ? 0.30
            : GetMaxVerticalBorderScore(Math.Max(0, xEnd - xPadding), Math.Min(imgW, xEnd + xPadding + 1), rowStart, rowEnd, grayFull, edges);

        return topSupport >= 0.24 &&
               bottomSupport >= 0.24 &&
               leftSupport >= 0.20 &&
               rightSupport >= 0.20;
    }

    private (double Start, double End)? FindLocalGutterRun(ComicPanel leftPanel, ComicPanel rightPanel, int rowStart, int rowEnd, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        int currentRight = (int)Math.Round((leftPanel.X + leftPanel.Width) * imgW);
        int currentLeft = (int)Math.Round(rightPanel.X * imgW);
        int searchPadding = Math.Max(8, imgW / 40);
        int searchStart = Math.Max(0, currentRight - searchPadding);
        int searchEnd = Math.Min(imgW - 1, currentLeft + searchPadding);
        if (searchEnd <= searchStart)
        {
            return null;
        }

        int? bestStart = null;
        int? bestEnd = null;
        double bestScore = double.MinValue;
        int? runStart = null;
        double runScore = 0;
        int runCount = 0;

        for (int x = searchStart; x <= searchEnd; x++)
        {
            double score = ScoreVerticalGutterColumn(grayFull, edges, x, rowStart, rowEnd);
            bool qualifies = score >= 0.38;
            if (qualifies)
            {
                runStart ??= x;
                runScore += score;
                runCount++;
            }
            else if (runStart.HasValue)
            {
                int runEnd = x;
                double averageScore = runScore / Math.Max(1, runCount);
                double widthScore = Math.Min(1.0, (runEnd - runStart.Value) / (double)Math.Max(4, imgW / 150));
                double center = (runStart.Value + runEnd) / 2.0;
                double currentCenter = (currentRight + currentLeft) / 2.0;
                double distancePenalty = Math.Abs(center - currentCenter) / Math.Max(20.0, searchEnd - searchStart);
                double candidateScore = averageScore + widthScore - distancePenalty;

                if (candidateScore > bestScore)
                {
                    bestScore = candidateScore;
                    bestStart = runStart.Value;
                    bestEnd = runEnd;
                }

                runStart = null;
                runScore = 0;
                runCount = 0;
            }
        }

        if (runStart.HasValue)
        {
            int runEnd = searchEnd + 1;
            double averageScore = runScore / Math.Max(1, runCount);
            double widthScore = Math.Min(1.0, (runEnd - runStart.Value) / (double)Math.Max(4, imgW / 150));
            double center = (runStart.Value + runEnd) / 2.0;
            double currentCenter = (currentRight + currentLeft) / 2.0;
            double distancePenalty = Math.Abs(center - currentCenter) / Math.Max(20.0, searchEnd - searchStart);
            double candidateScore = averageScore + widthScore - distancePenalty;

            if (candidateScore > bestScore)
            {
                bestScore = candidateScore;
                bestStart = runStart.Value;
                bestEnd = runEnd;
            }
        }

        if (!bestStart.HasValue || !bestEnd.HasValue)
        {
            return null;
        }

        double start = bestStart.Value / (double)imgW;
        double end = bestEnd.Value / (double)imgW;
        var leftRect = new Rect(
            (int)Math.Round(leftPanel.X * imgW),
            rowStart,
            Math.Max(1, (int)Math.Round((start - leftPanel.X) * imgW)),
            Math.Max(1, rowEnd - rowStart));
        var rightRect = new Rect(
            (int)Math.Round(start * imgW),
            rowStart,
            Math.Max(1, (int)Math.Round(((rightPanel.X + rightPanel.Width) - end) * imgW)),
            Math.Max(1, rowEnd - rowStart));

        if (!IsSensibleCandidate(leftRect, imgW, imgH, pageArea) || !IsSensibleCandidate(rightRect, imgW, imgH, pageArea))
        {
            return null;
        }

        return (start, end);
    }

    private bool HasValidatedVerticalSeparator(ComicPanel leftPanel, ComicPanel rightPanel, int rowStart, int rowEnd, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        var gutterRun = FindLocalGutterRun(leftPanel, rightPanel, rowStart, rowEnd, grayFull, edges, imgW, imgH, pageArea);
        if (gutterRun is not null && HasBorderSupportAroundGutter(gutterRun.Value, rowStart, rowEnd, grayFull, edges, imgW))
        {
            return true;
        }

        int currentRight = (int)Math.Round((leftPanel.X + leftPanel.Width) * imgW);
        int currentLeft = (int)Math.Round(rightPanel.X * imgW);
        int expectedCenter = (int)Math.Round((currentRight + currentLeft) / 2.0);
        int searchPadding = Math.Max(10, imgW / 70);
        int searchStart = Math.Max(0, expectedCenter - searchPadding);
        int searchEnd = Math.Min(imgW, expectedCenter + searchPadding + 1);
        int? borderX = FindBestVerticalBorder(searchStart, searchEnd, rowStart, rowEnd, grayFull, edges);
        if (!borderX.HasValue)
        {
            return false;
        }

        double distance = Math.Abs(borderX.Value - expectedCenter);
        if (distance > Math.Max(10, imgW / 75))
        {
            return false;
        }

        return ScoreVerticalBorderColumn(grayFull, edges, borderX.Value, rowStart, rowEnd) >= 0.30;
    }

    private static bool HasBorderSupportAroundGutter((double Start, double End) gutterRun, int rowStart, int rowEnd, Mat grayFull, Mat edges, int imgW)
    {
        int gutterStart = (int)Math.Round(gutterRun.Start * imgW);
        int gutterEnd = (int)Math.Round(gutterRun.End * imgW);
        int searchRadius = Math.Max(8, imgW / 120);

        double leftSupport = GetMaxVerticalBorderScore(
            Math.Max(0, gutterStart - searchRadius),
            Math.Min(imgW, gutterStart + searchRadius + 1),
            rowStart,
            rowEnd,
            grayFull,
            edges);
        double rightSupport = GetMaxVerticalBorderScore(
            Math.Max(0, gutterEnd - searchRadius),
            Math.Min(imgW, gutterEnd + searchRadius + 1),
            rowStart,
            rowEnd,
            grayFull,
            edges);

        return (leftSupport >= 0.24 && rightSupport >= 0.24) || Math.Max(leftSupport, rightSupport) >= 0.42;
    }

    private static double GetMaxVerticalBorderScore(int searchStart, int searchEnd, int rowStart, int rowEnd, Mat grayFull, Mat edges)
    {
        double bestScore = double.MinValue;
        for (int x = searchStart; x < searchEnd; x++)
        {
            bestScore = Math.Max(bestScore, ScoreVerticalBorderColumn(grayFull, edges, x, rowStart, rowEnd));
        }

        return bestScore;
    }

    private static double GetMaxHorizontalBorderScore(int searchStart, int searchEnd, int xStart, int xEnd, Mat grayFull, Mat edges)
    {
        double bestScore = double.MinValue;
        for (int y = searchStart; y < searchEnd; y++)
        {
            bestScore = Math.Max(bestScore, ScoreHorizontalBorderRow(grayFull, edges, y, xStart, xEnd));
        }

        return bestScore;
    }

    private (double Start, double End)? FindInternalVerticalSplitRun(ComicPanel panel, int rowStart, int rowEnd, Mat grayFull, Mat edges, int imgW, int imgH, double pageArea)
    {
        int panelLeft = (int)Math.Round(panel.X * imgW);
        int panelRight = (int)Math.Round((panel.X + panel.Width) * imgW);
        int panelWidth = Math.Max(1, panelRight - panelLeft);
        int margin = Math.Max(12, panelWidth / 6);
        int searchStart = panelLeft + margin;
        int searchEnd = panelRight - margin;
        if (searchEnd <= searchStart)
        {
            return null;
        }

        int? bestStart = null;
        int? bestEnd = null;
        double bestScore = double.MinValue;
        int? runStart = null;
        double runScore = 0;
        int runCount = 0;

        for (int x = searchStart; x <= searchEnd; x++)
        {
            double score = ScoreVerticalGutterColumn(grayFull, edges, x, rowStart, rowEnd);
            bool qualifies = score >= 0.50;
            if (qualifies)
            {
                runStart ??= x;
                runScore += score;
                runCount++;
            }
            else if (runStart.HasValue)
            {
                EvaluateVerticalSplitRun(panel, imgW, runStart.Value, x, runScore, runCount, ref bestStart, ref bestEnd, ref bestScore);
                runStart = null;
                runScore = 0;
                runCount = 0;
            }
        }

        if (runStart.HasValue)
        {
            EvaluateVerticalSplitRun(panel, imgW, runStart.Value, searchEnd + 1, runScore, runCount, ref bestStart, ref bestEnd, ref bestScore);
        }

        if (!bestStart.HasValue || !bestEnd.HasValue)
        {
            return null;
        }

        var splitRun = (bestStart.Value / (double)imgW, bestEnd.Value / (double)imgW);
        return HasBorderSupportAroundGutter(splitRun, rowStart, rowEnd, grayFull, edges, imgW)
            ? splitRun
            : null;
    }

    private static void EvaluateVerticalSplitRun(
        ComicPanel panel,
        int imgW,
        int runStart,
        int runEnd,
        double runScore,
        int runCount,
        ref int? bestStart,
        ref int? bestEnd,
        ref double bestScore)
    {
        double start = runStart / (double)imgW;
        double end = runEnd / (double)imgW;
        double averageScore = runScore / Math.Max(1, runCount);
        double widthScore = Math.Min(1.0, (runEnd - runStart) / (double)Math.Max(4, imgW / 150));
        double leftWidth = start - panel.X;
        double rightWidth = (panel.X + panel.Width) - end;
        if (leftWidth <= MinPanelSizeRatio || rightWidth <= MinPanelSizeRatio)
        {
            return;
        }

        double balanceScore = 1.0 - Math.Min(1.0, Math.Abs(leftWidth - rightWidth) / Math.Max(0.01, panel.Width));
        double candidateScore = averageScore + widthScore + (balanceScore * 0.35);

        if (candidateScore > bestScore)
        {
            bestScore = candidateScore;
            bestStart = runStart;
            bestEnd = runEnd;
        }
    }

    private static ComicPanel CreateAdjustedPanel(ComicPanel source, double x, double y, double width, double height)
    {
        return new ComicPanel
        {
            PageIndex = source.PageIndex,
            PanelIndex = source.PanelIndex,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Confidence = source.Confidence
        };
    }

    private static double ScoreVerticalGutterColumn(Mat grayFull, Mat edges, int x, int rowStart, int rowEnd)
    {
        int brightPixels = 0;
        int edgePixels = 0;
        int height = Math.Max(1, rowEnd - rowStart);

        for (int y = rowStart; y < rowEnd; y++)
        {
            if (grayFull.At<byte>(y, x) >= 220)
            {
                brightPixels++;
            }

            if (edges.At<byte>(y, x) > 0)
            {
                edgePixels++;
            }
        }

        double brightRatio = brightPixels / (double)height;
        double edgeRatio = edgePixels / (double)height;
        return brightRatio - (edgeRatio * 1.25);
    }

    private static bool ShouldMergeAdjacentBorderPanels(ComicPanel leftPanel, ComicPanel rightPanel, int rowPanelCount)
    {
        if (rowPanelCount <= 1)
        {
            return false;
        }

        double smallerWidth = Math.Min(leftPanel.Width, rightPanel.Width);
        double largerWidth = Math.Max(leftPanel.Width, rightPanel.Width);
        return smallerWidth <= 0.22 || smallerWidth <= largerWidth * 0.55;
    }

    private static int? FindBestVerticalBorder(int searchStart, int searchEnd, int rowStart, int rowEnd, Mat grayFull, Mat edges)
    {
        if (searchEnd <= searchStart || rowEnd <= rowStart)
        {
            return null;
        }

        int bestX = -1;
        double bestScore = 0.22;
        for (int x = searchStart; x < searchEnd; x++)
        {
            double score = ScoreVerticalBorderColumn(grayFull, edges, x, rowStart, rowEnd);
            if (score > bestScore)
            {
                bestScore = score;
                bestX = x;
            }
        }

        return bestX >= 0 ? bestX : null;
    }

    private static double ScoreVerticalBorderColumn(Mat grayFull, Mat edges, int x, int rowStart, int rowEnd)
    {
        int brightPixels = 0;
        int darkPixels = 0;
        int edgePixels = 0;
        int height = Math.Max(1, rowEnd - rowStart);

        for (int y = rowStart; y < rowEnd; y++)
        {
            byte gray = grayFull.At<byte>(y, x);
            if (gray >= 215)
            {
                brightPixels++;
            }

            if (gray <= 60)
            {
                darkPixels++;
            }

            if (edges.At<byte>(y, x) > 0)
            {
                edgePixels++;
            }
        }

        double brightRatio = brightPixels / (double)height;
        double darkRatio = darkPixels / (double)height;
        double edgeRatio = edgePixels / (double)height;
        return (darkRatio * 0.55) + (edgeRatio * 0.35) - (brightRatio * 0.15);
    }

    private static int? FindBestHorizontalBorder(int searchStart, int searchEnd, int xStart, int xEnd, Mat grayFull, Mat edges)
    {
        if (searchEnd <= searchStart || xEnd <= xStart)
        {
            return null;
        }

        int bestY = -1;
        double bestScore = 0.22;
        for (int y = searchStart; y < searchEnd; y++)
        {
            double score = ScoreHorizontalBorderRow(grayFull, edges, y, xStart, xEnd);
            if (score > bestScore)
            {
                bestScore = score;
                bestY = y;
            }
        }

        return bestY >= 0 ? bestY : null;
    }

    private static double ScoreHorizontalBorderRow(Mat grayFull, Mat edges, int y, int xStart, int xEnd)
    {
        int brightPixels = 0;
        int darkPixels = 0;
        int edgePixels = 0;
        int width = Math.Max(1, xEnd - xStart);

        for (int x = xStart; x < xEnd; x++)
        {
            byte gray = grayFull.At<byte>(y, x);
            if (gray >= 215)
            {
                brightPixels++;
            }

            if (gray <= 60)
            {
                darkPixels++;
            }

            if (edges.At<byte>(y, x) > 0)
            {
                edgePixels++;
            }
        }

        double brightRatio = brightPixels / (double)width;
        double darkRatio = darkPixels / (double)width;
        double edgeRatio = edgePixels / (double)width;
        return (darkRatio * 0.55) + (edgeRatio * 0.35) - (brightRatio * 0.15);
    }

    private static EvidenceRowSupport GetEvidenceRowSupport(int rowStart, int rowEnd, Mat grayFull, Mat edges, int imgW, int imgH)
    {
        double topSupport = GetMaxHorizontalBorderScore(
            Math.Max(0, rowStart - Math.Max(4, imgH / 240)),
            Math.Min(imgH, rowStart + Math.Max(5, imgH / 220)),
            0,
            imgW,
            grayFull,
            edges);
        double bottomSupport = GetMaxHorizontalBorderScore(
            Math.Max(0, rowEnd - Math.Max(5, imgH / 220)),
            Math.Min(imgH, rowEnd + Math.Max(4, imgH / 240)),
            0,
            imgW,
            grayFull,
            edges);

        bool touchesPageEdge = rowStart <= Math.Max(4, imgH / 240) || rowEnd >= imgH - Math.Max(4, imgH / 240);
        bool hasStrongSupport = touchesPageEdge || Math.Max(topSupport, bottomSupport) >= 0.24;
        double separatorSupport = Math.Clamp((topSupport + bottomSupport) / 2.0, 0.0, 1.0);
        return new EvidenceRowSupport(hasStrongSupport, separatorSupport);
    }

    private sealed class CandidateStats
    {
        public double AreaRatio { get; init; }
        public double AreaScore { get; init; }
        public double BorderEdgeDensity { get; init; }
        public double EdgeTouchScore { get; init; }
        public double GutterContrast { get; init; }
        public double InteriorStdDev { get; init; }
        public double InteriorVarianceScore { get; init; }
    }

    private sealed class RowBand(double minY, double maxY, double coverage)
    {
        public double MinY { get; set; } = minY;
        public double MaxY { get; set; } = maxY;
        public double Coverage { get; set; } = coverage;
        public int PanelCount { get; set; } = 1;
    }

    private sealed class PanelRow(ComicPanel panel)
    {
        public double MinY { get; set; } = panel.Y;
        public double MaxY { get; set; } = panel.Y + panel.Height;
        public double Coverage { get; set; } = panel.Width;
        public List<ComicPanel> Panels { get; } = [panel];
    }

    private sealed class LayoutRowCandidate(double y, double height, List<ComicPanel> panels)
    {
        public double Y { get; set; } = y;
        public double Height { get; set; } = height;
        public List<ComicPanel> Panels { get; } = panels;
    }

    private readonly record struct EvidenceRowSupport(bool HasStrongSupport, double SeparatorSupport);

    private sealed class RowBandCandidate(double startY, double endY, double x, double width, bool touchesTopEdge, bool touchesBottomEdge)
    {
        public double StartY { get; } = startY;
        public double EndY { get; } = endY;
        public double X { get; } = x;
        public double Width { get; } = width;
        public bool TouchesTopEdge { get; } = touchesTopEdge;
        public bool TouchesBottomEdge { get; } = touchesBottomEdge;
    }

    private static List<LayoutRowCandidate> NormalizeLayoutRows(List<LayoutRowCandidate> rows)
    {
        if (rows.Count <= 1)
        {
            return rows;
        }

        var orderedRows = rows.OrderBy(row => row.Y).ToList();
        var boundaries = new double[orderedRows.Count + 1];
        boundaries[0] = orderedRows[0].Y;
        boundaries[^1] = orderedRows[^1].Y + orderedRows[^1].Height;

        for (int i = 0; i < orderedRows.Count - 1; i++)
        {
            double currentBottom = orderedRows[i].Y + orderedRows[i].Height;
            double nextTop = orderedRows[i + 1].Y;
            boundaries[i + 1] = (currentBottom + nextTop) / 2.0;
        }

        for (int i = 0; i < orderedRows.Count; i++)
        {
            double newTop = boundaries[i];
            double newBottom = boundaries[i + 1];
            double newHeight = Math.Max(0.01, newBottom - newTop);

            orderedRows[i].Y = newTop;
            orderedRows[i].Height = newHeight;

            foreach (var panel in orderedRows[i].Panels)
            {
                panel.Y = newTop;
                panel.Height = newHeight;
            }
        }

        return orderedRows;
    }
}
