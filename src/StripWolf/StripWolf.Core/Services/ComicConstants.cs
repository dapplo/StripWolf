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

namespace StripWolf.Core.Services;

/// <summary>
/// Constants for comic book file handling
/// </summary>
public static class ComicConstants
{
    private const string IgnoredImportDirectoryName = "__MACOSX";

    /// <summary>
    /// Supported image file extensions for comic pages
    /// </summary>
    public static readonly string[] ImageExtensions = 
    [
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif", ".avif"
    ];

    /// <summary>
    /// Supported comic book file extensions
    /// </summary>
    public static readonly string[] ComicExtensions = 
    [
        ".cbz", ".cbr", ".cb7", ".cbt", ".pdf"
#if !DISABLE_EPUB_SUPPORT
        , ".epub"
#endif
    ];

    public static readonly string[] ComicFilePickerPatterns =
        ComicExtensions.Select(extension => $"*{extension}").ToArray();

    /// <summary>
    /// Checks if a filename is an image file
    /// </summary>
    public static bool IsImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }

    /// <summary>
    /// Checks if a filename is a supported comic file.
    /// </summary>
    public static bool IsSupportedComicFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return ComicExtensions.Contains(extension);
    }

    /// <summary>
    /// Checks if a filename is a ComicInfo.xml file
    /// </summary>
    public static bool IsComicInfoFile(string fileName)
    {
        return Path.GetFileName(fileName).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a path points to content inside an ignored import directory.
    /// </summary>
    public static bool IsIgnoredImportPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals(IgnoredImportDirectoryName, StringComparison.OrdinalIgnoreCase));
    }
}

