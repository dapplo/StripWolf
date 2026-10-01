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
using Xunit;

namespace StripWolf.Core.Tests;

public sealed class DatabaseServiceTests : IAsyncLifetime
{
    private readonly TempDirectory _temp = new("database");
    private DatabaseService _database = null!;

    private string DatabasePath => _temp.Combine("data", "StripWolf.db");

    public ValueTask InitializeAsync()
    {
        _database = new DatabaseService(DatabasePath);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        _temp.Dispose();
    }

    private static Comic NewComic(string title, Action<Comic>? configure = null)
    {
        var comic = new Comic
        {
            Title = title,
            FilePath = $"/comics/{title}.cbz",
            PageCount = 20,
            Format = ComicFormat.Cbz,
            Source = ComicSource.Local
        };
        configure?.Invoke(comic);
        return comic;
    }

    private async Task<Comic> InsertAsync(string title, Action<Comic>? configure = null)
    {
        var comic = NewComic(title, configure);
        await _database.SaveComicAsync(comic);
        return comic;
    }

    [Fact]
    public async Task SaveComicAsync_InsertsAndAssignsAnId_ThenUpdates()
    {
        var comic = NewComic("First", c =>
        {
            c.SeriesName = "Series";
            c.Number = 1.5f;
            c.Authors = "Alice";
            c.ReleaseDate = new DateTime(2024, 5, 17);
            c.FileSize = 12345;
        });

        Assert.Equal(1, await _database.SaveComicAsync(comic));
        Assert.True(comic.Id > 0);

        comic.Title = "Renamed";
        Assert.Equal(1, await _database.SaveComicAsync(comic));

        var stored = await _database.GetComicAsync(comic.Id);
        Assert.NotNull(stored);
        Assert.Equal("Renamed", stored.Title);
        Assert.Equal("Series", stored.SeriesName);
        Assert.Equal(1.5f, stored.Number!.Value);
        Assert.Equal("Alice", stored.Authors);
        Assert.Equal(new DateTime(2024, 5, 17), stored.ReleaseDate!.Value);
        Assert.Equal(12345, stored.FileSize);
        Assert.Equal(ComicFormat.Cbz, stored.Format);
        Assert.Single(await _database.GetComicsAsync());
    }

    [Fact]
    public async Task DeleteComicAsync_RemovesTheComic()
    {
        var keep = await InsertAsync("Keep");
        var delete = await InsertAsync("Delete");

        Assert.Equal(1, await _database.DeleteComicAsync(delete));

        Assert.Null(await _database.GetComicAsync(delete.Id));
        Assert.Equal(keep.Id, Assert.Single(await _database.GetComicsAsync()).Id);
    }

    [Fact]
    public async Task Lookups_ByFilePathKomgaIdAndHash()
    {
        var local = await InsertAsync("Local");
        var komga = await InsertAsync("Komga", c =>
        {
            c.Source = ComicSource.Komga;
            c.KomgaId = "0BOOK1";
            c.KomgaHash = "hash-1";
            c.KomgaServerId = 3;
        });

        Assert.Equal(local.Id, (await _database.GetComicByFilePathAsync(local.FilePath))?.Id);
        Assert.Null(await _database.GetComicByFilePathAsync("/comics/missing.cbz"));
        Assert.Equal(komga.Id, (await _database.GetComicByKomgaIdAsync("0BOOK1"))?.Id);
        Assert.Equal(komga.Id, (await _database.GetComicByKomgaHashAsync("hash-1"))?.Id);
        Assert.Null(await _database.GetComicByKomgaHashAsync(string.Empty));
        Assert.Equal(komga.Id, (await _database.GetComicByKomgaIdOrHashAsync("0BOOK1", null))?.Id);
        // Falls back to the hash when the id is unknown (e.g. the book was re-added to Komga)
        Assert.Equal(komga.Id, (await _database.GetComicByKomgaIdOrHashAsync("0OTHER", "hash-1"))?.Id);
        Assert.Null(await _database.GetComicByKomgaIdOrHashAsync("0OTHER", "other-hash"));
        Assert.Equal(komga.Id, Assert.Single(await _database.GetComicsByKomgaServerIdAsync(3)).Id);
        Assert.Empty(await _database.GetComicsByKomgaServerIdAsync(4));
    }

    [Fact]
    public async Task Sections_NewInProgressCompletedFavoritesAndRecent()
    {
        var now = DateTime.UtcNow;
        var fresh = await InsertAsync("Fresh", c => c.AddedDate = now.AddDays(-1));
        var newest = await InsertAsync("Newest", c => c.AddedDate = now);
        var reading = await InsertAsync("Reading", c =>
        {
            c.CurrentPage = 5;
            c.LastReadDate = now.AddHours(-1);
            c.AddedDate = now.AddDays(-10);
        });
        var openedOnly = await InsertAsync("OpenedOnly", c =>
        {
            c.LastReadDate = now.AddMinutes(-5);
            c.AddedDate = now.AddDays(-10);
        });
        var done = await InsertAsync("Done", c =>
        {
            c.IsCompleted = true;
            c.CurrentPage = 19;
            c.LastReadDate = now.AddDays(-2);
            c.IsFavorite = true;
            c.AddedDate = now.AddDays(-20);
        });

        Assert.Equal(new[] { newest.Id, fresh.Id }, (await _database.GetNewComicsAsync()).Select(c => c.Id));
        Assert.Equal(new[] { openedOnly.Id, reading.Id }, (await _database.GetInProgressComicsAsync()).Select(c => c.Id));
        Assert.Equal(done.Id, Assert.Single(await _database.GetCompletedComicsAsync()).Id);
        Assert.Equal(done.Id, Assert.Single(await _database.GetFavoriteComicsAsync()).Id);
        // Most recent first: last read date, or the added date when never read
        Assert.Equal(new[] { newest.Id, openedOnly.Id, reading.Id }, (await _database.GetRecentComicsAsync(3)).Select(c => c.Id));
    }

    [Fact]
    public async Task SearchComicsAsync_SearchesTitleSeriesAndAuthors_CaseInsensitive()
    {
        var byTitle = await InsertAsync("The Wolf Strip");
        var bySeries = await InsertAsync("Issue 1", c => c.SeriesName = "Wolfpack Tales");
        var byAuthor = await InsertAsync("Other", c => c.Authors = "Alice WOLFSON");
        await InsertAsync("Unrelated");

        var results = await _database.SearchComicsAsync("wolf");

        Assert.Equal(new[] { byTitle.Id, bySeries.Id, byAuthor.Id }.Order(), results.Select(c => c.Id).Order());
        Assert.Empty(await _database.SearchComicsAsync("   "));
        Assert.Empty(await _database.SearchComicsAsync("zebra"));
    }

    [Fact]
    public async Task UpdateReadingProgressAsync_StoresPageAndDates()
    {
        var comic = await InsertAsync("Progress");

        await _database.UpdateReadingProgressAsync(comic.Id, 7, isCompleted: false);

        var stored = await _database.GetComicAsync(comic.Id);
        Assert.NotNull(stored);
        Assert.Equal(7, stored.CurrentPage);
        Assert.False(stored.IsCompleted);
        Assert.NotNull(stored.LastReadDate);
        Assert.NotNull(stored.ReadProgressLastModified);
    }

    [Fact]
    public async Task UpdateReadingProgressAsync_OlderSync_DoesNotMoveLastReadDateBack()
    {
        var recent = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var older = recent.AddDays(-3);
        var comic = await InsertAsync("Sync");
        await _database.UpdateReadingProgressAsync(comic.Id, 3, false, recent);

        await _database.UpdateReadingProgressAsync(comic.Id, 10, false, older);

        var stored = await _database.GetComicAsync(comic.Id);
        Assert.NotNull(stored);
        Assert.Equal(10, stored.CurrentPage);
        Assert.Equal(older, stored.ReadProgressLastModified!.Value);
        Assert.Equal(recent, stored.LastReadDate!.Value);
    }

    [Fact]
    public async Task UpdateReadingProgressAsync_UnknownComic_DoesNothing()
    {
        await _database.UpdateReadingProgressAsync(12345, 3, true);

        Assert.Empty(await _database.GetComicsAsync());
    }

    [Fact]
    public async Task ToggleReadStatusAsync_MarkingUnread_ResetsTheProgress()
    {
        var comic = await InsertAsync("Toggle", c =>
        {
            c.CurrentPage = 4;
            c.LastReadDate = DateTime.UtcNow;
        });

        await _database.ToggleReadStatusAsync(comic.Id);
        var completed = await _database.GetComicAsync(comic.Id);
        Assert.True(completed!.IsCompleted);
        Assert.Equal(4, completed.CurrentPage);

        await _database.ToggleReadStatusAsync(comic.Id);
        var unread = await _database.GetComicAsync(comic.Id);
        Assert.False(unread!.IsCompleted);
        Assert.Equal(0, unread.CurrentPage);
        Assert.Null(unread.LastReadDate);
        Assert.NotNull(unread.ReadProgressLastModified);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_TogglesTheFlag()
    {
        var comic = await InsertAsync("Favorite");

        await _database.ToggleFavoriteAsync(comic.Id);
        Assert.True((await _database.GetComicAsync(comic.Id))!.IsFavorite);

        await _database.ToggleFavoriteAsync(comic.Id);
        Assert.False((await _database.GetComicAsync(comic.Id))!.IsFavorite);
    }

    [Fact]
    public async Task PendingKomgaReadProgress_SaveReplaceAndDelete()
    {
        var localTime = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Local);

        Assert.Equal(1, await _database.SavePendingKomgaReadProgressAsync(10, "0BOOK1", 2, 5, false, localTime));
        var pending = await _database.GetPendingKomgaReadProgressAsync(10);
        Assert.NotNull(pending);
        Assert.Equal("0BOOK1", pending.BookId);
        Assert.Equal(2, pending.ServerId);
        Assert.Equal(5, pending.Page);
        Assert.False(pending.IsCompleted);
        // Stored as UTC
        Assert.Equal(localTime.ToUniversalTime().Ticks, pending.ReadProgressLastModifiedUtc.Ticks);

        // One entry per comic: saving again replaces it
        await _database.SavePendingKomgaReadProgressAsync(10, "0BOOK1", 2, 19, true, DateTime.UtcNow);
        var replaced = await _database.GetPendingKomgaReadProgressAsync(10);
        Assert.Equal(19, replaced!.Page);
        Assert.True(replaced.IsCompleted);

        Assert.Equal(1, await _database.DeletePendingKomgaReadProgressAsync(10));
        Assert.Null(await _database.GetPendingKomgaReadProgressAsync(10));
    }

    [Fact]
    public async Task PendingKomgaReadProgress_InvalidInput_IsIgnored()
    {
        Assert.Equal(0, await _database.SavePendingKomgaReadProgressAsync(0, "0BOOK1", 1, 1, false, DateTime.UtcNow));
        Assert.Equal(0, await _database.SavePendingKomgaReadProgressAsync(5, " ", 1, 1, false, DateTime.UtcNow));
        Assert.Equal(0, await _database.DeletePendingKomgaReadProgressAsync(-1));
        Assert.Null(await _database.GetPendingKomgaReadProgressAsync(5));
    }

    [Fact]
    public async Task PendingKomgaDownloads_SaveListDeleteAndReplace()
    {
        await _database.SavePendingKomgaDownloadAsync("0BOOK1", 1);
        await _database.SavePendingKomgaDownloadAsync("0BOOK2");
        await _database.SavePendingKomgaDownloadAsync("0BOOK1", 1);
        Assert.Equal(0, await _database.SavePendingKomgaDownloadAsync("  "));

        Assert.Equal(new[] { "0BOOK1", "0BOOK2" }, (await _database.GetPendingKomgaDownloadBookIdsAsync()).Order(StringComparer.Ordinal));

        Assert.Equal(1, await _database.DeletePendingKomgaDownloadAsync("0BOOK2"));
        Assert.Equal(new[] { "0BOOK1" }, await _database.GetPendingKomgaDownloadBookIdsAsync());

        await _database.ReplacePendingKomgaDownloadsAsync(
        [
            new KomgaPendingDownload { BookId = "0BOOK3", ServerId = 1 },
            new KomgaPendingDownload { BookId = "0BOOK4", ServerId = 1 },
            new KomgaPendingDownload { BookId = "0BOOK4", ServerId = 2 },
            new KomgaPendingDownload { BookId = "" }
        ]);

        var downloads = await _database.GetPendingKomgaDownloadsAsync();
        Assert.Equal(new[] { "0BOOK3", "0BOOK4" }, downloads.Select(d => d.BookId).Order(StringComparer.Ordinal));
        // Duplicates: the last one wins
        Assert.Equal(2, downloads.Single(d => d.BookId == "0BOOK4").ServerId);
    }

    [Fact]
    public async Task EpubConversionState_SaveGetIncompleteAndDelete()
    {
        await _database.SaveEpubConversionStateAsync(new EpubConversionState
        {
            ComicId = 1,
            SourceEpubPath = "/comics/a.epub",
            ShadowPath = "/shadow/1",
            Status = EpubConversionStatus.Converting,
            ProducedPageCount = 3,
            NextChapterIndex = 2,
            PaginationSignature = "sig"
        });
        await _database.SaveEpubConversionStateAsync(new EpubConversionState { ComicId = 2, Status = EpubConversionStatus.Completed, FinalPageCount = 10 });
        await _database.SaveEpubConversionStateAsync(new EpubConversionState { ComicId = 3, Status = EpubConversionStatus.Failed, LastError = "boom" });

        var state = await _database.GetEpubConversionStateAsync(1);
        Assert.NotNull(state);
        Assert.Equal("/comics/a.epub", state.SourceEpubPath);
        Assert.Equal(EpubConversionStatus.Converting, state.Status);
        Assert.Equal(3, state.ProducedPageCount);
        Assert.Equal(2, state.NextChapterIndex);
        Assert.Equal("sig", state.PaginationSignature);

        state.Status = EpubConversionStatus.Paused;
        await _database.SaveEpubConversionStateAsync(state);
        Assert.Equal(EpubConversionStatus.Paused, (await _database.GetEpubConversionStateAsync(1))!.Status);

        Assert.Equal(new[] { 1, 3 }, (await _database.GetIncompleteEpubConversionStatesAsync()).Select(s => s.ComicId).Order());

        Assert.Equal(1, await _database.DeleteEpubConversionStateAsync(1));
        Assert.Null(await _database.GetEpubConversionStateAsync(1));
    }

    [Fact]
    public async Task Initialization_DropsTheLegacyKomgaServerTable()
    {
        // Older versions created a KomgaServer table with plain text passwords
        var legacy = new SQLite.SQLiteAsyncConnection(DatabasePath);
        await legacy.ExecuteAsync("CREATE TABLE KomgaServer (Id integer primary key autoincrement, Password varchar, ApiKey varchar)");
        await legacy.ExecuteAsync("INSERT INTO KomgaServer (Password, ApiKey) VALUES ('secret', 'key')");
        await legacy.CloseAsync();

        // Any call initializes the database
        await _database.GetComicsAsync();
        await _database.DisposeAsync();

        var check = new SQLite.SQLiteAsyncConnection(DatabasePath);
        var tableCount = await check.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='KomgaServer'");
        await check.CloseAsync();
        Assert.Equal(0, tableCount);

        _database = new DatabaseService(DatabasePath);
    }

    [Fact]
    public async Task UsageCounters_AreIncrementedPerMetric()
    {
        Assert.Equal(0, await _database.GetUsageCountAsync("PagesRead"));

        await _database.IncrementUsageAsync("PagesRead");
        await _database.IncrementUsageAsync("PagesRead", 41);
        await _database.IncrementUsageAsync("ComicOpen");
        await _database.IncrementUsageAsync("ComicOpen", 0);
        await _database.IncrementUsageAsync("ComicOpen", -5);

        Assert.Equal(42, await _database.GetUsageCountAsync("PagesRead"));
        Assert.Equal(1, await _database.GetUsageCountAsync("ComicOpen"));
        Assert.Equal(0, await _database.GetUsageCountAsync("Unknown"));
    }

    [Fact]
    public async Task UsageCounters_ConcurrentIncrements_AreAllCounted()
    {
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => _database.IncrementUsageAsync("LocalImport"))));

        Assert.Equal(50, await _database.GetUsageCountAsync("LocalImport"));
    }

    [Fact]
    public async Task UsageCounters_OneRowPerMetric()
    {
        for (var index = 0; index < 20; index++)
        {
            await _database.IncrementUsageAsync("PagesRead");
        }

        await _database.DisposeAsync();
        var check = new SQLite.SQLiteAsyncConnection(DatabasePath);
        var rows = await check.ExecuteScalarAsync<int>($"SELECT count(*) FROM {UsageCounter.TableName}");
        await check.CloseAsync();
        _database = new DatabaseService(DatabasePath);

        Assert.Equal(1, rows);
        Assert.Equal(20, await _database.GetUsageCountAsync("PagesRead"));
    }

    /// <summary>
    /// The old UsageStats table had a row per event, it is folded into the counters once and dropped
    /// </summary>
    [Fact]
    public async Task Initialization_MigratesTheLegacyUsageStatsTable()
    {
        var legacy = new SQLite.SQLiteAsyncConnection(DatabasePath);
        await legacy.ExecuteAsync("CREATE TABLE UsageStats (Id integer primary key autoincrement, Metric varchar, Timestamp bigint, Metadata varchar)");
        for (var index = 0; index < 3; index++)
        {
            await legacy.ExecuteAsync("INSERT INTO UsageStats (Metric, Timestamp, Metadata) VALUES ('PagesRead', 0, NULL)");
        }

        await legacy.ExecuteAsync("INSERT INTO UsageStats (Metric, Timestamp, Metadata) VALUES ('ComicOpen', 0, '12')");
        await legacy.ExecuteAsync("INSERT INTO UsageStats (Metric, Timestamp, Metadata) VALUES (NULL, 0, NULL)");
        await legacy.CloseAsync();

        Assert.Equal(3, await _database.GetUsageCountAsync("PagesRead"));
        Assert.Equal(1, await _database.GetUsageCountAsync("ComicOpen"));
        await _database.IncrementUsageAsync("PagesRead");
        Assert.Equal(4, await _database.GetUsageCountAsync("PagesRead"));
        await _database.DisposeAsync();

        var check = new SQLite.SQLiteAsyncConnection(DatabasePath);
        var tableCount = await check.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='UsageStats'");
        await check.CloseAsync();
        Assert.Equal(0, tableCount);

        // Opening the database again doesn't count anything twice
        _database = new DatabaseService(DatabasePath);
        Assert.Equal(4, await _database.GetUsageCountAsync("PagesRead"));
    }

    [Fact]
    public async Task BookmarkedFileImports_ArePerBookmark()
    {
        var modified = new DateTimeOffset(2025, 4, 5, 6, 7, 8, TimeSpan.FromHours(2));

        await _database.MarkBookmarkedFileImportedAsync("bookmark-a", "Series/issue1.cbz", 1234, modified);
        await _database.MarkBookmarkedFileImportedAsync("bookmark-a", "Series/issue2.cbz", null, null);
        await _database.MarkBookmarkedFileImportedAsync("bookmark-b", "Series/issue1.cbz", 99, null);
        // Marking again replaces the entry
        await _database.MarkBookmarkedFileImportedAsync("bookmark-a", "Series/issue1.cbz", 5678, modified);

        var importedA = await _database.GetImportedBookmarkedFilesAsync("bookmark-a");
        Assert.Equal(new[] { "Series/issue1.cbz", "Series/issue2.cbz" }, importedA.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Series/issue1.cbz" }, await _database.GetImportedBookmarkedFilesAsync("bookmark-b"));
        Assert.Empty(await _database.GetImportedBookmarkedFilesAsync("bookmark-c"));
        // Paths are case sensitive
        Assert.False(importedA.Contains("series/issue1.cbz"));

        await _database.DisposeAsync();
        var check = new SQLite.SQLiteAsyncConnection(DatabasePath);
        var row = await check.FindAsync<BookmarkedFileImport>(BookmarkedFileImport.CreateKey("bookmark-a", "Series/issue1.cbz"));
        var rows = await check.ExecuteScalarAsync<int>("SELECT count(*) FROM BookmarkedFileImport");
        await check.CloseAsync();
        _database = new DatabaseService(DatabasePath);

        Assert.Equal(3, rows);
        Assert.NotNull(row);
        Assert.Equal(5678, row.Size);
        Assert.Equal(modified.UtcTicks, row.ModifiedUtcTicks);
        Assert.True(row.ImportedUtcTicks > 0);
    }

    [Fact]
    public async Task Data_SurvivesReopeningTheDatabase()
    {
        var comic = await InsertAsync("Persistent", c => c.CurrentPage = 3);
        await _database.DisposeAsync();

        _database = new DatabaseService(DatabasePath);
        var stored = await _database.GetComicAsync(comic.Id);

        Assert.NotNull(stored);
        Assert.Equal("Persistent", stored.Title);
        Assert.Equal(3, stored.CurrentPage);
    }

    [Fact]
    public async Task ConcurrentAccess_DuringInitialization_Works()
    {
        var comics = Enumerable.Range(0, 25).Select(index => NewComic($"Parallel {index}")).ToList();

        await Task.WhenAll(comics.Select(comic => Task.Run(() => _database.SaveComicAsync(comic))));

        Assert.Equal(25, (await _database.GetComicsAsync()).Count);
        Assert.Equal(25, comics.Select(comic => comic.Id).Distinct().Count());
    }
}
