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

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace StripWolf.Core.Services;

/// <summary>
/// Service interface for cross-platform, cloud-agnostic comic library import and folder access.
/// Handles folder picking, folder access token persistence (bookmarking), and streaming data.
/// </summary>
public interface ICloudLibraryService
{
    /// <summary>
    /// Prompts the user to select a folder, saves its security-scoped bookmark, and persists it in settings.
    /// </summary>
    /// <param name="storageProvider">The platform storage provider.</param>
    /// <returns>The selected folder storage item, or null if cancelled.</returns>
    Task<IStorageFolder?> SelectAndBookmarkFolderAsync(IStorageProvider storageProvider);

    /// <summary>
    /// Restores all active folders from the persisted bookmarks.
    /// </summary>
    /// <param name="storageProvider">The platform storage provider.</param>
    /// <returns>A list of successfully restored storage folders.</returns>
    Task<List<IStorageFolder>> GetBookmarkedFoldersAsync(IStorageProvider storageProvider);

    /// <summary>
    /// Recursively enumerates all supported comic files within the specified folder.
    /// </summary>
    /// <param name="folder">The folder to scan.</param>
    /// <returns>An async enumerable of comic storage files.</returns>
    IAsyncEnumerable<IStorageFile> EnumerateComicFilesAsync(IStorageFolder folder);

    /// <summary>
    /// Copies the contents of a storage file to a local destination directory using stream copying (OpenReadAsync).
    /// </summary>
    /// <param name="file">The source storage file.</param>
    /// <param name="targetDirectory">The local directory to copy the file to.</param>
    /// <returns>The absolute path of the local file copy, or null if copy fails.</returns>
    Task<string?> CopyToLocalDirectoryAsync(IStorageFile file, string targetDirectory);

    /// <summary>
    /// Removes a folder bookmark from persistence.
    /// </summary>
    /// <param name="bookmark">The bookmark token to remove.</param>
    Task RemoveBookmarkAsync(string bookmark);
}
