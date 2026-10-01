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

using SQLite;

namespace StripWolf.Core.Models;

/// <summary>
/// Remembers which source files of a bookmarked (cloud/external) folder were imported by the background scan.
/// Without this, a comic which the user deleted from the library was copied and imported again by the next scan,
/// because the scan only checked if a file with that name still existed in the comics directory.
/// </summary>
public class BookmarkedFileImport
{
    /// <summary>
    /// "{BookmarkKey}|{RelativePath}", one row per source file
    /// </summary>
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the folder bookmark (bookmarks can be long base64 blobs), see CloudLibraryService.GetBookmarkKey
    /// </summary>
    [Indexed]
    public string BookmarkKey { get; set; } = string.Empty;

    /// <summary>
    /// Path of the source file relative to the bookmarked folder, '/' separated
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Size of the source file when it was imported, if the storage provider reported it
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// Last modification of the source file (UTC ticks) when it was imported, if the storage provider reported it
    /// </summary>
    public long? ModifiedUtcTicks { get; set; }

    public long ImportedUtcTicks { get; set; }

    public static string CreateKey(string bookmarkKey, string relativePath) => $"{bookmarkKey}|{relativePath}";
}
