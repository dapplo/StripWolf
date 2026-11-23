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
/// How the reader decodes comic pages.
/// </summary>
public enum ReaderImageQuality
{
    /// <summary>
    /// Pages are decoded at the resolution of the screen plus a margin, so they look the same as the original.
    /// Zooming in reloads the page at its full resolution.
    /// </summary>
    High,

    /// <summary>
    /// Pages are always decoded at their full resolution: zooming in is sharp immediately, but uses the most memory.
    /// </summary>
    Original,

    /// <summary>
    /// For devices with little memory or a slow CPU: pages are decoded at exactly the screen size with a faster
    /// scaler, and only the next page (or spread) is prepared in advance.
    /// </summary>
    SaveMemory
}
