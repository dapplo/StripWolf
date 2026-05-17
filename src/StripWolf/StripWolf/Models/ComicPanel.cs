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

namespace StripWolf.Models;

/// <summary>
/// Information about a single comic panel on a page
/// </summary>
public class ComicPanel
{
    /// <summary>
    /// The page index this panel belongs to
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// The index of this panel on the page
    /// </summary>
    public int PanelIndex { get; set; }

    /// <summary>
    /// X coordinate of the panel (normalized 0-1)
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y coordinate of the panel (normalized 0-1)
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Width of the panel (normalized 0-1)
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Height of the panel (normalized 0-1)
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Confidence of the detection (0-1)
    /// </summary>
    public double Confidence { get; set; }
}

