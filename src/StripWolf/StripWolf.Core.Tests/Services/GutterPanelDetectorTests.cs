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
}
