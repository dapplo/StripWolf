using Kom2go.Models;
using SQLite;

namespace Kom2go.Data;

/// <summary>
/// SQLite database service for local storage
/// </summary>
public class DatabaseService : IAsyncDisposable
{
    private SQLiteAsyncConnection? _database;
    private readonly string _databasePath;

    public DatabaseService()
    {
        _databasePath = Path.Combine(FileSystem.AppDataDirectory, "kom2go.db");
    }

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (_database is not null)
        {
            return _database;
        }

        _database = new SQLiteAsyncConnection(_databasePath);
        
        await _database.CreateTableAsync<Comic>();
        await _database.CreateTableAsync<KomgaServer>();
        
        return _database;
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
        return await db.Table<Comic>()
            .OrderByDescending(c => c.LastReadDate ?? c.AddedDate)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<Comic>> GetInProgressComicsAsync()
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>()
            .Where(c => c.CurrentPage > 0 && !c.IsCompleted)
            .OrderByDescending(c => c.LastReadDate)
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

    public async Task<Comic?> GetComicByFilePathAsync(string filePath)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<Comic>().FirstOrDefaultAsync(c => c.FilePath == filePath);
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
