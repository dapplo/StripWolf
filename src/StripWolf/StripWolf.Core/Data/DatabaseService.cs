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

using StripWolf.Core.Models;
using SQLite;
using System.Diagnostics.CodeAnalysis;

namespace StripWolf.Core.Data;

/// <summary>
/// SQLite database service for local storage
/// </summary>
public class DatabaseService : IAsyncDisposable
{
    private SQLiteAsyncConnection? _database;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized;

    public DatabaseService()
    {
        var appDataDir = GetAppDataDirectory();
        Directory.CreateDirectory(appDataDir);
        _databasePath = Path.Combine(appDataDir, "StripWolf.db");
    }

    private static string GetAppDataDirectory()
    {
        // Cross-platform app data directory
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "StripWolf");
    }

    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(Comic))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(EpubConversionState))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(KomgaServer))]
    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (_isInitialized && _database is not null)
        {
            return _database;
        }

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (_isInitialized && _database is not null)
            {
                return _database;
            }

            _database = new SQLiteAsyncConnection(_databasePath);
            
            // Enable WAL mode for better concurrency and faster writes
            // Also helps with "database is locked" and recovery after kills
            await _database.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL");
            await _database.ExecuteScalarAsync<string>("PRAGMA synchronous=NORMAL");
            
            await _database.CreateTableAsync<Comic>();
            await _database.CreateTableAsync<EpubConversionState>();
            await _database.CreateTableAsync<KomgaServer>();
            await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_Comic_FilePath ON Comic(FilePath)");
            await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_EpubConversionState_Status ON EpubConversionState(Status)");
            
            _isInitialized = true;
            return _database;
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    #region Comics

    public async Task<List<Comic>> GetComicsAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>().ToListAsync();
    }

    public async Task<List<Comic>> GetRecentComicsAsync(int count = 10)
    {
        var db = await GetDatabaseAsync();
        // Fetch all comics and sort in memory since SQLite-net doesn't support 
        // null-coalescing operator in OrderBy expressions
        var allComics = await db.Table<Comic>().ToListAsync();
        return allComics
            .OrderByDescending(c => c.LastReadDate ?? c.AddedDate)
            .Take(count)
            .ToList();
    }

    public async Task<List<Comic>> GetInProgressComicsAsync()
    {
        var db = await GetDatabaseAsync();
        // Include comics that have been read (LastReadDate is set) and are not completed
        // For Komga comics, also include those with CurrentPage > 0 OR that have been opened
        return await db.Table<Comic>()
            .Where(c => !c.IsCompleted && (c.CurrentPage > 0 || c.LastReadDate != null))
            .ToListAsync();
    }

    public async Task<List<Comic>> GetCompletedComicsAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>()
            .Where(c => c.IsCompleted)
            .ToListAsync();
    }

    public async Task<List<Comic>> GetNewComicsAsync()
    {
        var db = await GetDatabaseAsync();
        // New comics are those that haven't been started (no reading progress and not completed)
        return await db.Table<Comic>()
            .Where(c => !c.IsCompleted && c.CurrentPage == 0 && c.LastReadDate == null)
            .ToListAsync();
    }

    public async Task<Comic?> GetComicAsync(int id)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>().FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Comic?> GetComicByKomgaIdAsync(string komgaId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>().FirstOrDefaultAsync(c => c.KomgaId == komgaId);
    }

    public async Task<Comic?> GetComicByKomgaHashAsync(string fileHash)
    {
        if (string.IsNullOrEmpty(fileHash)) return null;
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>().FirstOrDefaultAsync(c => c.KomgaHash == fileHash);
    }

    public async Task<Comic?> GetComicByKomgaIdOrHashAsync(string komgaId, string? fileHash)
    {
        var db = await GetDatabaseAsync();
        var comic = await db.Table<Comic>().FirstOrDefaultAsync(c => c.KomgaId == komgaId);
        
        if (comic is null && !string.IsNullOrEmpty(fileHash))
        {
            comic = await db.Table<Comic>().FirstOrDefaultAsync(c => c.KomgaHash == fileHash);
        }
        
        return comic;
    }

    public async Task<List<Comic>> GetComicsByKomgaServerIdAsync(int serverId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>().Where(c => c.KomgaServerId == serverId).ToListAsync();
    }

    public async Task<Comic?> GetComicByFilePathAsync(string filePath)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>().FirstOrDefaultAsync(c => c.FilePath == filePath);
    }

    public async Task<List<Comic>> SearchComicsAsync(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return [];
        }
        
        var db = await GetDatabaseAsync();
        var allComics = await db.Table<Comic>().ToListAsync();
        var lowerSearch = searchText.ToLowerInvariant();
        
        return allComics
            .Where(c => (c.Title?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (c.SeriesName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (c.Authors?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    public async Task<int> SaveComicAsync(Comic comic)
    {
        var db = await GetDatabaseAsync();
        if (comic.Id != 0)
        {
            return await db.UpdateAsync(comic);
        }
        else
        {
            return await db.InsertAsync(comic);
        }
    }

    public async Task<int> DeleteComicAsync(Comic comic)
    {
        var db = await GetDatabaseAsync();
        return await db.DeleteAsync(comic);
    }

    public async Task<EpubConversionState?> GetEpubConversionStateAsync(int comicId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<EpubConversionState>().FirstOrDefaultAsync(state => state.ComicId == comicId);
    }

    public async Task<int> SaveEpubConversionStateAsync(EpubConversionState state)
    {
        var db = await GetDatabaseAsync();
        return await db.InsertOrReplaceAsync(state);
    }

    public async Task<int> DeleteEpubConversionStateAsync(int comicId)
    {
        var db = await GetDatabaseAsync();
        return await db.DeleteAsync<EpubConversionState>(comicId);
    }

    public async Task<List<EpubConversionState>> GetIncompleteEpubConversionStatesAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<EpubConversionState>()
            .Where(state => state.Status != EpubConversionStatus.Completed)
            .ToListAsync();
    }

    public async Task UpdateReadingProgressAsync(int comicId, int currentPage, bool isCompleted)
    {
        var comic = await GetComicAsync(comicId);
        if (comic is not null)
        {
            comic.CurrentPage = currentPage;
            comic.IsCompleted = isCompleted;
            comic.LastReadDate = DateTime.UtcNow;
            await SaveComicAsync(comic);
        }
    }

    public async Task ToggleReadStatusAsync(int comicId)
    {
        var comic = await GetComicAsync(comicId);
        if (comic is not null)
        {
            comic.IsCompleted = !comic.IsCompleted;
            // If marking as not read, also reset progress
            if (!comic.IsCompleted)
            {
                comic.CurrentPage = 0;
                comic.LastReadDate = null;
            }
            await SaveComicAsync(comic);
        }
    }

    public async Task<List<Comic>> GetFavoriteComicsAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>()
            .Where(c => c.IsFavorite)
            .ToListAsync();
    }

    public async Task ToggleFavoriteAsync(int comicId)
    {
        var comic = await GetComicAsync(comicId);
        if (comic is not null)
        {
            comic.IsFavorite = !comic.IsFavorite;
            await SaveComicAsync(comic);
        }
    }

    #endregion

    #region Komga Servers

    public async Task<List<KomgaServer>> GetServersAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<KomgaServer>().ToListAsync();
    }

    public async Task<KomgaServer?> GetActiveServerAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<KomgaServer>().FirstOrDefaultAsync(s => s.IsActive);
    }

    public async Task<KomgaServer?> GetServerAsync(int id)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<KomgaServer>().FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<int> SaveServerAsync(KomgaServer server)
    {
        var db = await GetDatabaseAsync();
        
        // If this server is being set as active, deactivate all others
        if (server.IsActive)
        {
            var allServers = await GetServersAsync();
            foreach (var s in allServers.Where(s => s.Id != server.Id && s.IsActive))
            {
                s.IsActive = false;
                await db.UpdateAsync(s);
            }
        }
        
        if (server.Id != 0)
        {
            return await db.UpdateAsync(server);
        }
        else
        {
            return await db.InsertAsync(server);
        }
    }

    public async Task<int> DeleteServerAsync(KomgaServer server)
    {
        var db = await GetDatabaseAsync();
        return await db.DeleteAsync(server);
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.CloseAsync();
            _database = null;
        }
    }
}

