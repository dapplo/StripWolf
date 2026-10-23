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

namespace StripWolf.PanelBench;

public readonly record struct Match(int ExpectedIndex, int DetectedIndex, double Iou);

/// <summary>
/// The comparison of the detected panels of one page with the expected panels.
/// </summary>
public sealed record PageScore(string Comic, string Page, int Expected, int Detected, IReadOnlyList<Match> Matches)
{
    /// <summary>IoU a detected panel needs to count as found.</summary>
    public const double MatchIou = 0.5;


    public int Found => Matches.Count;

    public double MeanIou => Matches.Count == 0 ? 0 : Matches.Average(match => match.Iou);

    /// <summary>
    /// All panels found and nothing extra. What counts is the number and order of the panels, a few pixels more or less
    /// around a panel don't matter, so a match (IoU of at least <see cref="MatchIou"/>) is enough.
    /// </summary>
    public bool IsCorrect => Expected == Detected && Found == Expected;

    /// <summary>Correct and also in the expected reading order.</summary>
    public bool IsCorrectInOrder => IsCorrect &&
        Matches.OrderBy(match => match.DetectedIndex).Select(match => match.ExpectedIndex).SequenceEqual(Enumerable.Range(0, Expected));
}

public static class Scoring
{
    /// <summary>
    /// Greedy one to one matching of the panels with the highest IoU first.
    /// </summary>
    public static List<Match> MatchPanels(IReadOnlyList<Box> expected, IReadOnlyList<Box> detected, double minIou)
    {
        var pairs = new List<Match>();
        for (int e = 0; e < expected.Count; e++)
        {
            for (int d = 0; d < detected.Count; d++)
            {
                double iou = expected[e].IntersectionOverUnion(detected[d]);
                if (iou >= minIou)
                {
                    pairs.Add(new Match(e, d, iou));
                }
            }
        }

        var usedExpected = new HashSet<int>();
        var usedDetected = new HashSet<int>();
        var matches = new List<Match>();
        foreach (var pair in pairs.OrderByDescending(pair => pair.Iou))
        {
            if (usedExpected.Contains(pair.ExpectedIndex) || usedDetected.Contains(pair.DetectedIndex))
            {
                continue;
            }

            usedExpected.Add(pair.ExpectedIndex);
            usedDetected.Add(pair.DetectedIndex);
            matches.Add(pair);
        }

        return matches;
    }

    public static PageScore ScorePage(string comic, string page, IReadOnlyList<Box> expected, IReadOnlyList<Box> detected) =>
        new(comic, page, expected.Count, detected.Count, MatchPanels(expected, detected, PageScore.MatchIou));

    /// <summary>
    /// Whether two detection results of (variants of) the same page are practically the same.
    /// </summary>
    public static bool IsSameResult(IReadOnlyList<Box> first, IReadOnlyList<Box> second, double minIou = 0.85)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        return MatchPanels(first, second, minIou).Count == first.Count;
    }
}
