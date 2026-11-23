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
/// The disk cache of Komga series and book thumbnails: thumbnails/{serverId}/{type}/{id}.jpg in the app data directory.
/// Entries are refreshed after <see cref="MaxAge"/> (a cover changed on the server showed the old one forever),
/// written atomically (a write cut off by a kill left a truncated file which never decoded again),
/// and the cache is kept below <see cref="MaxCacheBytes"/>.
/// </summary>
public static class KomgaThumbnailCache
{
    /// <summary>
    /// After this the thumbnail is requested from the server again (the cached one is used when that fails)
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    private const long MaxCacheBytes = 200L * 1024 * 1024;
    private const long PruneTargetBytes = 150L * 1024 * 1024;

    private static int _pruneStarted;

    /// <summary>
    /// The root directory of the cache, the same location as before (LocalApplicationData/StripWolf/thumbnails)
    /// </summary>
    public static string RootDirectory => Path.Combine(AppPaths.DefaultAppDataDirectory, "thumbnails");

    /// <summary>
    /// Returns the path for a cached thumbnail. The path includes the server ID so that
    /// thumbnails from different servers do not overlap.
    /// The item id comes from the server, it is sanitized so a malicious server can't write outside the cache.
    /// </summary>
    public static string GetPath(int serverId, string type, string itemId)
    {
        var cacheDir = Path.Combine(RootDirectory, serverId.ToString(System.Globalization.CultureInfo.InvariantCulture), type);
        Directory.CreateDirectory(cacheDir);
        return Path.Combine(cacheDir, $"{LibraryService.SanitizeFileName(itemId)}.jpg");
    }

    /// <summary>
    /// Returns the thumbnail bytes from the disk cache, or fetches (and caches) them from the server when they are
    /// missing or older than <see cref="MaxAge"/>.
    /// </summary>
    /// <returns>The bytes, and the cache file they are stored in (null when they couldn't be cached)</returns>
    public static async Task<(byte[]? Bytes, string? CachePath)> GetOrFetchAsync(int serverId, string type, string itemId,
        Func<CancellationToken, Task<byte[]?>> fetch, CancellationToken ct)
    {
        var cachePath = GetPath(serverId, type, itemId);
        byte[]? cachedBytes = null;
        var cacheFile = new FileInfo(cachePath);
        if (cacheFile.Exists)
        {
            try
            {
                cachedBytes = await File.ReadAllBytesAsync(cachePath, ct);
            }
            catch (IOException)
            {
                // Being replaced by another load of the same thumbnail, fetch it
            }
            TouchLastAccess(cachePath);

            if (cachedBytes is { Length: > 0 } && DateTime.UtcNow - cacheFile.LastWriteTimeUtc < MaxAge)
            {
                return (cachedBytes, cachePath);
            }
        }

        byte[]? bytes;
        try
        {
            bytes = await fetch(ct);
        }
        catch (Exception) when (cachedBytes is { Length: > 0 } && !ct.IsCancellationRequested)
        {
            // Offline or a server error: an old thumbnail is better than none
            return (cachedBytes, cachePath);
        }

        if (bytes is not { Length: > 0 })
        {
            return (cachedBytes, cachedBytes is null ? null : cachePath);
        }

        if (!ct.IsCancellationRequested)
        {
            await WriteAtomicallyAsync(cachePath, bytes);
        }
        return (bytes, cachePath);
    }

    /// <summary>
    /// Used by Prune to find the entries which weren't needed for the longest time (many file systems don't update the
    /// access time themselves). Best effort.
    /// </summary>
    private static void TouchLastAccess(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Not supported or not allowed, pruning then falls back to the write time
        }
    }

    /// <summary>
    /// Removes a cache file which turned out to be unreadable, so it is fetched again next time
    /// </summary>
    public static void Invalidate(string? cachePath)
    {
        if (cachePath is null)
        {
            return;
        }

        try
        {
            File.Delete(cachePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KomgaThumbnailCache: Failed to delete '{cachePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the thumbnails of a server which was deleted
    /// </summary>
    public static void DeleteServer(int serverId)
    {
        try
        {
            var directory = Path.Combine(RootDirectory, serverId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KomgaThumbnailCache: Failed to delete the thumbnails of server {serverId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Keeps the cache below its maximum size, the thumbnails which weren't used for the longest time are removed first.
    /// Runs once per app session, in the background.
    /// </summary>
    public static void PruneInBackground()
    {
        if (Interlocked.Exchange(ref _pruneStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Not while the app is starting
                await Task.Delay(TimeSpan.FromSeconds(30));
                Prune(RootDirectory, MaxCacheBytes, PruneTargetBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"KomgaThumbnailCache: Pruning failed: {ex.Message}");
            }
        });
    }

    internal static void Prune(string rootDirectory, long maxBytes, long targetBytes)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        var files = new DirectoryInfo(rootDirectory).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        var totalBytes = files.Sum(file => file.Length);
        if (totalBytes <= maxBytes)
        {
            return;
        }

        foreach (var file in files.OrderBy(file => file.LastAccessTimeUtc > file.LastWriteTimeUtc ? file.LastAccessTimeUtc : file.LastWriteTimeUtc))
        {
            if (totalBytes <= targetBytes)
            {
                break;
            }

            try
            {
                var length = file.Length;
                file.Delete();
                totalBytes -= length;
            }
            catch (IOException)
            {
                // In use, skip it
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes)
    {
        // A unique temporary name: the same thumbnail can be loaded twice at the same time (two home sections)
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another load of the same thumbnail replaced it at the same moment, or the file is being read
            System.Diagnostics.Debug.WriteLine($"KomgaThumbnailCache: Failed to store '{path}': {ex.Message}");
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Best effort
            }
        }
    }
}
