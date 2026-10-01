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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace StripWolf.Core.Services;

/// <summary>
/// Service implementing ICloudLibraryService to handle cross-platform folder access
/// using Avalonia's StorageProvider and the Bookmark pattern.
/// </summary>
public class CloudLibraryService : ICloudLibraryService
{
    /// <summary>
    /// Extension of files which are still being copied, it's not a supported comic extension so scans ignore them
    /// </summary>
    private const string PartialFileExtension = ".partial";

    private readonly SettingsService _settingsService;

    public CloudLibraryService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// A short, stable key for a bookmark (bookmarks can be long base64 blobs), used to track imported files
    /// </summary>
    public static string GetBookmarkKey(string bookmark)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bookmark)));
    }

    public async Task<IStorageFolder?> SelectAndBookmarkFolderAsync(IStorageProvider storageProvider)
    {
        try
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Comic Folder",
                AllowMultiple = false
            });

            if (folders.Count == 0)
            {
                return null;
            }

            var folder = folders[0];
            string? bookmark = null;

            try
            {
                bookmark = await folder.SaveBookmarkAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] SaveBookmarkAsync failed: {ex.Message}");
            }

            // Fallback for platforms/folders where SaveBookmarkAsync returns null
            if (string.IsNullOrEmpty(bookmark))
            {
                bookmark = folder.TryGetLocalPath() ?? folder.Path.ToString();
            }

            if (!string.IsNullOrEmpty(bookmark))
            {
                var newBookmark = bookmark;
                await _settingsService.UpdateSettingsAsync(settings =>
                {
                    if (!settings.CloudFolderBookmarks.Contains(newBookmark))
                    {
                        settings.CloudFolderBookmarks.Add(newBookmark);
                    }
                });
            }

            return folder;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Failed to select folder: {ex.Message}");
            return null;
        }
    }

    public async Task<List<BookmarkedFolder>> GetBookmarkedFoldersAsync(IStorageProvider storageProvider)
    {
        var settings = _settingsService.LoadSettings();
        var folders = new List<BookmarkedFolder>();
        var revokedBookmarks = new List<string>();

        foreach (var bookmark in settings.CloudFolderBookmarks)
        {
            IStorageFolder? folder = null;
            var isRevoked = false;

            try
            {
                folder = await storageProvider.OpenFolderBookmarkAsync(bookmark);
            }
            catch (Exception ex)
            {
                isRevoked = IsAccessRevoked(ex);
                System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] OpenFolderBookmarkAsync failed for '{bookmark}': {ex.Message}");
            }

            // Fallback check if it represents a local folder path or URI
            if (folder is null && !isRevoked)
            {
                try
                {
                    if (Directory.Exists(bookmark))
                    {
                        folder = await storageProvider.TryGetFolderFromPathAsync(bookmark);
                    }
                    else if (Uri.TryCreate(bookmark, UriKind.Absolute, out var uri))
                    {
                        folder = await storageProvider.TryGetFolderFromPathAsync(uri);
                    }
                }
                catch (Exception ex)
                {
                    isRevoked = IsAccessRevoked(ex);
                    System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Fallback TryGetFolderFromPathAsync failed for '{bookmark}': {ex.Message}");
                }
            }

            // Opening a bookmark doesn't check the access on every platform (e.g. Android only fails when reading),
            // so read the first item to find out if we (still) have access.
            if (folder is not null && !isRevoked)
            {
                try
                {
                    await foreach (var item in folder.GetItemsAsync())
                    {
                        item.Dispose();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    isRevoked = IsAccessRevoked(ex);
                    System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Bookmarked folder '{bookmark}' can't be read: {ex.Message}");
                    folder = null;
                }
            }

            if (isRevoked)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Access to the bookmarked folder was revoked, it will be removed: {bookmark}");
                revokedBookmarks.Add(bookmark);
            }
            else if (folder is not null)
            {
                folders.Add(new BookmarkedFolder(bookmark, folder));
            }
            else
            {
                // Offline NAS, unplugged USB drive, provider not ready...: keep the bookmark and just skip it this time.
                // (Previously any failure removed the bookmark permanently.)
                System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Bookmarked folder is currently not accessible, skipping it: {bookmark}");
            }
        }

        if (revokedBookmarks.Count > 0)
        {
            await _settingsService.UpdateSettingsAsync(s => s.CloudFolderBookmarks.RemoveAll(b => revokedBookmarks.Contains(b)));
        }

        return folders;
    }

    /// <summary>
    /// Only a denied access on Android/iOS means the bookmark is definitively unusable: there the bookmark is a
    /// permission grant (persisted SAF tree permission / security scoped bookmark) which the user or OS revoked.
    /// On desktop a bookmark is just a path and an access problem can be temporary (e.g. a network share which
    /// isn't connected yet), so it's never removed automatically there.
    /// </summary>
    private static bool IsAccessRevoked(Exception exception)
    {
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            // Java.Lang.SecurityException on Android can't be referenced from here, hence the type name check
            if (current is UnauthorizedAccessException or System.Security.SecurityException ||
                current.GetType().Name == "SecurityException")
            {
                return true;
            }
        }

        return false;
    }

    public async Task RemoveBookmarkAsync(string bookmark)
    {
        if (!_settingsService.LoadSettings().CloudFolderBookmarks.Contains(bookmark))
        {
            return;
        }

        await _settingsService.UpdateSettingsAsync(settings => settings.CloudFolderBookmarks.Remove(bookmark));
    }

    public async IAsyncEnumerable<IStorageFile> EnumerateComicFilesAsync(IStorageFolder folder)
    {
        await foreach (var comicFile in EnumerateComicFilesRecursivelyAsync(folder, string.Empty))
        {
            yield return comicFile.File;
        }
    }

    public IAsyncEnumerable<ComicStorageFile> EnumerateComicFilesWithRelativePathAsync(IStorageFolder folder)
    {
        return EnumerateComicFilesRecursivelyAsync(folder, string.Empty);
    }

    private async IAsyncEnumerable<ComicStorageFile> EnumerateComicFilesRecursivelyAsync(IStorageFolder folder, string relativeFolderPath)
    {
        IReadOnlyList<IStorageItem>? items = null;

        try
        {
            var list = new List<IStorageItem>();
            await foreach (var item in folder.GetItemsAsync())
            {
                list.Add(item);
            }
            items = list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Failed to read contents of folder '{folder.Name}': {ex.Message}");
        }

        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            if (item is IStorageFile file)
            {
                if (ComicConstants.IsSupportedComicFile(file.Name))
                {
                    yield return new ComicStorageFile(file, relativeFolderPath + file.Name);
                }
            }
            else if (item is IStorageFolder subFolder)
            {
                if (!ComicConstants.IsIgnoredImportPath(subFolder.Name))
                {
                    await foreach (var subFile in EnumerateComicFilesRecursivelyAsync(subFolder, relativeFolderPath + subFolder.Name + "/"))
                    {
                        yield return subFile;
                    }
                }
            }
        }
    }

    public async Task<string?> CopyToLocalDirectoryAsync(IStorageFile file, string targetDirectory)
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(targetDirectory);

            var sanitizedName = LibraryService.SanitizeFileName(file.Name);

            // Copy to a temporary name first and move it into place when complete: a failed or interrupted copy
            // (connection lost, app killed) must never leave a truncated archive which the next scan would import.
            tempPath = Path.Combine(targetDirectory, $"{sanitizedName}.{Guid.NewGuid():N}{PartialFileExtension}");
            await using (var sourceStream = await file.OpenReadAsync())
            await using (var tempStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await sourceStream.CopyToAsync(tempStream);
                await tempStream.FlushAsync();
            }

            // Handle file collision by appending _1, _2 etc.
            var targetPath = Path.Combine(targetDirectory, sanitizedName);
            var counter = 1;
            var baseName = Path.GetFileNameWithoutExtension(sanitizedName);
            var extension = Path.GetExtension(sanitizedName);
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(targetDirectory, $"{baseName}_{counter}{extension}");
                counter++;
            }

            File.Move(tempPath, targetPath, overwrite: false);
            tempPath = null;

            return targetPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Failed to copy storage file '{file.Name}' locally: {ex.Message}");
            return null;
        }
        finally
        {
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Failed to delete temporary file '{tempPath}': {ex.Message}");
                }
            }
        }
    }
}
