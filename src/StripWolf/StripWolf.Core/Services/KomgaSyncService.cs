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
    private readonly KomgaApiServiceFactory _komgaApiServiceFactory;
    private readonly SettingsService _settingsService;
    private readonly DatabaseService _databaseService;
    private readonly SemaphoreSlim _syncAllSemaphore = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _comicSemaphores = new();

    /// <summary>
    /// A full sync sends one request per downloaded Komga comic. It is requested on every return to the app and by the
    /// library maintenance; within this interval after the last one those requests are skipped.
    /// </summary>
    internal static readonly TimeSpan SyncAllMinimumInterval = TimeSpan.FromMinutes(15);
    private DateTime _lastSyncAllUtc = DateTime.MinValue;
    private int _readerComicId;

    /// <summary>
    /// The comic which is open in the reader (null when the reader is closed). The reader syncs that comic itself;
    /// the full sync works on copies from the database and could push an older page of it while it is being read.
    /// </summary>
    public int? ReaderComicId
    {
        get
        {
            var id = Volatile.Read(ref _readerComicId);
            return id == 0 ? null : id;
        }
        set => Volatile.Write(ref _readerComicId, value ?? 0);
    }

    public KomgaSyncService(
        LibraryService libraryService,
        KomgaApiServiceFactory komgaApiServiceFactory,
        SettingsService settingsService,
        DatabaseService databaseService)
    {
        _libraryService = libraryService;
        _komgaApiServiceFactory = komgaApiServiceFactory;
        _settingsService = settingsService;
        _databaseService = databaseService;
    }

    private SemaphoreSlim GetComicSemaphore(int comicId)
    {
        return _comicSemaphores.GetOrAdd(comicId, _ => new SemaphoreSlim(1, 1));
    }

    private async Task QueueCurrentProgressAsync(Comic comic)
    {
        if (string.IsNullOrEmpty(comic.KomgaId))
        {
            return;
        }

        await _databaseService.SavePendingKomgaReadProgressAsync(
            comic.Id,
            comic.KomgaId,
            comic.KomgaServerId,
            comic.CurrentPage,
            comic.IsCompleted,
            AsUtc(comic.ReadProgressLastModified) ?? DateTime.UtcNow);

        comic.KomgaSyncState = KomgaSyncState.Pending;
    }

    /// <summary>
    /// Timestamps are written as UTC, but sqlite-net reads DateTime values back with DateTimeKind.Unspecified.
    /// ToUniversalTime() treats Unspecified as local time, which shifted the local timestamp by the UTC offset and
    /// broke the "which side is newer" comparison with Komga outside of UTC.
    /// </summary>
    private static DateTime? AsUtc(DateTime? value)
    {
        if (value is not { } dateTime)
        {
            return null;
        }

        return dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime();
    }

    private async Task<bool> TryPushPendingReadProgressAsync(Comic comic, KomgaServer? configuredServer = null, KomgaApiService? configuredApiService = null)
    {
        var pendingReadProgress = await _databaseService.GetPendingKomgaReadProgressAsync(comic.Id);
        if (pendingReadProgress is null || string.IsNullOrWhiteSpace(pendingReadProgress.BookId))
        {
            return true;
        }

        var server = configuredServer;
        if (server is null || pendingReadProgress.ServerId.HasValue && server.Id != pendingReadProgress.ServerId.Value)
        {
            var settings = _settingsService.LoadSettings();
            server = settings.Servers.FirstOrDefault(s => s.Id == (pendingReadProgress.ServerId ?? comic.KomgaServerId));
        }

        if (server is null)
        {
            comic.KomgaSyncState = KomgaSyncState.ServerNotFound;
            return false;
        }

        var komgaApiService = configuredApiService ?? _komgaApiServiceFactory.GetForServer(server);
        var pushed = await komgaApiService.UpdateReadProgressAsync(
            pendingReadProgress.BookId,
            pendingReadProgress.Page + 1,
            pendingReadProgress.IsCompleted);

        if (!pushed)
        {
            // Keep the pending progress so it is retried later. Before, the result was ignored: the pending entry
            // was deleted and "Synced to Komga" was shown even for a 401/403/500 response.
            comic.KomgaSyncState = KomgaSyncState.Failed;
            return false;
        }

        await _databaseService.DeletePendingKomgaReadProgressAsync(comic.Id);
        comic.KomgaSyncState = KomgaSyncState.Synced;
        return true;
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
                comic.KomgaSyncState = KomgaSyncState.ServerNotFound;
                return;
            }

            var komgaApiService = _komgaApiServiceFactory.GetForServer(server);

            if (!await TryPushPendingReadProgressAsync(comic, server, komgaApiService))
            {
                return;
            }

            var book = await komgaApiService.GetBookAsync(comic.KomgaId);
            if (book is null)
            {
                return;
            }

            if (book.ReadProgress is null)
            {
                // If local has progress, push to Komga
                if (comic.CurrentPage > 0 || comic.IsCompleted)
                {
                    await QueueCurrentProgressAsync(comic);
                    await TryPushPendingReadProgressAsync(comic, server, komgaApiService);
                }
                return;
            }

            var komgaProgress = book.ReadProgress;
            var komgaLastModified = komgaProgress.LastModified.ToUniversalTime();
            var localLastModified = AsUtc(comic.ReadProgressLastModified) ?? DateTime.MinValue;

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

                comic.KomgaSyncState = KomgaSyncState.UpdatedFromKomga;
            }
            else if (diff.TotalSeconds < -2)
            {
                // Local is newer, update Komga
                await QueueCurrentProgressAsync(comic);
                await TryPushPendingReadProgressAsync(comic, server, komgaApiService);
            }
            else
            {
                comic.KomgaSyncState = KomgaSyncState.InSync;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"Komga sync cancelled for comic '{comic.Title}' (Komga ID: {comic.KomgaId})");
        }
        catch (HttpRequestException ex)
        {
            comic.KomgaSyncState = KomgaSyncState.NetworkError;
            Logger.Error($"Komga sync network error for comic '{comic.Title}' (Komga ID: {comic.KomgaId})", ex);
        }
        catch (Exception ex)
        {
            comic.KomgaSyncState = KomgaSyncState.Failed;
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
    /// <param name="force">False: skipped when the last full sync was less than <see cref="SyncAllMinimumInterval"/> ago</param>
    public async Task SyncAllComicsAsync(bool force = false)
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
            if (!force && DateTime.UtcNow - _lastSyncAllUtc < SyncAllMinimumInterval)
            {
                return;
            }
            _lastSyncAllUtc = DateTime.UtcNow;

            var komgaComics = (await _libraryService.GetAllComicsAsync())
                .Where(c => c.Source == ComicSource.Komga && !string.IsNullOrEmpty(c.KomgaId) && c.KomgaServerId.HasValue)
                .GroupBy(c => c.KomgaServerId!.Value)
                .ToList();

            if (komgaComics.Count == 0) return;

            // Defer library changed notifications to avoid flickering while syncing many comics
            using var deferredScope = _libraryService.DeferLibraryChanged();

            foreach (var serverGroup in komgaComics)
            {
                var serverId = serverGroup.Key;
                var server = settings.Servers.FirstOrDefault(s => s.Id == serverId);

                if (server == null)
                {
                    foreach (var comic in serverGroup)
                    {
                        comic.KomgaSyncState = KomgaSyncState.ServerNotFound;
                    }
                    continue;
                }

                var komgaApiService = _komgaApiServiceFactory.GetForServer(server);

                foreach (var comic in serverGroup)
                {
                    if (comic.Id == ReaderComicId)
                    {
                        continue;
                    }
                    await SyncComicReadProgressInternalAsync(comic, server, komgaApiService);
                }
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
    /// Internal sync logic for a specific server API service.
    /// </summary>
    private async Task SyncComicReadProgressInternalAsync(Comic comic, KomgaServer server, KomgaApiService komgaApiService)
    {
        var semaphore = GetComicSemaphore(comic.Id);
        await semaphore.WaitAsync();

        try
        {
            // The comics were loaded when the full sync started, with many comics that can be minutes ago: compare the
            // current state, otherwise an older local page could be pushed (and then pulled back as the newest).
            var current = await _libraryService.GetComicAsync(comic.Id);
            if (current is null || current.Source != ComicSource.Komga || string.IsNullOrEmpty(current.KomgaId))
            {
                return;
            }
            comic = current;

            if (!await TryPushPendingReadProgressAsync(comic, server, komgaApiService))
            {
                return;
            }

            var book = await komgaApiService.GetBookAsync(comic.KomgaId!);
            if (book is null)
            {
                return;
            }

            if (book.ReadProgress is null)
            {
                if (comic.CurrentPage > 0 || comic.IsCompleted)
                {
                    await QueueCurrentProgressAsync(comic);
                    await TryPushPendingReadProgressAsync(comic, server, komgaApiService);
                }
                return;
            }

            var komgaProgress = book.ReadProgress;
            var komgaLastModified = komgaProgress.LastModified.ToUniversalTime();
            var localLastModified = AsUtc(comic.ReadProgressLastModified) ?? DateTime.MinValue;

            var diff = komgaLastModified - localLastModified;
            if (diff.TotalSeconds > 2)
            {
                var newPage = Math.Max(0, komgaProgress.Page - 1);
                var newCompleted = komgaProgress.Completed;

                await _libraryService.UpdateReadingProgressAsync(comic, newPage, komgaLastModified, newCompleted);

                comic.CurrentPage = newPage;
                comic.IsCompleted = newCompleted;
                comic.KomgaSyncState = KomgaSyncState.UpdatedFromKomga;
            }
            else if (diff.TotalSeconds < -2)
            {
                await QueueCurrentProgressAsync(comic);
                await TryPushPendingReadProgressAsync(comic, server, komgaApiService);
            }
            else
            {
                comic.KomgaSyncState = KomgaSyncState.InSync;
            }
        }
        catch (Exception ex)
        {
            comic.KomgaSyncState = KomgaSyncState.Failed;
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
            await QueueCurrentProgressAsync(comic);
            await TryPushPendingReadProgressAsync(comic, server);
        }
        catch (HttpRequestException ex)
        {
            comic.KomgaSyncState = KomgaSyncState.Pending;
            Logger.Error($"Push progress to Komga network error for comic '{comic.Title}' (Komga ID: {comic.KomgaId})", ex);
        }
        catch (Exception ex)
        {
            comic.KomgaSyncState = KomgaSyncState.Pending;
            Logger.Error($"Push progress to Komga failed for comic '{comic.Title}' (Komga ID: {comic.KomgaId})", ex);
        }
    }
}
