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
/// Controls page order and navigation direction in the reader.
/// </summary>
public enum ReadingDirectionMode
{
    /// <summary>
    /// Detect reading direction from metadata (Komga or ComicInfo).
    /// </summary>
    Automatic,

    /// <summary>
    /// Western reading (start at first page, next is right).
    /// </summary>
    LeftToRight,

    /// <summary>
    /// Manga reading (start at last page, next is left).
    /// </summary>
    RightToLeft,

    /// <summary>
    /// Start at last page but keep western next/previous controls.
    /// </summary>
    LeftToRightReversedPages,

    /// <summary>
    /// Start at first page but use manga-style next/previous controls.
    /// </summary>
    RightToLeftReversedPages
}
