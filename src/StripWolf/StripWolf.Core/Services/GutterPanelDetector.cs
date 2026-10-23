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
/// 8. In a big region which can't be cut, closed panel frames are searched: panels drawn over a background panel.
///    The page gets a margin in the page colour first, so art bleeding off the page has an edge too.
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
    private const double FrameSide = 0.85;
    private const double MeanFrameCoverage = 0.92;
    private const double CoveredFrameSide = 0.5;
    private const double CompleteFrameSide = 0.97;
    private const int CannyLowThreshold = 40;
    private const int CannyHighThreshold = 120;
    private const double HorizontalFrameLineRatio = 0.04;
    private const double VerticalFrameLineRatio = 0.03;

    /// <summary>
    /// Detects the panels in reading order, normalized to 0-1. Returns an empty list when the page has no panel
    /// structure (a splash page or a cover).
    /// </summary>
    /// <param name="log">Optional, receives the decisions for tuning the detection</param>
    public static List<ComicPanel> Detect(int pageIndex, byte[] pageData, bool isManga, Action<string>? log = null)
    {
        var mask = CreateContentMask(pageData);
        if (mask is null)
        {
            return [];
        }

        mask.Log = log;
        var page = new Region(0, 0, mask.Width, mask.Height);
        var leaves = new List<Region>();
        Split(mask, page, 0, isManga, false, leaves);
        leaves = SplitInsets(mask, leaves, isManga);

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

    private static ContentMask? CreateContentMask(byte[] pageData)
    {
        using var decoded = Cv2.ImDecode(pageData, ImreadModes.Grayscale);
        if (decoded.Empty())
        {
            return null;
        }

        using var gray = new Mat();
        if (decoded.Rows > WorkHeight)
        {
            int width = Math.Max(1, (int)Math.Round(decoded.Cols * (double)WorkHeight / decoded.Rows));
            Cv2.Resize(decoded, gray, new Size(width, WorkHeight), interpolation: InterpolationFlags.Area);
        }
        else
        {
            decoded.CopyTo(gray);
        }

        using var unpadded = new Mat();
        Cv2.GaussianBlur(gray, unpadded, new Size(3, 3), 0);
        int pageWidth = unpadded.Cols;
        int pageHeight = unpadded.Rows;
        var (background, deviation) = MeasureBorder(ToArray(unpadded), pageWidth, pageHeight);

        // A margin in the page colour all around, so art bleeding off the page has an edge like a panel frame
        int padding = Math.Max(4, (int)Math.Round(pageHeight * PaddingRatio));
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

        // A real gutter is as flat as the page background, a light stripe in the art is only close to it
        double flatness = Math.Max(6.0, 2.5 * deviation + 2.0);
        return new ContentMask(content, difference, flatness, w, h, padding, pageWidth, pageHeight, blurredData);

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
    /// The background colour and its noise (median absolute deviation) of the page border. The background is the most
    /// common colour of the border: art bleeding off the page has many colours, the paper one.
    /// </summary>
    private static (byte Background, double Deviation) MeasureBorder(byte[] gray, int w, int h)
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

        int background = 0;
        int peak = -1;
        for (int value = 0; value < 256; value++)
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
        for (int value = Math.Max(0, background - ForegroundThreshold); value <= Math.Min(255, background + ForegroundThreshold); value++)
        {
            deviations[Math.Abs(value - background)] += histogram[value];
        }

        return ((byte)background, Median(deviations));
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
    private static void Split(ContentMask mask, Region region, int depth, bool isManga, bool strictOnly, List<Region> leaves)
    {
        if (mask.Trim(region) is not { } bounds)
        {
            return;
        }

        var cut = depth < MaxDepth ? ChooseCut(mask, bounds, strictOnly) : null;
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

        foreach (var part in parts)
        {
            Split(mask, part, depth + 1, isManga, strictOnly, leaves);
        }
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

        return null;
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
            if (leaf.Area < pageArea * InsetRegionRatio)
            {
                result.Add(leaf);
                continue;
            }

            double minArea = Math.Max(pageArea * MinPanelAreaRatio, leaf.Area * MinInsetRegionRatio);
            var insets = FindInsets(mask.FrameLines, leaf, minArea, mask.PageWidth * MinPanelWidthRatio, mask.PageHeight * MinPanelHeightRatio);
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
    private static List<Region> FindInsets(FrameLines lines, Region region, double minArea, double minWidth, double minHeight)
    {
        var rows = Cluster(lines.Horizontal
            .Where(line => line.Position >= region.Y0 && line.Position < region.Y1 && line.End > region.X0 && line.Start < region.X1)
            .Select(line => line.Position));
        var columns = Cluster(lines.Vertical
            .Where(line => line.Position >= region.X0 && line.Position < region.X1 && line.End > region.Y0 && line.Start < region.Y1)
            .Select(line => line.Position));

        var frames = new List<(Region Frame, double Coverage, bool Covered)>();
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
                    }
                }
            }
        }

        return SelectInsets(frames, region);
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

    private static List<Region> SelectInsets(List<(Region Frame, double Coverage, bool Covered)> frames, Region region)
    {
        int tolerance = FrameLineTolerance * 2;
        int SidesOnRegion(Region frame) =>
            (Math.Abs(frame.X0 - region.X0) <= tolerance ? 1 : 0) + (Math.Abs(frame.Y0 - region.Y0) <= tolerance ? 1 : 0) +
            (Math.Abs(frame.X1 - region.X1) <= tolerance ? 1 : 0) + (Math.Abs(frame.Y1 - region.Y1) <= tolerance ? 1 : 0);

        // Not the frame of the region itself, nor a strip along its edges closed by a line which is partly art
        var candidates = frames
            .Where(frame => frame.Frame.IntersectionOverUnion(region) <= 0.8 && frame.Frame.Area <= 0.85 * region.Area &&
                            !(frame.Covered && SidesOnRegion(frame.Frame) >= 2))
            .OrderByDescending(frame => frame.Coverage)
            .ThenByDescending(frame => frame.Frame.Area);

        // A double frame (a white border and a black line) or a line close by: the best drawn one
        var kept = new List<Region>();
        foreach (var (frame, _, _) in candidates)
        {
            if (kept.All(other => frame.IntersectionOverUnion(other) <= 0.8))
            {
                kept.Add(frame);
            }
        }

        // A frame made of the sides of the frames inside it is two neighbouring panels, not a panel
        kept = kept.Where(frame => !IsUnion(frame)).ToList();

        // A frame with other frames (mostly) over it is a background panel
        return kept
            .Where(frame => !kept.Any(other => other != frame && frame.Intersection(other) >= 0.5 * other.Area))
            .ToList();

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
    /// </summary>
    private static List<int> Cluster(IEnumerable<int> positions)
    {
        var clusters = new List<List<int>>();
        foreach (int position in positions.Distinct().Order())
        {
            if (clusters.Count > 0 && position - clusters[^1][^1] <= FrameLineTolerance * 2)
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
            using var source = new Mat(height, width, MatType.CV_8UC1);
            Marshal.Copy(gray, 0, source.Data, gray.Length);
            using var edges = new Mat();
            Cv2.Canny(source, edges, CannyLowThreshold, CannyHighThreshold);
            using var joinKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.Dilate(edges, edges, joinKernel);

            using var horizontal = KeepLines(edges, new Size(Math.Max(20, (int)(pageWidth * HorizontalFrameLineRatio)), 1));
            using var vertical = KeepLines(edges, new Size(1, Math.Max(20, (int)(pageHeight * VerticalFrameLineRatio))));
            Horizontal = Segments(horizontal, true);
            Vertical = Segments(vertical, false);

            using var horizontalTolerant = new Mat();
            using var acrossRows = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, 2 * FrameLineTolerance + 1));
            Cv2.Dilate(horizontal, horizontalTolerant, acrossRows);
            using var verticalTolerant = new Mat();
            using var acrossColumns = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2 * FrameLineTolerance + 1, 1));
            Cv2.Dilate(vertical, verticalTolerant, acrossColumns);

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

        private static Mat KeepLines(Mat edges, Size length)
        {
            var lines = new Mat();
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, length);
            Cv2.MorphologyEx(edges, lines, MorphTypes.Open, kernel);
            return lines;
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
        int padding, int pageWidth, int pageHeight, byte[] gray)
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
