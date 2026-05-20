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

    /// <summary>
    /// Synchronizes the reading progress of a single comic with Komga.
    /// </summary>
    public async Task SyncComicReadProgressAsync(Comic comic)
    {
        if (comic.Source != ComicSource.Komga || string.IsNullOrEmpty(comic.KomgaId))
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        if (!settings.SyncReadProgress || !_komgaApiService.IsConfigured)
        {
            return;
        }

        try
        {
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
            if (diff.TotalSeconds > 1)
            {
                // Komga is newer, update local
                comic.CurrentPage = Math.Max(0, komgaProgress.Page - 1);
                comic.IsCompleted = komgaProgress.Completed;
                
                await _databaseService.UpdateReadingProgressAsync(comic.Id, comic.CurrentPage, comic.IsCompleted);
                // Update it again to match Komga exactly so next sync knows they are same
                var updatedComic = await _databaseService.GetComicAsync(comic.Id);
                if (updatedComic != null)
                {
                    updatedComic.ReadProgressLastModified = komgaLastModified;
                    await _databaseService.SaveComicAsync(updatedComic);
                }
                
                comic.KomgaSyncStatus = "Updated from Komga";
            }
            else if (diff.TotalSeconds < -1)
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
        catch
        {
            comic.KomgaSyncStatus = "Sync failed";
        }
    }

    /// <summary>
    /// Synchronizes reading progress for all comics in the library that come from Komga.
    /// </summary>
    public async Task SyncAllComicsAsync()
    {
        var settings = _settingsService.LoadSettings();
        if (!settings.SyncReadProgress || !_komgaApiService.IsConfigured)
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
                .Where(c => c.Source == ComicSource.Komga && !string.IsNullOrEmpty(c.KomgaId))
                .ToList();

            // To be efficient, we could use Komga's pagination or "latest" endpoints,
            // but for now, we'll just do them sequentially or in small batches.
            foreach (var comic in komgaComics)
            {
                await SyncComicReadProgressAsync(comic);
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
    /// Pushes the current reading progress to Komga without full sync.
    /// Useful for periodic updates during reading.
    /// </summary>
    public async Task PushProgressToKomgaAsync(Comic comic)
    {
        if (comic.Source != ComicSource.Komga || string.IsNullOrEmpty(comic.KomgaId))
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        if (!settings.SyncReadProgress || !_komgaApiService.IsConfigured)
        {
            return;
        }

        try
        {
            await _komgaApiService.UpdateReadProgressAsync(comic.KomgaId, comic.CurrentPage + 1, comic.IsCompleted);
            comic.KomgaSyncStatus = "Synced to Komga";
        }
        catch
        {
            comic.KomgaSyncStatus = "Sync failed";
        }
    }
}
