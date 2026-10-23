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

    /// <summary>
    /// Detects the panels in reading order, normalized to 0-1. Returns an empty list when the page has no panel
    /// structure (a splash page or a cover).
    /// </summary>
    public static List<ComicPanel> Detect(int pageIndex, byte[] pageData, bool isManga)
    {
        var mask = CreateContentMask(pageData);
        if (mask is null)
        {
            return [];
        }

        var leaves = new List<Region>();
        Split(mask, new Region(0, 0, mask.Width, mask.Height), 0, isManga, leaves);

        double pageArea = (double)mask.Width * mask.Height;
        var panels = leaves
            .Where(region => region.Area >= pageArea * MinPanelAreaRatio &&
                             region.Width >= mask.Width * MinPanelWidthRatio &&
                             region.Height >= mask.Height * MinPanelHeightRatio)
            .Select((region, index) => new ComicPanel
            {
                PageIndex = pageIndex,
                PanelIndex = index,
                X = (double)region.X0 / mask.Width,
                Y = (double)region.Y0 / mask.Height,
                Width = (double)region.Width / mask.Width,
                Height = (double)region.Height / mask.Height,
                Confidence = 1.0
            })
            .ToList();

        // Bands from edge to edge are text or a cover without panel borders, not panels
        bool allBands = panels.All(panel => panel.X <= PageEdgeRatio && panel.X + panel.Width >= 1 - PageEdgeRatio);
        return panels.Count > 1 && !allBands ? panels : [];
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

        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);
        int w = blurred.Cols;
        int h = blurred.Rows;

        var blurredData = ToArray(blurred);
        var (background, deviation) = MeasureBorder(blurredData, w, h);

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
        return new ContentMask(content, difference, flatness, w, h);

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
    /// The background colour (median) and its noise (median absolute deviation) of the page border.
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

        byte background = (byte)Median(histogram);
        var deviations = new int[256];
        for (int value = 0; value < 256; value++)
        {
            deviations[Math.Abs(value - background)] += histogram[value];
        }

        return (background, Median(deviations));
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

    private static void Split(ContentMask mask, Region region, int depth, bool isManga, List<Region> leaves)
    {
        if (mask.Trim(region) is not { } bounds)
        {
            return;
        }

        var cut = depth < MaxDepth ? ChooseCut(mask, bounds) : null;
        if (cut is not { } chosen)
        {
            leaves.Add(bounds);
            return;
        }

        var parts = chosen.Parts;
        if (!chosen.Horizontal)
        {
            if (isManga)
            {
                parts.Reverse();
            }

            if (TryGetStaggeredRows(mask, parts) is { } rows)
            {
                // Row by row, in each row the columns in reading order
                for (int row = 0; row < rows[0].Count; row++)
                {
                    foreach (var column in rows)
                    {
                        Split(mask, column[row], depth + 2, isManga, leaves);
                    }
                }
                return;
            }
        }

        foreach (var part in parts)
        {
            Split(mask, part, depth + 1, isManga, leaves);
        }
    }

    private static (bool Horizontal, List<Region> Parts)? ChooseCut(ContentMask mask, Region bounds)
    {
        // Clean cuts first, rows before columns, then cuts through partly covered gutters
        foreach (var (horizontal, soft) in new[] { (true, false), (false, false), (true, true), (false, true) })
        {
            if (Cut(mask, bounds, horizontal, soft) is { } parts)
            {
                return (horizontal, parts);
            }
        }

        return null;
    }

    /// <summary>
    /// When every column is cut into the same number of rows, and the rows of the columns end at about the same
    /// height, the gutters are only staggered and the page is read row by row. Returns the rows per column.
    /// </summary>
    private static List<List<Region>>? TryGetStaggeredRows(ContentMask mask, List<Region> columns)
    {
        var rowsPerColumn = new List<List<Region>>();
        foreach (var column in columns)
        {
            if (mask.Trim(column) is not { } bounds || ChooseCut(mask, bounds) is not { } cut || !cut.Horizontal)
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

        double tolerance = mask.Height * StaggeredRowTolerance;
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

    private static List<Region>? Cut(ContentMask mask, Region region, bool horizontal, bool soft)
    {
        var profile = mask.Profile(region, horizontal);
        int lineLength = horizontal ? region.Width : region.Height;
        double tolerance = lineLength * (soft ? SoftGutterTolerance : StrictGutterTolerance);

        var parts = new List<(int Start, int End)>();
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
                (soft || mask.IsFlat(region, horizontal, start, i)))
            {
                parts.Add((partStart, start));
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
            double minPart = (horizontal ? mask.Height : mask.Width) * SoftCutMinPartRatio;
            if (parts.Any(part => part.End - part.Start < minPart))
            {
                return null;
            }
        }

        return parts
            .Select(part => horizontal
                ? new Region(region.X0, region.Y0 + part.Start, region.X1, region.Y0 + part.End)
                : new Region(region.X0 + part.Start, region.Y0, region.X0 + part.End, region.Y1))
            .ToList();
    }

    private readonly record struct Region(int X0, int Y0, int X1, int Y1)
    {
        public int Width => X1 - X0;
        public int Height => Y1 - Y0;
        public double Area => (double)Width * Height;
    }

    private sealed class ContentMask(byte[] content, byte[] difference, double flatness, int width, int height)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;

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
