// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Runtime.InteropServices;
using OpenCvSharp;
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

/// <summary>
/// Detects panels by splitting the page along its gutters (recursive XY-cut):
/// 1. The page is scaled to a fixed working height, so all sizes mean the same for every scan resolution.
/// 2. The gutter colour is taken from the page border, everything clearly different from it is content.
/// 3. Background which is not connected to the page border (e.g. a white sky inside a bordered panel) is content too.
/// 4. The page is cut along empty rows, then along empty columns, recursively. The order of the cuts is the reading order.
/// 5. Only when no clean cut exists, a cut through a gutter which is partly covered (a balloon crossing it) is tried.
/// 6. Columns whose rows are only slightly staggered are read row by row, like a reader does.
/// 7. A page which only splits into bands from edge to edge, without any frame along its gutters, is text or a cover.
/// 8. Optional: in a big region on the page edge which can't be cut, closed panel frames are
///    searched, panels drawn over a background panel. The page gets a margin in the page colour first, so art
///    bleeding off the page has an edge too.
/// </summary>
public static class GutterPanelDetector
{
    private const int WorkHeight = 1600;
    private const int ForegroundThreshold = 20;
    private const int MinGutterSize = 2;
    private const int MaxDepth = 12;
    private const double StrictGutterTolerance = 0.002;
    private const double SoftGutterTolerance = 0.3;
    private const double SoftCutMinPartRatio = 0.06;
    private const double MinPanelAreaRatio = 0.015;
    private const double MinPanelWidthRatio = 0.06;
    private const double MinPanelHeightRatio = 0.04;
    private const double StaggeredRowTolerance = 0.06;
    private const double PageEdgeRatio = 0.005;
    private const int StrongDifference = 60;
    private const int FrameSearchLines = 4;
    private const double NoFrameEvidence = 0.1;
    private const double PaddingRatio = 0.02;
    private const double InsetRegionRatio = 0.2;
    private const double MinInsetRegionRatio = 0.05;
    private const double BackgroundCoveredRatio = 0.7;
    private const int InsetClearance = 3;
    private const int FrameLineTolerance = 3;
    private const double FrameSide = 0.75;
    private const double MeanFrameCoverage = 0.85;
    private const double CoveredFrameSide = 0.5;
    private const double CompleteFrameSide = 0.97;
    private const int CannyLowThreshold = 40;
    private const int CannyHighThreshold = 120;
    private const double HorizontalFrameLineRatio = 0.04;
    private const double VerticalFrameLineRatio = 0.03;
    private const double FrameSegmentRatio = 0.04;
    private const int FrameLineThickness = 31;
    private const int FlatLineEnds = 4;
    private const double EdgeBarUniformity = 0.98;
    private const double EdgeBarPageShare = 0.6;
    private const int EdgeBarBlur = 3;
    private const double FramedOutline = 0.85;
    private const double UnframedOutline = 0.1;
    private const double SiblingSizeTolerance = 0.25;
    private static readonly bool DiagnoseFrames = true;
    private const double ClearFrameMargin = 0.02;
    private const double AmbiguousFrameRange = 0.05;

    /// <summary>
    /// Detects the panels in reading order, normalized to 0-1. Returns an empty list when the page has no panel
    /// structure (a splash page or a cover).
    /// </summary>
    /// <param name="log">Optional, receives the decisions for tuning the detection</param>
    /// <param name="findInsets">Also search panels drawn over a background panel</param>
    public static List<ComicPanel> Detect(int pageIndex, byte[] pageData, bool isManga, Action<string>? log = null, bool findInsets = false)
    {
        var mask = CreateContentMask(pageData, findInsets);
        if (mask is null)
        {
            return [];
        }

        mask.Log = log;
        var page = new Region(0, 0, mask.Width, mask.Height);
        var leaves = new List<Region>();
        Split(mask, page, 0, isManga, false, leaves);
        if (findInsets)
        {
            leaves = SplitInsets(mask, leaves, isManga);
        }

        double pageArea = (double)mask.PageWidth * mask.PageHeight;
        var panels = leaves
            .Select(mask.ToPage)
            .Where(region => region.Area >= pageArea * MinPanelAreaRatio &&
                             region.Width >= mask.PageWidth * MinPanelWidthRatio &&
                             region.Height >= mask.PageHeight * MinPanelHeightRatio)
            .Select((region, index) => new ComicPanel
            {
                PageIndex = pageIndex,
                PanelIndex = index,
                X = (double)region.X0 / mask.PageWidth,
                Y = (double)region.Y0 / mask.PageHeight,
                Width = (double)region.Width / mask.PageWidth,
                Height = (double)region.Height / mask.PageHeight,
                Confidence = 1.0
            })
            .ToList();

        // Bands from edge to edge without any frame along the gutters are text or a cover, not panels
        int bands = panels.Count(panel => panel.X <= PageEdgeRatio && panel.X + panel.Width >= 1 - PageEdgeRatio);
        bool hasFrames = mask.FrameEvidence.Any(evidence => evidence >= NoFrameEvidence);
        bool isBandPage = (bands == panels.Count || (bands >= 2 && bands >= panels.Count - 1)) && !hasFrames;
        log?.Invoke($"{panels.Count} panels, {bands} edge to edge bands{(isBandPage ? ", band page: splash" : string.Empty)}");
        return panels.Count > 1 && !isBandPage ? panels : [];
    }

    private static ContentMask? CreateContentMask(byte[] pageData, bool findInsets)
    {
        using var decoded = Cv2.ImDecode(pageData, ImreadModes.Grayscale);
        if (decoded.Empty())
        {
            return null;
        }

        using var gray = new Mat();
        // The frame search measures lines in pixels, it needs the same scale for every page
        if (decoded.Rows > WorkHeight || (findInsets && decoded.Rows != WorkHeight))
        {
            int width = Math.Max(1, (int)Math.Round(decoded.Cols * (double)WorkHeight / decoded.Rows));
            Cv2.Resize(decoded, gray, new Size(width, WorkHeight),
                interpolation: decoded.Rows > WorkHeight ? InterpolationFlags.Area : InterpolationFlags.Linear);
        }
        else
        {
            decoded.CopyTo(gray);
        }

        using var unpadded = new Mat();
        Cv2.GaussianBlur(gray, unpadded, new Size(3, 3), 0);
        int pageWidth = unpadded.Cols;
        int pageHeight = unpadded.Rows;
        var unpaddedData = ToArray(unpadded);
        var (background, deviation) = MeasureBorder(unpaddedData, pageWidth, pageHeight, findInsets);

        // Bars along the page edges (e.g. black bars left and right of a white page) are no part of the page: the
        // page colour is the one just inside them, the bars count as background
        var bars = FindEdgeBars(unpaddedData, pageWidth, pageHeight);
        if (bars is { } edges)
        {
            background = edges.PageColour;
            deviation = edges.Deviation;
        }

        // A margin in the page colour all around, so art bleeding off the page has an edge like a panel frame
        int padding = findInsets ? Math.Max(4, (int)Math.Round(pageHeight * PaddingRatio)) : 0;
        using var blurred = new Mat();
        Cv2.CopyMakeBorder(unpadded, blurred, padding, padding, padding, padding, BorderTypes.Constant, new Scalar(background));
        int w = blurred.Cols;
        int h = blurred.Rows;
        var blurredData = ToArray(blurred);

        // Content: clearly different from the background, without specks of scan noise
        using var differenceMat = new Mat();
        Cv2.Absdiff(blurred, new Scalar(background), differenceMat);
        using var foreground = new Mat();
        Cv2.Threshold(differenceMat, foreground, ForegroundThreshold, 255, ThresholdTypes.Binary);
        using var speckKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        Cv2.MorphologyEx(foreground, foreground, MorphTypes.Open, speckKernel);
        var barRegions = bars is { } found ? found.Regions.Select(bar => new Region(bar.X0 + padding, bar.Y0 + padding, bar.X1 + padding, bar.Y1 + padding)).ToList() : [];
        foreach (var bar in barRegions)
        {
            Cv2.Rectangle(foreground, new Rect(bar.X0, bar.Y0, bar.Width, bar.Height), Scalar.All(0), -1);
        }

        // Background connected to the page border: the margins and gutters
        using var backgroundMask = new Mat();
        Cv2.BitwiseNot(foreground, backgroundMask);
        using var labels = new Mat();
        Cv2.ConnectedComponents(backgroundMask, labels, PixelConnectivity.Connectivity4, MatType.CV_32S);
        // Freshly allocated Mats are continuous
        var labelData = new int[w * h];
        Marshal.Copy(labels.Data, labelData, 0, labelData.Length);

        var foregroundData = ToArray(foreground);
        var outerLabels = new HashSet<int>();
        for (int x = 0; x < w; x++)
        {
            AddOuterLabel(x, 0);
            AddOuterLabel(x, h - 1);
        }
        for (int y = 0; y < h; y++)
        {
            AddOuterLabel(0, y);
            AddOuterLabel(w - 1, y);
        }

        var content = new byte[w * h];
        var difference = new byte[w * h];
        for (int i = 0; i < content.Length; i++)
        {
            bool isOuterBackground = foregroundData[i] == 0 && outerLabels.Contains(labelData[i]);
            content[i] = isOuterBackground ? (byte)0 : (byte)1;
            difference[i] = (byte)Math.Abs(blurredData[i] - background);
        }

        foreach (var bar in barRegions)
        {
            for (int y = bar.Y0; y < bar.Y1; y++)
            {
                Array.Clear(difference, y * w + bar.X0, bar.Width);
            }
        }

        // A real gutter is as flat as the page background, a light stripe in the art is only close to it
        double flatness = Math.Max(6.0, 2.5 * deviation + 2.0);
        return new ContentMask(content, difference, flatness, w, h, padding, pageWidth, pageHeight, blurredData, background);

        void AddOuterLabel(int x, int y)
        {
            int index = y * w + x;
            if (foregroundData[index] == 0)
            {
                outerLabels.Add(labelData[index]);
            }
        }
    }

    private static byte[] ToArray(Mat mat)
    {
        // Freshly allocated Mats are continuous, one copy replaces a P/Invoke per pixel access
        var data = new byte[mat.Rows * mat.Cols];
        Marshal.Copy(mat.Data, data, 0, data.Length);
        return data;
    }

    /// <summary>
    /// The background colour and its noise (median absolute deviation) of the page border. The background is the median,
    /// or the most common colour of the border: art bleeding off the page has many colours, the paper one.
    /// </summary>
    private static (byte Background, double Deviation) MeasureBorder(byte[] gray, int w, int h, bool mostCommon)
    {
        int border = Math.Max(2, Math.Min(w, h) / 100);
        var histogram = new int[256];
        for (int y = 0; y < h; y++)
        {
            bool isBorderRow = y < border || y >= h - border;
            for (int x = 0; x < w; x++)
            {
                if (isBorderRow || x < border || x >= w - border)
                {
                    histogram[gray[y * w + x]]++;
                }
            }
        }

        int background = Median(histogram);
        int peak = -1;
        for (int value = 0; value < 256 && mostCommon; value++)
        {
            int count = 0;
            for (int near = Math.Max(0, value - 2); near <= Math.Min(255, value + 2); near++)
            {
                count += histogram[near];
            }

            if (count > peak)
            {
                peak = count;
                background = value;
            }
        }

        // Only the border pixels of the paper, not the art bleeding off the page
        var deviations = new int[256];
        int range = mostCommon ? ForegroundThreshold : 255;
        for (int value = Math.Max(0, background - range); value <= Math.Min(255, background + range); value++)
        {
            deviations[Math.Abs(value - background)] += histogram[value];
        }

        return ((byte)background, Median(deviations));
    }

    /// <summary>
    /// Bars of one flat colour along opposite page edges, with a page of another colour inside them: the bars, the
    /// page colour just inside them and its noise. Null for a page without such bars.
    /// </summary>
    private static (List<Region> Regions, byte PageColour, double Deviation)? FindEdgeBars(byte[] gray, int w, int h)
    {
        int maxBar = Math.Min(w, h) / 5;
        // The number of lines from an edge which are (nearly) all one colour, the colour of the first one
        (int Size, int Colour) Measure(bool vertical, bool fromEnd)
        {
            int count = vertical ? w : h;
            int length = vertical ? h : w;
            int colour = -1;
            int size = 0;
            for (; size < Math.Min(maxBar, count); size++)
            {
                int line = fromEnd ? count - 1 - size : size;
                var histogram = new int[256];
                for (int i = 0; i < length; i++)
                {
                    histogram[vertical ? gray[i * w + line] : gray[line * w + i]]++;
                }

                int median = Median(histogram);
                colour = colour < 0 ? median : colour;
                int near = 0;
                for (int value = Math.Max(0, colour - ForegroundThreshold); value <= Math.Min(255, colour + ForegroundThreshold); value++)
                {
                    near += histogram[value];
                }

                if (near < length * EdgeBarUniformity)
                {
                    break;
                }
            }

            return (size, colour);
        }

        var left = Measure(true, false);
        var right = Measure(true, true);
        var top = Measure(false, false);
        var bottom = Measure(false, true);
        int minBar = Math.Max(2, Math.Min(w, h) / 200);
        bool SameBars((int Size, int Colour) a, (int Size, int Colour) b) =>
            a.Size >= minBar && b.Size >= minBar && Math.Abs(a.Colour - b.Colour) <= ForegroundThreshold;
        bool sides = SameBars(left, right);
        bool ends = SameBars(top, bottom);
        if (!sides && !ends)
        {
            return null;
        }

        // The page inside the bars of that colour; bars of the other colour (e.g. white margins) are part of it
        int barColour = sides ? left.Colour : top.Colour;
        bool IsBar((int Size, int Colour) bar) => bar.Size >= minBar && Math.Abs(bar.Colour - barColour) <= ForegroundThreshold;
        int x0 = IsBar(left) ? left.Size : 0, x1 = IsBar(right) ? w - right.Size : w;
        int y0 = IsBar(top) ? top.Size : 0, y1 = IsBar(bottom) ? h - bottom.Size : h;
        if (x1 - x0 < w / 2 || y1 - y0 < h / 2)
        {
            return null;
        }

        var inside = new byte[(x1 - x0) * (y1 - y0)];
        for (int y = y0; y < y1; y++)
        {
            Array.Copy(gray, y * w + x0, inside, (y - y0) * (x1 - x0), x1 - x0);
        }

        var (pageColour, deviation) = MeasureBorder(inside, x1 - x0, y1 - y0, true);
        // Only a page clearly of another colour, mostly that colour along its border
        if (Math.Abs(pageColour - barColour) <= ForegroundThreshold || BorderShare(inside, x1 - x0, y1 - y0, pageColour) < EdgeBarPageShare)
        {
            return null;
        }

        // With the blurred edge between a bar and the page
        var regions = new List<Region>();
        if (x0 > 0) regions.Add(new Region(0, 0, Math.Min(w, x0 + EdgeBarBlur), h));
        if (x1 < w) regions.Add(new Region(Math.Max(0, x1 - EdgeBarBlur), 0, w, h));
        if (y0 > 0) regions.Add(new Region(0, 0, w, Math.Min(h, y0 + EdgeBarBlur)));
        if (y1 < h) regions.Add(new Region(0, Math.Max(0, y1 - EdgeBarBlur), w, h));
        return (regions, pageColour, deviation);
    }

    /// <summary>The share of the border pixels within the foreground threshold of the colour.</summary>
    private static double BorderShare(byte[] gray, int w, int h, int colour)
    {
        int border = Math.Max(2, Math.Min(w, h) / 100);
        int near = 0, total = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (y < border || y >= h - border || x < border || x >= w - border)
                {
                    total++;
                    near += Math.Abs(gray[y * w + x] - colour) <= ForegroundThreshold ? 1 : 0;
                }
            }
        }

        return total == 0 ? 0 : (double)near / total;
    }

    private static int Median(int[] histogram)
    {
        int half = histogram.Sum() / 2;
        int sum = 0;
        for (int value = 0; value < histogram.Length; value++)
        {
            sum += histogram[value];
            if (sum > half)
            {
                return value;
            }
        }

        return histogram.Length - 1;
    }

    /// <param name="strictOnly">Only cut along clean gutters, used when cleared insets leave holes in the content</param>
    /// <param name="keepWhole">A panel without a frame next to a framed one of its size: no soft cuts through its light areas</param>
    private static void Split(ContentMask mask, Region region, int depth, bool isManga, bool strictOnly, List<Region> leaves, bool keepWhole = false)
    {
        if (mask.Trim(region) is not { } bounds)
        {
            return;
        }

        var cut = depth < MaxDepth ? ChooseCut(mask, bounds, strictOnly) : null;
        if (cut is { Soft: true } && keepWhole)
        {
            mask.Log?.Invoke($"kept whole (no frame, framed sibling of its size): {mask.Describe(bounds)} outline {mask.OutlineFrame(bounds):0.00}");
            cut = null;
        }

        if (cut is not { } chosen)
        {
            leaves.Add(bounds);
            return;
        }

        mask.Record(chosen);
        var parts = chosen.Parts;
        if (!chosen.Horizontal)
        {
            if (isManga)
            {
                parts.Reverse();
            }

            if (TryGetStaggeredRows(mask, parts, strictOnly) is { } rows)
            {
                // Row by row, in each row the columns in reading order
                for (int row = 0; row < rows[0].Count; row++)
                {
                    foreach (var column in rows)
                    {
                        Split(mask, column[row], depth + 2, isManga, strictOnly, leaves);
                    }
                }
                return;
            }
        }

        var unframed = UnframedBesideFramed(mask, parts);
        for (int i = 0; i < parts.Count; i++)
        {
            Split(mask, parts[i], depth + 1, isManga, strictOnly, leaves, unframed[i]);
        }
    }

    /// <summary>
    /// The parts of a cut without a frame which have a framed sibling of about the same size: such a part is a panel
    /// drawn without a frame (e.g. art on the white page), light areas in it are no gutters.
    /// </summary>
    private static bool[] UnframedBesideFramed(ContentMask mask, List<Region> parts)
    {
        var bounds = parts.Select(part => mask.Trim(part)).ToList();
        var scores = bounds.Select(region => region is { } b ? mask.OutlineFrame(b) : 0).ToList();
        var result = new bool[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            if (bounds[i] is not { } part || scores[i] >= UnframedOutline)
            {
                continue;
            }

            result[i] = Enumerable.Range(0, parts.Count).Any(j => j != i && bounds[j] is { } sibling && scores[j] >= FramedOutline &&
                Math.Abs(sibling.Width - part.Width) <= SiblingSizeTolerance * sibling.Width &&
                Math.Abs(sibling.Height - part.Height) <= SiblingSizeTolerance * sibling.Height);
        }

        return result;
    }

    private static CutResult? ChooseCut(ContentMask mask, Region bounds, bool strictOnly)
    {
        // Clean cuts first, rows before columns, then cuts through partly covered gutters
        foreach (var (horizontal, soft) in new[] { (true, false), (false, false), (true, true), (false, true) })
        {
            if (soft && strictOnly)
            {
                break;
            }

            if (Cut(mask, bounds, horizontal, soft) is { } cut)
            {
                return cut;
            }
        }

        // Gutters in their own colour, e.g. white gutters on a page with black margins
        return ColourCut(mask, bounds, true) ?? ColourCut(mask, bounds, false);
    }

    /// <summary>
    /// Cuts along bands which cross the whole region in one flat colour different from the page background. The first
    /// such cut on a page needs two bands of the same colour, that colour is then the gutter colour of the page.
    /// </summary>
    private static CutResult? ColourCut(ContentMask mask, Region region, bool horizontal)
    {
        var lines = mask.FlatLineColours(region, horizontal);
        var runs = new List<(int Start, int End, int Colour)>();
        int? start = null;
        for (int i = 0; i <= lines.Length; i++)
        {
            bool continues = i < lines.Length && lines[i] >= 0 && start is { } s && Math.Abs(lines[i] - lines[s]) <= ForegroundThreshold;
            bool begins = i < lines.Length && lines[i] >= 0 && start is null;
            if (continues)
            {
                continue;
            }

            if (start is { } runStart && i - runStart >= MinGutterSize && runStart > 0 && i < lines.Length)
            {
                runs.Add((runStart, i, lines[runStart]));
            }

            start = begins || (i < lines.Length && lines[i] >= 0) ? i : null;
        }

        // A run at the ends of the region is a margin, not a gutter
        double minPart = (horizontal ? mask.PageHeight : mask.PageWidth) * SoftCutMinPartRatio;
        runs = runs.Where(run => run.Start >= minPart && lines.Length - run.End >= minPart).ToList();

        int? colour = mask.GutterColour;
        runs = runs.Where(run => colour is not { } known || Math.Abs(run.Colour - known) <= ForegroundThreshold).ToList();
        if (colour is null)
        {
            var first = runs.FirstOrDefault();
            runs = runs.Where(run => Math.Abs(run.Colour - first.Colour) <= ForegroundThreshold).ToList();
            if (runs.Count < 2)
            {
                return null;
            }
        }

        if (runs.Count == 0)
        {
            return null;
        }

        // Only between parts which are big enough to be panels
        var parts = new List<(int Start, int End)>();
        int partStart = 0;
        foreach (var run in runs)
        {
            parts.Add((partStart, run.Start));
            partStart = run.End;
        }

        parts.Add((partStart, lines.Length));
        if (parts.Any(part => part.End - part.Start < minPart))
        {
            return null;
        }

        mask.GutterColour ??= runs[0].Colour;
        var regions = parts
            .Select(part => horizontal
                ? new Region(region.X0, region.Y0 + part.Start, region.X1, region.Y0 + part.End)
                : new Region(region.X0 + part.Start, region.Y0, region.X0 + part.End, region.Y1))
            .ToList();
        return new CutResult(region, horizontal, false, regions, runs.Select(run => (run.Start, run.End)).ToList());
    }

    /// <summary>
    /// When every column is cut into the same number of rows, and the rows of the columns end at about the same
    /// height, the gutters are only staggered and the page is read row by row. Returns the rows per column.
    /// </summary>
    private static List<List<Region>>? TryGetStaggeredRows(ContentMask mask, List<Region> columns, bool strictOnly)
    {
        var rowsPerColumn = new List<List<Region>>();
        foreach (var column in columns)
        {
            if (mask.Trim(column) is not { } bounds || ChooseCut(mask, bounds, strictOnly) is not { } cut || !cut.Horizontal)
            {
                return null;
            }

            rowsPerColumn.Add(cut.Parts.Select(part => mask.Trim(part) ?? part).ToList());
        }

        int rowCount = rowsPerColumn[0].Count;
        if (rowsPerColumn.Any(rows => rows.Count != rowCount))
        {
            return null;
        }

        double tolerance = mask.PageHeight * StaggeredRowTolerance;
        for (int row = 0; row < rowCount - 1; row++)
        {
            int bottom = rowsPerColumn[0][row].Y1;
            if (rowsPerColumn.Any(rows => Math.Abs(rows[row].Y1 - bottom) > tolerance))
            {
                return null;
            }
        }

        return rowsPerColumn;
    }

    private static CutResult? Cut(ContentMask mask, Region region, bool horizontal, bool soft)
    {
        var profile = mask.Profile(region, horizontal);
        int lineLength = horizontal ? region.Width : region.Height;
        double tolerance = lineLength * (soft ? SoftGutterTolerance : StrictGutterTolerance);

        var parts = new List<(int Start, int End)>();
        var gutters = new List<(int Start, int End)>();
        int partStart = 0;
        int? gutterStart = null;
        for (int i = 0; i <= profile.Length; i++)
        {
            bool isEmpty = i < profile.Length && profile[i] <= tolerance;
            if (isEmpty)
            {
                gutterStart ??= i;
                continue;
            }

            if (gutterStart is { } start && i < profile.Length && start > 0 && i - start >= MinGutterSize &&
                (soft || mask.IsCleanGutter(region, horizontal, start, i)))
            {
                parts.Add((partStart, start));
                gutters.Add((start, i));
                partStart = i;
            }
            gutterStart = null;
        }

        if (parts.Count == 0)
        {
            return null;
        }
        parts.Add((partStart, profile.Length));

        if (soft)
        {
            // Only cut through a covered gutter between parts which are big enough to be panels
            double minPart = (horizontal ? mask.PageHeight : mask.PageWidth) * SoftCutMinPartRatio;
            if (parts.Any(part => part.End - part.Start < minPart))
            {
                return null;
            }
        }

        var regions = parts
            .Select(part => horizontal
                ? new Region(region.X0, region.Y0 + part.Start, region.X1, region.Y0 + part.End)
                : new Region(region.X0 + part.Start, region.Y0, region.X0 + part.End, region.Y1))
            .ToList();

        return new CutResult(region, horizontal, soft, regions, gutters);
    }

    /// <summary>
    /// Replaces each big region which couldn't be cut, and which has closed panel frames inside, by those insets and
    /// the background panel(s) behind them, in reading order.
    /// </summary>
    private static List<Region> SplitInsets(ContentMask mask, List<Region> leaves, bool isManga)
    {
        double pageArea = (double)mask.PageWidth * mask.PageHeight;
        var result = new List<Region>();
        foreach (var leaf in leaves)
        {
            // A background panel bleeds off the page, normal panels with boxes in the art don't
            if (leaf.Area < pageArea * InsetRegionRatio || !mask.IsOnPageEdge(leaf))
            {
                result.Add(leaf);
                continue;
            }

            double minArea = Math.Max(pageArea * MinPanelAreaRatio, leaf.Area * MinInsetRegionRatio);
            var insets = FindInsets(mask.FrameLines, leaf, mask.PageEdgeSides(leaf), minArea, mask.PageWidth * MinPanelWidthRatio, mask.PageHeight * MinPanelHeightRatio,
                message => mask.Log?.Invoke($"region {mask.Describe(leaf)}: {message}"));
            if (insets.Count == 0)
            {
                result.Add(leaf);
                continue;
            }

            // The background panel(s): what is left with the insets cleared, holes are no gutters
            var saved = mask.Snapshot();
            foreach (var inset in insets)
            {
                mask.Clear(new Region(inset.X0 - InsetClearance, inset.Y0 - InsetClearance, inset.X1 + InsetClearance + 1, inset.Y1 + InsetClearance + 1));
            }

            var parts = new List<Region>();
            Split(mask, leaf, 1, isManga, true, parts);
            mask.Restore(saved);
            var backgrounds = parts
                .Where(part => insets.Sum(inset => part.Intersection(inset)) < BackgroundCoveredRatio * part.Area)
                .ToList();

            mask.Log?.Invoke($"region {mask.Describe(leaf)}: {insets.Count} framed insets {string.Join(", ", insets.Select(mask.Describe))}, " +
                             $"{backgrounds.Count} background panels {string.Join(", ", backgrounds.Select(mask.Describe))}");
            result.AddRange(InsertBackgrounds(ReadingOrder(insets, isManga), backgrounds));
        }

        return result;
    }

    /// <summary>
    /// Insets row by row, a row starts with the highest inset and takes the insets starting above its middle.
    /// </summary>
    private static List<Region> ReadingOrder(List<Region> insets, bool isManga)
    {
        var rows = new List<List<Region>>();
        foreach (var inset in insets.OrderBy(inset => inset.Y0))
        {
            if (rows.Count > 0 && inset.Y0 < rows[^1][0].Y0 + rows[^1][0].Height * 0.5)
            {
                rows[^1].Add(inset);
            }
            else
            {
                rows.Add([inset]);
            }
        }

        return rows
            .SelectMany(row => isManga ? row.OrderByDescending(inset => inset.X0) : row.OrderBy(inset => inset.X0))
            .ToList();
    }

    /// <summary>
    /// A background panel is read before the insets whose middle is below its upper third, after the others.
    /// </summary>
    private static List<Region> InsertBackgrounds(List<Region> insets, List<Region> backgrounds)
    {
        var order = new List<Region>(insets);
        foreach (var background in backgrounds)
        {
            double limit = background.Y0 + background.Height / 3.0;
            int position = order.FindIndex(region => insets.Contains(region) && (region.Y0 + region.Y1) / 2.0 > limit);
            order.Insert(position < 0 ? order.Count : position, background);
        }

        return order;
    }

    /// <summary>
    /// The closed panel frames in the region, without the frame of the region itself. A frame with other frames over
    /// it is the background panel, it is left out.
    /// </summary>
    private static List<Region> FindInsets(FrameLines lines, Region region, (bool Left, bool Top, bool Right, bool Bottom) pageEdges, double minArea, double minWidth, double minHeight, Action<string> log)
    {
        // Only lines which could be a good part of a frame side, the short lines of the art would chain all clusters
        var rows = Cluster(lines.Horizontal
            .Where(line => line.Position >= region.Y0 && line.Position < region.Y1 &&
                           Math.Min(line.End, region.X1) - Math.Max(line.Start, region.X0) >= lines.PageWidth * FrameSegmentRatio)
            .Select(line => line.Position));
        var columns = Cluster(lines.Vertical
            .Where(line => line.Position >= region.X0 && line.Position < region.X1 &&
                           Math.Min(line.End, region.Y1) - Math.Max(line.Start, region.Y0) >= lines.PageHeight * FrameSegmentRatio)
            .Select(line => line.Position));

        var frames = new List<(Region Frame, double Coverage, bool Covered)>();
        var nearFrames = new List<Region>();
        for (int i = 0; i < rows.Count; i++)
        {
            for (int j = i + 1; j < rows.Count; j++)
            {
                int top = rows[i];
                int bottom = rows[j];
                if (bottom - top < minHeight)
                {
                    continue;
                }

                var sides = columns.Where(x => lines.VerticalCoverage(x, top, bottom) >= CoveredFrameSide).ToList();
                for (int k = 0; k < sides.Count; k++)
                {
                    for (int l = k + 1; l < sides.Count; l++)
                    {
                        int left = sides[k];
                        int right = sides[l];
                        if (right - left < minWidth || (double)(right - left) * (bottom - top) < minArea)
                        {
                            continue;
                        }

                        double[] coverage =
                        [
                            lines.HorizontalCoverage(top, left, right),
                            lines.HorizontalCoverage(bottom, left, right),
                            lines.VerticalCoverage(left, top, bottom),
                            lines.VerticalCoverage(right, top, bottom)
                        ];
                        if (IsFrame(coverage))
                        {
                            frames.Add((new Region(left, top, right, bottom), coverage.Average(), coverage.Min() < FrameSide));
                        }
                        else if (coverage.Min() >= FrameSide - AmbiguousFrameRange && coverage.Average() >= MeanFrameCoverage - AmbiguousFrameRange)
                        {
                            nearFrames.Add(new Region(left, top, right, bottom));
                            if (DiagnoseFrames)
                            {
                                log($"near frame px x {left}-{right} y {top}-{bottom} coverage {string.Join("/", coverage.Select(c => c.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
                            }
                        }
                    }
                }
            }
        }

        var insets = SelectInsets(frames, region, pageEdges, log);
        foreach (var inset in insets.Where(_ => DiagnoseFrames))
        {
            double[] coverage =
            [
                lines.HorizontalCoverage(inset.Y0, inset.X0, inset.X1), lines.HorizontalCoverage(inset.Y1, inset.X0, inset.X1),
                lines.VerticalCoverage(inset.X0, inset.Y0, inset.Y1), lines.VerticalCoverage(inset.X1, inset.Y0, inset.Y1)
            ];
            log($"inset px x {inset.X0}-{inset.X1} y {inset.Y0}-{inset.Y1} coverage {string.Join("/", coverage.Select(c => c.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
        }
        // All or nothing: a frame close to the thresholds comes and goes with a slightly changed page, then the region
        // stays one panel
        double MinCoverage(Region frame) => new[]
        {
            lines.HorizontalCoverage(frame.Y0, frame.X0, frame.X1), lines.HorizontalCoverage(frame.Y1, frame.X0, frame.X1),
            lines.VerticalCoverage(frame.X0, frame.Y0, frame.Y1), lines.VerticalCoverage(frame.X1, frame.Y0, frame.Y1)
        }.Min();
        bool IsOpenCandidate(Region frame) =>
            frame.IntersectionOverUnion(region) <= 0.8 && frame.Area <= 0.85 * region.Area &&
            !insets.Any(inset => frame.IntersectionOverUnion(inset) > 0.5 || frame.Intersection(inset) >= 0.5 * inset.Area);
        if (insets.Count > 0 && (insets.Any(inset => MinCoverage(inset) < FrameSide + ClearFrameMargin) || nearFrames.Any(IsOpenCandidate)))
        {
            log($"ambiguous: {insets.Count(inset => MinCoverage(inset) < FrameSide + ClearFrameMargin)} insets close to the thresholds, {nearFrames.Count(IsOpenCandidate)} frames just below them");
            insets = [];
        }

        log($"{rows.Count} row lines, {columns.Count} column lines (columns {string.Join(" ", columns)}), {frames.Count} closed frames, {insets.Count} insets");
        return insets;
    }

    /// <summary>
    /// All sides of a frame are mostly drawn. One side may be covered by a balloon for a part, when the other sides
    /// are complete.
    /// </summary>
    private static bool IsFrame(double[] coverage)
    {
        var sorted = coverage.Order().ToArray();
        return sorted[0] >= FrameSide
            ? sorted.Average() >= MeanFrameCoverage
            : sorted[0] >= CoveredFrameSide && sorted[1] >= CompleteFrameSide;
    }

    private static List<Region> SelectInsets(List<(Region Frame, double Coverage, bool Covered)> frames, Region region,
        (bool Left, bool Top, bool Right, bool Bottom) pageEdges, Action<string> log)
    {
        int tolerance = FrameLineTolerance * 2;
        // A side on a gutter cut is a real panel border, only the sides on the page edge (where art bleeds off) count
        int SidesOnRegion(Region frame) =>
            (Math.Abs(frame.X0 - region.X0) <= tolerance && pageEdges.Left ? 1 : 0) +
            (Math.Abs(frame.Y0 - region.Y0) <= tolerance && pageEdges.Top ? 1 : 0) +
            (Math.Abs(frame.X1 - region.X1) <= tolerance && pageEdges.Right ? 1 : 0) +
            (Math.Abs(frame.Y1 - region.Y1) <= tolerance && pageEdges.Bottom ? 1 : 0);

        // Not the frame of the region itself, nor a strip along its edges closed by a line which is partly art
        var candidates = new List<(Region Frame, double Coverage, bool Covered)>();
        foreach (var frame in frames.OrderByDescending(frame => frame.Coverage).ThenByDescending(frame => frame.Frame.Area))
        {
            string? reason = frame.Frame.IntersectionOverUnion(region) > 0.8 || frame.Frame.Area > 0.85 * region.Area ? "region frame"
                : SidesOnRegion(frame.Frame) >= (frame.Covered ? 1 : 2) ? $"{SidesOnRegion(frame.Frame)} sides on the region"
                : null;
            if (reason is null)
            {
                candidates.Add(frame);
            }
            else
            {
                log($"frame {Describe(frame.Frame)} dropped: {reason}");
            }
        }

        // A double frame (a white border and a black line) or a line close by: the best drawn one
        var kept = new List<Region>();
        foreach (var (frame, _, _) in candidates)
        {
            if (kept.All(other => frame.IntersectionOverUnion(other) <= 0.8))
            {
                kept.Add(frame);
            }
            else
            {
                log($"frame {Describe(frame)} dropped: double frame");
            }
        }

        // A frame made of the sides of the frames inside it is two neighbouring panels, not a panel
        foreach (var frame in kept.Where(IsUnion).ToList())
        {
            log($"frame {Describe(frame)} dropped: union");
            kept.Remove(frame);
        }

        // A frame with other frames (mostly) over it is a background panel
        var insets = new List<Region>();
        foreach (var frame in kept)
        {
            if (kept.Any(other => other != frame && frame.Intersection(other) >= 0.5 * other.Area))
            {
                log($"frame {Describe(frame)} dropped: frames over it");
            }
            else
            {
                insets.Add(frame);
            }
        }

        return insets;

        static string Describe(Region frame) => $"px x {frame.X0}-{frame.X1} y {frame.Y0}-{frame.Y1}";

        bool IsUnion(Region frame)
        {
            var inside = kept.Where(other => other != frame && frame.Intersection(other) >= 0.9 * other.Area).ToList();
            return inside.Count >= 2 &&
                   inside.Any(other => Math.Abs(frame.X0 - other.X0) <= tolerance) &&
                   inside.Any(other => Math.Abs(frame.Y0 - other.Y0) <= tolerance) &&
                   inside.Any(other => Math.Abs(frame.X1 - other.X1) <= tolerance) &&
                   inside.Any(other => Math.Abs(frame.Y1 - other.Y1) <= tolerance);
        }
    }

    /// <summary>
    /// Line positions close to each other are one line (an edge on both sides of a frame line), the middle one.
    /// A cluster is never wider than the tolerance, so the frames of neighbouring panels stay apart.
    /// </summary>
    private static List<int> Cluster(IEnumerable<int> positions)
    {
        var clusters = new List<List<int>>();
        foreach (int position in positions.Distinct().Order())
        {
            if (clusters.Count > 0 && position - clusters[^1][0] <= FrameLineTolerance * 2)
            {
                clusters[^1].Add(position);
            }
            else
            {
                clusters.Add([position]);
            }
        }

        return clusters.Select(cluster => cluster[cluster.Count / 2]).ToList();
    }

    /// <param name="Gutters">The gutters of the cut, relative to the region (rows when horizontal, else columns)</param>
    private sealed record CutResult(Region Region, bool Horizontal, bool Soft, List<Region> Parts, List<(int Start, int End)> Gutters);

    private readonly record struct Region(int X0, int Y0, int X1, int Y1)
    {
        public int Width => X1 - X0;
        public int Height => Y1 - Y0;
        public double Area => (double)Width * Height;

        public double Intersection(Region other)
        {
            int width = Math.Min(X1, other.X1) - Math.Max(X0, other.X0);
            int height = Math.Min(Y1, other.Y1) - Math.Max(Y0, other.Y0);
            return width <= 0 || height <= 0 ? 0 : (double)width * height;
        }

        public double IntersectionOverUnion(Region other)
        {
            double intersection = Intersection(other);
            return intersection == 0 ? 0 : intersection / (Area + other.Area - intersection);
        }
    }

    /// <summary>
    /// Long straight horizontal and vertical edges of the page, to find closed panel frames.
    /// </summary>
    private sealed class FrameLines
    {
        private readonly int _width;
        private readonly int _height;
        // Prefix sums of the line masks (with a tolerance across the line): per row along x, per column along y
        private readonly ushort[] _rowSums;
        private readonly ushort[] _columnSums;

        public FrameLines(byte[] gray, int width, int height, int pageWidth, int pageHeight)
        {
            _width = width;
            _height = height;
            PageWidth = pageWidth;
            PageHeight = pageHeight;
            using var source = new Mat(height, width, MatType.CV_8UC1);
            Marshal.Copy(gray, 0, source.Data, gray.Length);
            using var edges = new Mat();
            Cv2.Canny(source, edges, CannyLowThreshold, CannyHighThreshold);
            using var joinKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.Dilate(edges, edges, joinKernel);

            // Thin lines only: dense hatching would close frames everywhere
            var (horizontal, horizontalThin) = KeepLines(edges, new Size(Math.Max(20, (int)(pageWidth * HorizontalFrameLineRatio)), 1));
            var (vertical, verticalThin) = KeepLines(edges, new Size(1, Math.Max(20, (int)(pageHeight * VerticalFrameLineRatio))));
            horizontal.Dispose();
            vertical.Dispose();
            horizontal = horizontalThin;
            vertical = verticalThin;
            Horizontal = Segments(horizontal, true);
            Vertical = Segments(vertical, false);

            using var horizontalTolerant = new Mat();
            using var acrossRows = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, 2 * FrameLineTolerance + 1));
            Cv2.Dilate(horizontal, horizontalTolerant, acrossRows);
            using var verticalTolerant = new Mat();
            using var acrossColumns = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2 * FrameLineTolerance + 1, 1));
            Cv2.Dilate(vertical, verticalTolerant, acrossColumns);
            horizontal.Dispose();
            vertical.Dispose();

            var rowsData = ToArray(horizontalTolerant);
            _rowSums = new ushort[(width + 1) * height];
            for (int y = 0; y < height; y++)
            {
                int sum = 0;
                for (int x = 0; x < width; x++)
                {
                    sum += rowsData[y * width + x] != 0 ? 1 : 0;
                    _rowSums[y * (width + 1) + x + 1] = (ushort)sum;
                }
            }

            var columnsData = ToArray(verticalTolerant);
            _columnSums = new ushort[(height + 1) * width];
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                for (int y = 0; y < height; y++)
                {
                    sum += columnsData[y * width + x] != 0 ? 1 : 0;
                    _columnSums[x * (height + 1) + y + 1] = (ushort)sum;
                }
            }
        }

        /// <summary>The size of the page without the margin.</summary>
        public int PageWidth { get; }
        public int PageHeight { get; }

        /// <summary>The horizontal line segments: the row, and the columns from start to end.</summary>
        public List<(int Position, int Start, int End)> Horizontal { get; }

        /// <summary>The vertical line segments: the column, and the rows from start to end.</summary>
        public List<(int Position, int Start, int End)> Vertical { get; }

        /// <summary>The part of the row from x0 to x1 which is on a horizontal line.</summary>
        public double HorizontalCoverage(int y, int x0, int x1)
        {
            int row = y * (_width + 1);
            return (double)(_rowSums[row + x1] - _rowSums[row + x0]) / Math.Max(1, x1 - x0);
        }

        /// <summary>The part of the column from y0 to y1 which is on a vertical line.</summary>
        public double VerticalCoverage(int x, int y0, int y1)
        {
            int column = x * (_height + 1);
            return (double)(_columnSums[column + y1] - _columnSums[column + y0]) / Math.Max(1, y1 - y0);
        }

        private static (Mat Lines, Mat Thin) KeepLines(Mat edges, Size length)
        {
            var lines = new Mat();
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, length);
            Cv2.MorphologyEx(edges, lines, MorphTypes.Open, kernel);

            // Dense hatching survives the long opening as a solid block, a line is thin across its direction
            using var solid = new Mat();
            using var across = Cv2.GetStructuringElement(MorphShapes.Rect,
                length.Width > length.Height ? new Size(1, FrameLineThickness) : new Size(FrameLineThickness, 1));
            Cv2.MorphologyEx(edges, solid, MorphTypes.Open, across);
            var thin = new Mat();
            Cv2.Subtract(lines, solid, thin);
            return (lines, thin);
        }

        private static List<(int Position, int Start, int End)> Segments(Mat lines, bool horizontal)
        {
            var components = Cv2.ConnectedComponentsEx(lines, PixelConnectivity.Connectivity8);
            return components.Blobs
                .Where(blob => blob.Label != 0)
                .Select(blob => horizontal
                    ? (blob.Top + blob.Height / 2, blob.Left, blob.Left + blob.Width)
                    : (blob.Left + blob.Width / 2, blob.Top, blob.Top + blob.Height))
                .ToList();
        }
    }

    /// <param name="padding">The margin added around the page</param>
    /// <param name="gray">The (blurred) page, for the frame lines</param>
    private sealed class ContentMask(byte[] content, byte[] difference, double flatness, int width, int height,
        int padding, int pageWidth, int pageHeight, byte[] gray, byte background)
    {
        private FrameLines? _frameLines;

        /// <summary>The size of the mask, the page with its margin.</summary>
        public int Width { get; } = width;
        public int Height { get; } = height;

        /// <summary>The size of the page without the margin.</summary>
        public int PageWidth { get; } = pageWidth;
        public int PageHeight { get; } = pageHeight;

        /// <summary>The frame lines of the page, only searched for when needed.</summary>
        public FrameLines FrameLines => _frameLines ??= new FrameLines(gray, Width, Height, PageWidth, PageHeight);

        /// <summary>The region on the page without the margin.</summary>
        public Region ToPage(Region region) => new(
            Math.Clamp(region.X0 - padding, 0, PageWidth), Math.Clamp(region.Y0 - padding, 0, PageHeight),
            Math.Clamp(region.X1 - padding, 0, PageWidth), Math.Clamp(region.Y1 - padding, 0, PageHeight));

        /// <summary>Which sides of the region are on the page edge.</summary>
        public (bool Left, bool Top, bool Right, bool Bottom) PageEdgeSides(Region region)
        {
            var page = ToPage(region);
            int marginX = (int)Math.Ceiling(PageWidth * PageEdgeRatio);
            int marginY = (int)Math.Ceiling(PageHeight * PageEdgeRatio);
            return (page.X0 <= marginX, page.Y0 <= marginY, page.X1 >= PageWidth - marginX, page.Y1 >= PageHeight - marginY);
        }

        public bool IsOnPageEdge(Region region)
        {
            var page = ToPage(region);
            int marginX = (int)Math.Ceiling(PageWidth * PageEdgeRatio);
            int marginY = (int)Math.Ceiling(PageHeight * PageEdgeRatio);
            return page.X0 <= marginX || page.Y0 <= marginY || page.X1 >= PageWidth - marginX || page.Y1 >= PageHeight - marginY;
        }

        /// <summary>The region on the page, normalized, for the log.</summary>
        public string Describe(Region region)
        {
            var page = ToPage(region);
            return $"x {(double)page.X0 / PageWidth:0.000}-{(double)page.X1 / PageWidth:0.000} y {(double)page.Y0 / PageHeight:0.000}-{(double)page.Y1 / PageHeight:0.000}";
        }

        public byte[] Snapshot() => (byte[])content.Clone();

        public void Restore(byte[] snapshot) => Array.Copy(snapshot, content, content.Length);

        /// <summary>Makes the region background, e.g. an inset over a background panel.</summary>
        public void Clear(Region region)
        {
            for (int y = Math.Max(0, region.Y0); y < Math.Min(Height, region.Y1); y++)
            {
                Array.Clear(content, y * Width + Math.Max(0, region.X0), Math.Max(0, Math.Min(Width, region.X1) - Math.Max(0, region.X0)));
            }
        }

        /// <summary>The frame evidence of all gutters which were cut along.</summary>
        public List<double> FrameEvidence { get; } = [];

        public Action<string>? Log { get; set; }

        public bool IsCleanGutter(Region region, bool horizontal, int start, int end) => IsFlat(region, horizontal, start, end);

        /// <summary>
        /// Remembers the frame evidence of the gutters which are cut along, and logs the cut.
        /// </summary>
        public void Record(CutResult cut)
        {
            foreach (var (start, end) in cut.Gutters)
            {
                double frames = MeasureFrames(cut.Region, cut.Horizontal, start, end);
                FrameEvidence.Add(frames);

                if (Log is not null)
                {
                    int offset = (cut.Horizontal ? cut.Region.Y0 : cut.Region.X0) - padding;
                    int size = cut.Horizontal ? PageHeight : PageWidth;
                    Log($"{(cut.Horizontal ? "row" : "column")} gutter{(cut.Soft ? " (soft)" : string.Empty)} " +
                        $"at {(double)(offset + start) / size:0.000}-{(double)(offset + end) / size:0.000}, thickness {(double)(end - start) / PageHeight:0.0000} of the page height, " +
                        $"frame evidence {frames:0.00}, in region {Describe(cut.Region)}");
                }
            }
        }

        /// <summary>
        /// How much of the lines next to the gutter, on the side with the least, is clearly different from the
        /// background: about 1 for a panel frame, less for the edge of art.
        /// </summary>
        public double MeasureFrames(Region region, bool horizontal, int start, int end)
        {
            double before = 0;
            double after = 0;
            for (int offset = 1; offset <= FrameSearchLines; offset++)
            {
                before = Math.Max(before, StrongFraction(region, horizontal, start - offset));
                after = Math.Max(after, StrongFraction(region, horizontal, end - 1 + offset));
            }

            return Math.Min(before, after);
        }

        private double StrongFraction(Region region, bool horizontal, int line)
        {
            if (line < 0 || line >= (horizontal ? region.Height : region.Width))
            {
                return 0;
            }

            int length = horizontal ? region.Width : region.Height;
            int strong = 0;
            for (int i = 0; i < length; i++)
            {
                int x = horizontal ? region.X0 + i : region.X0 + line;
                int y = horizontal ? region.Y0 + line : region.Y0 + i;
                if (difference[y * Width + x] > StrongDifference)
                {
                    strong++;
                }
            }

            return (double)strong / length;
        }

        /// <summary>
        /// How much of the outline of the region is a frame: per side the best of the first lines inside it, the
        /// weakest side. About 1 for a framed panel, low for art without a frame.
        /// </summary>
        public double OutlineFrame(Region region)
        {
            double weakest = 1;
            foreach (bool horizontal in new[] { true, false })
            {
                int size = horizontal ? region.Height : region.Width;
                double first = 0, last = 0;
                for (int offset = 0; offset < Math.Min(FrameSearchLines, size); offset++)
                {
                    first = Math.Max(first, StrongFraction(region, horizontal, offset));
                    last = Math.Max(last, StrongFraction(region, horizontal, size - 1 - offset));
                }

                weakest = Math.Min(weakest, Math.Min(first, last));
            }

            return weakest;
        }

        /// <summary>
        /// The region shrunk to its content, null when it has none.
        /// </summary>
        public Region? Trim(Region region)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = region.Y0; y < region.Y1; y++)
            {
                int row = y * Width;
                for (int x = region.X0; x < region.X1; x++)
                {
                    if (content[row + x] == 0)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    maxY = y;
                }
            }

            return maxX < 0 ? null : new Region(minX, minY, maxX + 1, maxY + 1);
        }

        /// <summary>
        /// Whether the lines start to end (rows when horizontal, else columns) of the region are as flat as the page
        /// background, the average difference to the background colour is small.
        /// </summary>
        public bool IsFlat(Region region, bool horizontal, int start, int end)
        {
            var lines = horizontal
                ? new Region(region.X0, region.Y0 + start, region.X1, region.Y0 + end)
                : new Region(region.X0 + start, region.Y0, region.X0 + end, region.Y1);
            long sum = 0;
            for (int y = lines.Y0; y < lines.Y1; y++)
            {
                int row = y * Width;
                for (int x = lines.X0; x < lines.X1; x++)
                {
                    sum += difference[row + x];
                }
            }

            return lines.Area == 0 || sum / lines.Area <= flatness;
        }

        /// <summary>The colour of the gutters when it differs from the page background, once found.</summary>
        public int? GutterColour { get; set; }

        /// <summary>
        /// Per line of the region (rows when horizontal, else columns): its colour when the whole line is flat in a
        /// colour clearly different from the background, else -1. The last pixels at both ends may differ (a blurred
        /// edge), nothing in between.
        /// </summary>
        public int[] FlatLineColours(Region region, bool horizontal)
        {
            int count = horizontal ? region.Height : region.Width;
            int length = horizontal ? region.Width : region.Height;
            var colours = new int[count];
            for (int i = 0; i < count; i++)
            {
                colours[i] = -1;
                if (length <= 2 * FlatLineEnds)
                {
                    continue;
                }

                // Most lines cross art, stop at the first pixel which makes the line too uneven
                long sum = 0;
                int min = 255, max = 0;
                int j = FlatLineEnds;
                for (; j < length - FlatLineEnds && max - min <= 2 * flatness; j++)
                {
                    int value = horizontal ? gray[(region.Y0 + i) * Width + region.X0 + j] : gray[(region.Y0 + j) * Width + region.X0 + i];
                    sum += value;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }

                int mean = (int)(sum / Math.Max(1, j - FlatLineEnds));
                if (j == length - FlatLineEnds && max - min <= 2 * flatness && Math.Abs(mean - background) > ForegroundThreshold)
                {
                    colours[i] = mean;
                }
            }

            return colours;
        }

        /// <summary>
        /// The number of content pixels per row (horizontal) or per column of the region.
        /// </summary>
        public int[] Profile(Region region, bool horizontal)
        {
            var profile = new int[horizontal ? region.Height : region.Width];
            for (int y = region.Y0; y < region.Y1; y++)
            {
                int row = y * Width;
                for (int x = region.X0; x < region.X1; x++)
                {
                    if (content[row + x] != 0)
                    {
                        profile[horizontal ? y - region.Y0 : x - region.X0]++;
                    }
                }
            }

            return profile;
        }
    }
}
