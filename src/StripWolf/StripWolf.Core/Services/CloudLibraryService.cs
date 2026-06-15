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
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace StripWolf.Core.Services;

/// <summary>
/// Service implementing ICloudLibraryService to handle cross-platform folder access
/// using Avalonia's StorageProvider and the Bookmark pattern.
/// </summary>
public class CloudLibraryService : ICloudLibraryService
{
    private readonly SettingsService _settingsService;

    public CloudLibraryService(SettingsService settingsService)
    {
        _settingsService = settingsService;
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
                var settings = _settingsService.LoadSettings();
                if (!settings.CloudFolderBookmarks.Contains(bookmark))
                {
                    settings.CloudFolderBookmarks.Add(bookmark);
                    await _settingsService.SaveSettingsAsync(settings);
                }
            }

            return folder;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Failed to select folder: {ex.Message}");
            return null;
        }
    }

    public async Task<List<IStorageFolder>> GetBookmarkedFoldersAsync(IStorageProvider storageProvider)
    {
        var settings = _settingsService.LoadSettings();
        var folders = new List<IStorageFolder>();
        var invalidBookmarks = new List<string>();

        foreach (var bookmark in settings.CloudFolderBookmarks)
        {
            IStorageFolder? folder = null;

            try
            {
                folder = await storageProvider.OpenFolderBookmarkAsync(bookmark);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] OpenFolderBookmarkAsync failed for '{bookmark}': {ex.Message}");
            }

            // Fallback check if it represents a local folder path or URI
            if (folder is null)
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
                    System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Fallback TryGetFolderFromPathAsync failed for '{bookmark}': {ex.Message}");
                }
            }

            if (folder is not null)
            {
                folders.Add(folder);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Bookmark is no longer accessible and will be cleaned up: {bookmark}");
                invalidBookmarks.Add(bookmark);
            }
        }

        if (invalidBookmarks.Count > 0)
        {
            settings.CloudFolderBookmarks.RemoveAll(b => invalidBookmarks.Contains(b));
            await _settingsService.SaveSettingsAsync(settings);
        }

        return folders;
    }

    public async Task RemoveBookmarkAsync(string bookmark)
    {
        var settings = _settingsService.LoadSettings();
        if (settings.CloudFolderBookmarks.Remove(bookmark))
        {
            await _settingsService.SaveSettingsAsync(settings);
        }
    }

    public async IAsyncEnumerable<IStorageFile> EnumerateComicFilesAsync(IStorageFolder folder)
    {
        await foreach (var file in EnumerateComicFilesRecursivelyAsync(folder))
        {
            yield return file;
        }
    }

    private async IAsyncEnumerable<IStorageFile> EnumerateComicFilesRecursivelyAsync(IStorageFolder folder)
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
                    yield return file;
                }
            }
            else if (item is IStorageFolder subFolder)
            {
                if (!ComicConstants.IsIgnoredImportPath(subFolder.Name))
                {
                    await foreach (var subFile in EnumerateComicFilesRecursivelyAsync(subFolder))
                    {
                        yield return subFile;
                    }
                }
            }
        }
    }

    public async Task<string?> CopyToLocalDirectoryAsync(IStorageFile file, string targetDirectory)
    {
        try
        {
            Directory.CreateDirectory(targetDirectory);

            var sanitizedName = LibraryService.SanitizeFileName(file.Name);
            var targetPath = Path.Combine(targetDirectory, sanitizedName);

            // Handle file collision by appending _1, _2 etc.
            var counter = 1;
            var baseName = Path.GetFileNameWithoutExtension(sanitizedName);
            var extension = Path.GetExtension(sanitizedName);
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(targetDirectory, $"{baseName}_{counter}{extension}");
                counter++;
            }

            await using var sourceStream = await file.OpenReadAsync();
            await using var targetStream = File.Create(targetPath);
            await sourceStream.CopyToAsync(targetStream);

            return targetPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudLibraryService] Failed to copy storage file '{file.Name}' locally: {ex.Message}");
            return null;
        }
    }
}
