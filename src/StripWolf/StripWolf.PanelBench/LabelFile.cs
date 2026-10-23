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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace StripWolf.PanelBench;

/// <summary>
/// A panel rectangle, normalized to 0-1 of the page size, like ComicPanel.
/// </summary>
public sealed record Box(double X, double Y, double W, double H)
{
    public double Area => W * H;

    public double IntersectionOverUnion(Box other)
    {
        double x1 = Math.Max(X, other.X);
        double y1 = Math.Max(Y, other.Y);
        double x2 = Math.Min(X + W, other.X + other.W);
        double y2 = Math.Min(Y + H, other.Y + other.H);
        if (x2 <= x1 || y2 <= y1)
        {
            return 0;
        }

        double intersection = (x2 - x1) * (y2 - y1);
        return intersection / (Area + other.Area - intersection);
    }
}

/// <summary>
/// The panels of one page in reading order. Only reviewed pages are used for scoring, the label editor pre-fills
/// unreviewed pages with the detection result.
/// </summary>
public sealed class PageLabel
{
    public string Name { get; set; } = string.Empty;
    public bool Reviewed { get; set; }
    public List<Box> Panels { get; set; } = [];
}

/// <summary>
/// The expected panels of one comic, stored as "&lt;comic file&gt;.panels.json" next to the comic.
/// </summary>
public sealed class LabelFile
{
    public int Version { get; set; } = 1;
    public string Comic { get; set; } = string.Empty;
    public List<PageLabel> Pages { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string PathFor(string comicPath) => comicPath + ".panels.json";

    /// <summary>
    /// Loads the labels next to the comic, or else from the folder of the label editor (where the browser saves them
    /// by default).
    /// </summary>
    public static LabelFile? TryLoad(string comicPath, string editorFolder)
    {
        var path = PathFor(comicPath);
        if (!File.Exists(path))
        {
            path = Path.Combine(editorFolder, Path.GetFileName(path));
            if (!File.Exists(path))
            {
                return null;
            }
        }

        return JsonSerializer.Deserialize<LabelFile>(File.ReadAllText(path), JsonOptions);
    }
}
