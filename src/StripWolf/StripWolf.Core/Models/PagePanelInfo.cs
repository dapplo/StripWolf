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

namespace StripWolf.Core.Models;

/// <summary>
/// Contains cached panel detection results for a comic page
/// </summary>
public class PagePanelInfo
{
    /// <summary>
    /// The page index
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// List of detected panels in reading order (top-left to bottom-right)
    /// </summary>
    public List<ComicPanel> Panels { get; set; } = [];

    /// <summary>
    /// Whether the detection was successful
    /// </summary>
    public bool DetectionSuccessful { get; set; }

    /// <summary>
    /// If detection failed or page is a splash page, this will be true
    /// </summary>
    public bool IsSplashPage { get; set; }

    /// <summary>
    /// Time when this was detected (for cache management)
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

