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

using StripWolf.Core.Data;
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

/// <summary>
/// Service for synchronizing reading progress with Komga
/// </summary>
public class KomgaSyncService
{
    private readonly LibraryService _libraryService;
    private readonly KomgaApiService _komgaApiService;
    private readonly SettingsService _settingsService;
    private readonly DatabaseService _databaseService;
    private readonly SemaphoreSlim _syncAllSemaphore = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _comicSemaphores = new();

    public KomgaSyncService(
        LibraryService libraryService,
        KomgaApiService komgaApiService,
        SettingsService settingsService,
        DatabaseService databaseService)
    {
        _libraryService = libraryService;
        _komgaApiService = komgaApiService;
        _settingsService = settingsService;
        _databaseService = databaseService;
    }

    private SemaphoreSlim GetComicSemaphore(int comicId)
    {
        return _comicSemaphores.GetOrAdd(comicId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Synchronizes the reading progress of a single comic with Komga.
    /// </summary>
    public async Task SyncComicReadProgressAsync(Comic comic)
    {
        if (comic.Source != ComicSource.Komga || string.IsNullOrEmpty(comic.KomgaId) || !comic.KomgaServerId.HasValue)
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        if (!settings.SyncReadProgress)
        {
            return;
        }

        var semaphore = GetComicSemaphore(comic.Id);
        await semaphore.WaitAsync();

        try
        {
            // Find the specific server for this comic
            var server = settings.Servers.FirstOrDefault(s => s.Id == comic.KomgaServerId.Value);
            if (server == null)
            {
                comic.KomgaSyncStatus = "Server not found";
                return;
            }

            // Configure API service for this specific server
            _komgaApiService.Configure(server);

            var book = await _komgaApiService.GetBookAsync(comic.KomgaId);
            if (book is null)
            {
                return;
            }

            if (book.ReadProgress is null)
            {
                // If local has progress, push to Komga
                if (comic.CurrentPage > 0 || comic.IsCompleted)
                {
                    await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId, comic.CurrentPage + 1, comic.IsCompleted);
                    comic.KomgaSyncStatus = "Synced to Komga";
                }
                return;
            }

            var komgaProgress = book.ReadProgress;
            var komgaLastModified = komgaProgress.LastModified.ToUniversalTime();
            var localLastModified = comic.ReadProgressLastModified?.ToUniversalTime() ?? DateTime.MinValue;

            // Use a small epsilon for comparison to avoid issues with precision or clock skew
            var diff = komgaLastModified - localLastModified;
            if (diff.TotalSeconds > 2)
            {
                // Komga is newer, update local
                var newPage = Math.Max(0, komgaProgress.Page - 1);
                var newCompleted = komgaProgress.Completed;
                
                // Update local database and memory object via LibraryService
                await _libraryService.UpdateReadingProgressAsync(comic, newPage, komgaLastModified, newCompleted);
                
                // Ensure UI properties are updated if this instance is bound
                comic.CurrentPage = newPage;
                comic.IsCompleted = newCompleted;
                
                comic.KomgaSyncStatus = "Updated from Komga";
            }
            else if (diff.TotalSeconds < -2)
            {
                // Local is newer, update Komga
                await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId, comic.CurrentPage + 1, comic.IsCompleted);
                comic.KomgaSyncStatus = "Synced to Komga";
            }
            else
            {
                comic.KomgaSyncStatus = "In sync";
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"Komga sync cancelled for comic '{comic.Title}' (Komga ID: {comic.KomgaId})");
        }
        catch (HttpRequestException ex)
        {
            comic.KomgaSyncStatus = "Network error";
            Logger.Error($"Komga sync network error for comic '{comic.Title}' (Komga ID: {comic.KomgaId})", ex);
        }
        catch (Exception ex)
        {
            comic.KomgaSyncStatus = "Sync failed";
            Logger.Error($"Komga sync failed for comic '{comic.Title}' (Komga ID: {comic.KomgaId})", ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Synchronizes reading progress for all comics in the library that come from Komga.
    /// </summary>
    public async Task SyncAllComicsAsync()
    {
        var settings = _settingsService.LoadSettings();
        if (!settings.SyncReadProgress)
        {
            return;
        }

        if (!await _syncAllSemaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            var komgaComics = (await _libraryService.GetAllComicsAsync())
                .Where(c => c.Source == ComicSource.Komga && !string.IsNullOrEmpty(c.KomgaId) && c.KomgaServerId.HasValue)
                .GroupBy(c => c.KomgaServerId!.Value)
                .ToList();

            if (komgaComics.Count == 0) return;

            // Defer library changed notifications to avoid flickering while syncing many comics
            using var deferredScope = _libraryService.DeferLibraryChanged();

            // Store original API configuration (browsing server)
            var browsingServer = settings.Servers.FirstOrDefault(s => s.Id == settings.ActiveServerId);

            foreach (var serverGroup in komgaComics)
            {
                var serverId = serverGroup.Key;
                var server = settings.Servers.FirstOrDefault(s => s.Id == serverId);
                
                if (server == null)
                {
                    foreach (var comic in serverGroup)
                    {
                        comic.KomgaSyncStatus = "Server not found";
                    }
                    continue;
                }

                // Configure API service once per server
                _komgaApiService.Configure(server);

                foreach (var comic in serverGroup)
                {
                    await SyncComicReadProgressInternalAsync(comic);
                }
            }

            // Restore browsing server configuration
            if (browsingServer != null)
            {
                _komgaApiService.Configure(browsingServer);
            }
        }
        catch
        {
            // Silently fail global sync
        }
        finally
        {
            _syncAllSemaphore.Release();
        }
    }

    /// <summary>
    /// Internal sync logic that assumes _komgaApiService is already configured for the correct server.
    /// </summary>
    private async Task SyncComicReadProgressInternalAsync(Comic comic)
    {
        var semaphore = GetComicSemaphore(comic.Id);
        await semaphore.WaitAsync();

        try
        {
            var book = await _komgaApiService.GetBookAsync(comic.KomgaId!);
            if (book is null)
            {
                return;
            }

            if (book.ReadProgress is null)
            {
                if (comic.CurrentPage > 0 || comic.IsCompleted)
                {
                    await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId!, comic.CurrentPage + 1, comic.IsCompleted);
                    comic.KomgaSyncStatus = "Synced to Komga";
                }
                return;
            }

            var komgaProgress = book.ReadProgress;
            var komgaLastModified = komgaProgress.LastModified.ToUniversalTime();
            var localLastModified = comic.ReadProgressLastModified?.ToUniversalTime() ?? DateTime.MinValue;

            var diff = komgaLastModified - localLastModified;
            if (diff.TotalSeconds > 2)
            {
                var newPage = Math.Max(0, komgaProgress.Page - 1);
                var newCompleted = komgaProgress.Completed;
                
                await _libraryService.UpdateReadingProgressAsync(comic, newPage, komgaLastModified, newCompleted);
                
                comic.CurrentPage = newPage;
                comic.IsCompleted = newCompleted;
                comic.KomgaSyncStatus = "Updated from Komga";
            }
            else if (diff.TotalSeconds < -2)
            {
                await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId!, comic.CurrentPage + 1, comic.IsCompleted);
                comic.KomgaSyncStatus = "Synced to Komga";
            }
            else
            {
                comic.KomgaSyncStatus = "In sync";
            }
        }
        catch (Exception ex)
        {
            comic.KomgaSyncStatus = "Sync failed";
            Logger.Error($"Komga internal sync failed for comic '{comic.Title}' (Komga ID: {comic.KomgaId})", ex);
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    /// <summary>
    /// Pushes the current reading progress to Komga without full sync.
    /// Useful for periodic updates during reading.
    /// </summary>
    public async Task PushProgressToKomgaAsync(Comic comic)
    {
        if (comic.Source != ComicSource.Komga || string.IsNullOrEmpty(comic.KomgaId) || !comic.KomgaServerId.HasValue)
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        if (!settings.SyncReadProgress)
        {
            return;
        }

        // Find the specific server for this comic
        var server = settings.Servers.FirstOrDefault(s => s.Id == comic.KomgaServerId.Value);
        if (server == null)
        {
            return;
        }

        try
        {
            // Configure API service for this specific server
            _komgaApiService.Configure(server);

            await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId, comic.CurrentPage + 1, comic.IsCompleted);
            comic.KomgaSyncStatus = "Synced to Komga";
        }
        catch (Exception ex)
        {
            comic.KomgaSyncStatus = "Sync failed";
            Logger.Error($"Push progress to Komga failed for comic '{comic.Title}' (Komga ID: {comic.KomgaId})", ex);
        }
    }
}
