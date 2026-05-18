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

namespace StripWolf.Services;

/// <summary>
/// Comparer for comic page file paths.
/// Sorts directories first, then files within each directory.
/// Uses natural string ordering for proper numeric sorting.
/// </summary>
internal sealed class ComicPageComparer : IComparer<string>
{
    public static readonly ComicPageComparer Instance = new();

    private ComicPageComparer() { }

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        // Normalize path separators
        x = x.Replace('\\', '/');
        y = y.Replace('\\', '/');

        var xParts = x.Split('/');
        var yParts = y.Split('/');

        // Compare path components
        var minParts = Math.Min(xParts.Length, yParts.Length);
        
        for (var i = 0; i < minParts; i++)
        {
            var isLastX = i == xParts.Length - 1;
            var isLastY = i == yParts.Length - 1;
            
            // If one is a directory component and the other is a file, directory goes first
            if (!isLastX && isLastY)
            {
                // x has more path components (is in a subdirectory), compare at this level
                var cmp = NaturalCompare(xParts[i], yParts[i]);
                if (cmp != 0) return cmp;
                // If same prefix, directory path sorts before file at same level
                return -1;
            }
            if (isLastX && !isLastY)
            {
                var cmp = NaturalCompare(xParts[i], yParts[i]);
                if (cmp != 0) return cmp;
                // If same prefix, file sorts after directory at same level
                return 1;
            }

            // Both are at the same depth level, compare naturally
            var result = NaturalCompare(xParts[i], yParts[i]);
            if (result != 0)
            {
                return result;
            }
        }

        // If all compared parts are equal, shorter path comes first
        return xParts.Length.CompareTo(yParts.Length);
    }

    /// <summary>
    /// Natural string comparison that handles numbers correctly.
    /// "page2" comes before "page10".
    /// </summary>
    private static int NaturalCompare(string x, string y)
    {
        var xi = 0;
        var yi = 0;

        while (xi < x.Length && yi < y.Length)
        {
            var xc = x[xi];
            var yc = y[yi];

            // If both are digits, compare as numbers
            if (char.IsDigit(xc) && char.IsDigit(yc))
            {
                // Extract the full number from both strings
                var xNumStart = xi;
                while (xi < x.Length && char.IsDigit(x[xi])) xi++;
                if (!long.TryParse(x.AsSpan(xNumStart, xi - xNumStart), out var xNum))
                {
                    // Fallback to string comparison if number is too large
                    return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                }

                var yNumStart = yi;
                while (yi < y.Length && char.IsDigit(y[yi])) yi++;
                if (!long.TryParse(y.AsSpan(yNumStart, yi - yNumStart), out var yNum))
                {
                    // Fallback to string comparison if number is too large
                    return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                }

                var numCmp = xNum.CompareTo(yNum);
                if (numCmp != 0) return numCmp;
            }
            else
            {
                // Compare as characters (case-insensitive)
                var charCmp = char.ToLowerInvariant(xc).CompareTo(char.ToLowerInvariant(yc));
                if (charCmp != 0) return charCmp;
                xi++;
                yi++;
            }
        }

        // If we've exhausted one string, the shorter one comes first
        return (x.Length - xi).CompareTo(y.Length - yi);
    }
}

