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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

/// <summary>
/// The gutter split panel detection on synthetic pages. Only built with the OpenCV panel detection.
/// </summary>
public sealed class GutterPanelDetectorTests
{
    private const byte Black = 10;
    private const byte White = 250;

    /// <summary>
    /// A page with black bars left and right and a black top and bottom edge, the page background is black. Four full
    /// width panels are separated by white gutters which only run between the bars.
    /// </summary>
    private static byte[] CreatePageWithWhiteGuttersOnBlack(int width, int height, int[] gutterRows, int gutterSize)
    {
        int bar = width * 103 / 2400;
        int edge = height * 40 / 3392;
        var random = new Random(42);
        using var image = new Image<L8>(width, height, new L8(Black));
        image.ProcessPixelRows(accessor =>
        {
            for (int y = edge; y < height - edge; y++)
            {
                var row = accessor.GetRowSpan(y);
                bool isGutter = gutterRows.Any(gutter => y >= gutter && y < gutter + gutterSize);
                for (int x = bar; x < width - bar; x++)
                {
                    // Art: blocks of different grey, never flat across a whole row
                    row[x] = isGutter ? new L8(White) : new L8((byte)(60 + ((x / 23 + y / 31) % 7) * 25 + random.Next(10)));
                }
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(2400, 3392)]
    [InlineData(1200, 1696)]
    public void Detect_WhiteGuttersOnBlackPage_FindsTheFourPanelsInOrder(int width, int height)
    {
        int[] gutterRows = [height * 831 / 3392, height * 1665 / 3392, height * 2505 / 3392];
        int gutterSize = Math.Max(4, height * 14 / 3392);
        var page = CreatePageWithWhiteGuttersOnBlack(width, height, gutterRows, gutterSize);

        foreach (bool findInsets in new[] { false, true })
        {
            var log = new List<string>();
            var panels = GutterPanelDetector.Detect(0, page, false, log.Add, findInsets);

            Assert.True(panels.Count == 4, string.Join(" | ", log));
            double[] expectedBottoms = [831 / 3392.0, 1665 / 3392.0, 2505 / 3392.0, 1.0];
            for (int i = 0; i < panels.Count; i++)
            {
                Assert.InRange(panels[i].X, 0.03, 0.06);
                Assert.InRange(panels[i].X + panels[i].Width, 0.94, 0.97);
                Assert.InRange(panels[i].Y + panels[i].Height, expectedBottoms[i] - 0.02, expectedBottoms[i] + 0.02);
            }
        }
    }

    /// <summary>
    /// A white page between black bars left and right, with white margins at the top and bottom. Row 1 has a framed
    /// panel and next to it a panel without a frame (art on the white background), row 2 one wide panel, row 3 two
    /// panels. The panels are framed by a black line and filled with art.
    /// </summary>
    private static byte[] CreateWhitePageBetweenBlackBars(int width, int height)
    {
        int bar = width * 96 / 2400;
        int Px(double fraction, int size) => (int)(fraction * size);
        var framed = new[]
        {
            (X0: 0.092, Y0: 0.041, X1: 0.495, Y1: 0.369),
            (X0: 0.093, Y0: 0.374, X1: 0.906, Y1: 0.601),
            (X0: 0.092, Y0: 0.606, X1: 0.496, Y1: 0.955),
            (X0: 0.502, Y0: 0.606, X1: 0.906, Y1: 0.955)
        };
        // The unframed panel, the size of the framed one next to it: two pieces of art on the white page with a wide
        // white area between them, which a figure partly crosses
        var unframedArt = new[]
        {
            (X0: 0.502, Y0: 0.041, X1: 0.906, Y1: 0.15), (X0: 0.502, Y0: 0.21, X1: 0.906, Y1: 0.369), (X0: 0.70, Y0: 0.14, X1: 0.74, Y1: 0.22)
        };
        var random = new Random(7);
        using var image = new Image<L8>(width, height, new L8(White));
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    if (x < bar || x >= width - bar)
                    {
                        row[x] = new L8(Black);
                        continue;
                    }

                    byte art = (byte)(60 + ((x / 23 + y / 31) % 7) * 25 + random.Next(10));
                    foreach (var panel in framed)
                    {
                        int x0 = Px(panel.X0, width), x1 = Px(panel.X1, width), y0 = Px(panel.Y0, height), y1 = Px(panel.Y1, height);
                        if (x >= x0 && x < x1 && y >= y0 && y < y1)
                        {
                            bool isFrame = x < x0 + 4 || x >= x1 - 4 || y < y0 + 4 || y >= y1 - 4;
                            row[x] = new L8(isFrame ? Black : art);
                        }
                    }

                    // Art without a frame: light colours with a few dark strokes
                    if (unframedArt.Any(piece => x >= Px(piece.X0, width) && x < Px(piece.X1, width) && y >= Px(piece.Y0, height) && y < Px(piece.Y1, height)))
                    {
                        row[x] = new L8((x + y) % 60 < 2 ? (byte)40 : (byte)(215 + random.Next(15)));
                    }
                }
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(2400, 3392)]
    [InlineData(1200, 1696)]
    public void Detect_WhitePageBetweenBlackBars_FindsTheFivePanelsInOrder(int width, int height)
    {
        var page = CreateWhitePageBetweenBlackBars(width, height);

        foreach (bool findInsets in new[] { false, true })
        {
            var log = new List<string>();
            var panels = GutterPanelDetector.Detect(0, page, false, log.Add, findInsets);

            Assert.True(panels.Count == 5, string.Join(" | ", log));
            // Row 1 framed left, unframed right; row 2 wide; row 3 left and right
            (double X, double Y)[] centres = [(0.29, 0.20), (0.71, 0.20), (0.50, 0.49), (0.29, 0.78), (0.70, 0.78)];
            for (int i = 0; i < panels.Count; i++)
            {
                Assert.InRange(centres[i].X, panels[i].X, panels[i].X + panels[i].Width);
                Assert.InRange(centres[i].Y, panels[i].Y, panels[i].Y + panels[i].Height);
            }

            // The unframed panel stays whole across the white area in it
            Assert.InRange(panels[1].Y, 0.03, 0.06);
            Assert.InRange(panels[1].Y + panels[1].Height, 0.35, 0.38);
        }
    }

    /// <summary>
    /// One full-bleed panel: art over the top of the page down to a straight edge, below it a flat grey floor with a
    /// small object on it. There is no frame anywhere, the art is not much darker than the floor.
    /// </summary>
    private static byte[] CreateFullBleedPageWithToneEdge(int width, int height)
    {
        int edge = height * 73 / 100;
        var random = new Random(3);
        using var image = new Image<L8>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    bool isObject = x > width * 22 / 100 && x < width * 71 / 100 && y > height * 88 / 100 && y < height * 97 / 100;
                    row[x] = y < edge || isObject
                        ? new L8((byte)(140 + ((x / 19 + y / 27) % 6) * 20 + random.Next(4)))
                        : new L8((byte)(196 + random.Next(4)));
                }
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(2400, 3392)]
    [InlineData(1200, 1696)]
    public void Detect_FullBleedPageWithToneEdge_IsOnePanel(int width, int height)
    {
        var page = CreateFullBleedPageWithToneEdge(width, height);

        foreach (bool findInsets in new[] { false, true })
        {
            var log = new List<string>();
            var panels = GutterPanelDetector.Detect(0, page, false, log.Add, findInsets);

            // No panels: the reader shows the whole page
            Assert.True(panels.Count == 0, string.Join(" | ", log));
        }
    }
}