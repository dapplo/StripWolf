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
using StripWolf.Core.Services;
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

    public DatabaseService() : this(Path.Combine(AppPaths.DefaultAppDataDirectory, "StripWolf.db"))
    {
    }

    /// <summary>
    /// Use a specific database file (used by the tests)
    /// </summary>
    internal DatabaseService(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        _databasePath = databasePath;
    }

    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(Comic))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(EpubConversionState))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(KomgaPendingDownload))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(KomgaPendingReadProgress))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(UsageCounter))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(BookmarkedFileImport))]
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
            // Komga servers are stored in settings.json (credentials encrypted separately). The old, unused table
            // still contained the passwords and API keys in plain text, remove it.
            await _database.ExecuteAsync("DROP TABLE IF EXISTS KomgaServer");
            await _database.CreateTableAsync<KomgaPendingDownload>();
            await _database.CreateTableAsync<KomgaPendingReadProgress>();
            await _database.CreateTableAsync<UsageCounter>();
            await MigrateUsageStatsAsync(_database);
            await _database.CreateTableAsync<BookmarkedFileImport>();
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
        // Sort in memory since SQLite-net doesn't support null-coalescing operator in OrderBy expressions
        var comics = await db.Table<Comic>()
            .Where(c => !c.IsCompleted && (c.CurrentPage > 0 || c.LastReadDate != null))
            .ToListAsync();
        return comics.OrderByDescending(c => c.LastReadDate).ToList();
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
            .OrderByDescending(c => c.AddedDate)
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

    public async Task UpdateReadingProgressAsync(int comicId, int currentPage, bool isCompleted, DateTime? lastModified = null)
    {
        var comic = await GetComicAsync(comicId);
        if (comic is not null)
        {
            if (comic.CurrentPage != currentPage || comic.IsCompleted != isCompleted)
            {
                comic.CurrentPage = currentPage;
                comic.IsCompleted = isCompleted;
                comic.ReadProgressLastModified = lastModified ?? DateTime.UtcNow;
            }

            // Only update LastReadDate if this is a real read action (not a sync from an older state)
            if (lastModified == null || (comic.LastReadDate ?? DateTime.MinValue) < lastModified)
            {
                comic.LastReadDate = lastModified ?? DateTime.UtcNow;
            }

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
            comic.ReadProgressLastModified = DateTime.UtcNow;
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

    #region Komga Read Progress Queue

    public async Task<KomgaPendingReadProgress?> GetPendingKomgaReadProgressAsync(int comicId)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<KomgaPendingReadProgress>()
            .FirstOrDefaultAsync(pendingReadProgress => pendingReadProgress.ComicId == comicId);
    }

    public async Task<int> SavePendingKomgaReadProgressAsync(int comicId, string bookId, int? serverId, int page, bool isCompleted, DateTime readProgressLastModifiedUtc)
    {
        if (comicId <= 0 || string.IsNullOrWhiteSpace(bookId))
        {
            return 0;
        }

        var db = await GetDatabaseAsync();
        return await db.InsertOrReplaceAsync(new KomgaPendingReadProgress
        {
            ComicId = comicId,
            BookId = bookId,
            ServerId = serverId,
            Page = page,
            IsCompleted = isCompleted,
            ReadProgressLastModifiedUtc = readProgressLastModifiedUtc.ToUniversalTime(),
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    public async Task<int> DeletePendingKomgaReadProgressAsync(int comicId)
    {
        if (comicId <= 0)
        {
            return 0;
        }

        var db = await GetDatabaseAsync();
        return await db.DeleteAsync<KomgaPendingReadProgress>(comicId);
    }

    #endregion

    #region Komga Download Queue

    public async Task<List<KomgaPendingDownload>> GetPendingKomgaDownloadsAsync()
    {
        var db = await GetDatabaseAsync();
        var pendingDownloads = await db.Table<KomgaPendingDownload>()
            .Where(pendingDownload => pendingDownload.BookId != null && pendingDownload.BookId != string.Empty)
            .ToListAsync();

        return pendingDownloads
            .Where(pendingDownload => !string.IsNullOrWhiteSpace(pendingDownload.BookId))
            .ToList();
    }

    public async Task<List<string>> GetPendingKomgaDownloadBookIdsAsync()
    {
        var pendingDownloads = await GetPendingKomgaDownloadsAsync();
        return pendingDownloads
            .Select(pendingDownload => pendingDownload.BookId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<int> SavePendingKomgaDownloadAsync(string bookId, int? serverId = null)
    {
        if (string.IsNullOrWhiteSpace(bookId))
        {
            return 0;
        }

        var db = await GetDatabaseAsync();
        return await db.InsertOrReplaceAsync(new KomgaPendingDownload
        {
            BookId = bookId,
            ServerId = serverId,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    public async Task<int> DeletePendingKomgaDownloadAsync(string bookId)
    {
        if (string.IsNullOrWhiteSpace(bookId))
        {
            return 0;
        }

        var db = await GetDatabaseAsync();
        return await db.DeleteAsync<KomgaPendingDownload>(bookId);
    }

    public async Task ReplacePendingKomgaDownloadsAsync(IEnumerable<KomgaPendingDownload> pendingDownloads)
    {
        var normalizedPendingDownloads = pendingDownloads
            .Where(pendingDownload => !string.IsNullOrWhiteSpace(pendingDownload.BookId))
            .GroupBy(pendingDownload => pendingDownload.BookId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        var db = await GetDatabaseAsync();
        await db.RunInTransactionAsync(connection =>
        {
            connection.DeleteAll<KomgaPendingDownload>();
            var now = DateTime.UtcNow;
            foreach (var pendingDownload in normalizedPendingDownloads)
            {
                connection.Insert(new KomgaPendingDownload
                {
                    BookId = pendingDownload.BookId,
                    ServerId = pendingDownload.ServerId,
                    UpdatedAtUtc = now
                });
            }
        });
    }

    #endregion

    #region Usage Stats

    /// <summary>
    /// The old UsageStats table had a row per event (including every page turn) and grew forever,
    /// only the number of rows per metric was used. Fold it into the UsageCounter table once and drop it.
    /// </summary>
    private static async Task MigrateUsageStatsAsync(SQLiteAsyncConnection database)
    {
        try
        {
            var oldTableCount = await database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'UsageStats'");
            if (oldTableCount == 0)
            {
                return;
            }

            // INSERT OR IGNORE: if the app is killed between these statements, a rerun can't count twice
            await database.ExecuteAsync(
                $"INSERT OR IGNORE INTO {UsageCounter.TableName} (Metric, Amount) " +
                "SELECT Metric, COUNT(*) FROM UsageStats WHERE Metric IS NOT NULL GROUP BY Metric");
            await database.ExecuteAsync("DROP TABLE UsageStats");
        }
        catch (Exception ex)
        {
            // Statistics are not important enough to block the database initialization
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Failed to migrate usage stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Add amount to the counter of the specified metric
    /// </summary>
    public async Task IncrementUsageAsync(string metric, long amount = 1)
    {
        if (amount <= 0)
        {
            return;
        }

        try
        {
            var db = await GetDatabaseAsync();
            await db.ExecuteAsync($"INSERT OR IGNORE INTO {UsageCounter.TableName} (Metric, Amount) VALUES (?, 0)", metric);
            await db.ExecuteAsync($"UPDATE {UsageCounter.TableName} SET Amount = Amount + ? WHERE Metric = ?", amount, metric);
        }
        catch
        {
            // Ignore stats errors so they don't crash the app
        }
    }

    public async Task<int> GetUsageCountAsync(string metric)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var amount = await db.ExecuteScalarAsync<long>($"SELECT Amount FROM {UsageCounter.TableName} WHERE Metric = ?", metric);
            return (int)Math.Min(amount, int.MaxValue);
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region Bookmarked folder imports

    /// <summary>
    /// Relative paths of all source files of the bookmarked folder which were imported before
    /// </summary>
    public async Task<HashSet<string>> GetImportedBookmarkedFilesAsync(string bookmarkKey)
    {
        var db = await GetDatabaseAsync();
        var imports = await db.Table<BookmarkedFileImport>().Where(i => i.BookmarkKey == bookmarkKey).ToListAsync();
        return imports.Select(i => i.RelativePath).ToHashSet(StringComparer.Ordinal);
    }

    public async Task MarkBookmarkedFileImportedAsync(string bookmarkKey, string relativePath, long? size, DateTimeOffset? modified)
    {
        var db = await GetDatabaseAsync();
        await db.InsertOrReplaceAsync(new BookmarkedFileImport
        {
            Key = BookmarkedFileImport.CreateKey(bookmarkKey, relativePath),
            BookmarkKey = bookmarkKey,
            RelativePath = relativePath,
            Size = size,
            ModifiedUtcTicks = modified?.UtcTicks,
            ImportedUtcTicks = DateTime.UtcNow.Ticks
        });
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
