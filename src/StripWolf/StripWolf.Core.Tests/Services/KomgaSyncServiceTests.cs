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

using System.Text.Json;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

/// <summary>
/// KomgaSyncService gets its KomgaApiService from KomgaApiServiceFactory (no handler seam), so these tests run a tiny
/// HTTP server on 127.0.0.1. Note: page numbers are 1-based in Komga and 0-based in StripWolf.
/// </summary>
public sealed class KomgaSyncServiceTests
{
    private const int ServerId = 7;
    private const string BookId = "0BOOK1";

    private sealed class Context : IAsyncDisposable
    {
        private Context(TestServiceHost host, LoopbackHttpServer server, KomgaSyncService sync)
        {
            Host = host;
            Server = server;
            Sync = sync;
        }

        public TestServiceHost Host { get; }

        public LoopbackHttpServer Server { get; }

        public KomgaSyncService Sync { get; }

        /// <summary>
        /// The Komga book returned by GET api/v1/books/0BOOK1, null for 404
        /// </summary>
        public string? BookJson { get; set; } = KomgaJson.Book(BookId);

        public int ReadProgressStatus { get; set; } = 204;

        public IEnumerable<LoopbackRequest> ReadProgressRequests => Server.Requests.Where(request => request.Method == "PATCH");

        public static async Task<Context> CreateAsync(bool syncReadProgress = true, bool configureServer = true)
        {
            Context? context = null;
            // ReSharper disable once AccessToModifiedClosure
            var server = new LoopbackHttpServer(request => context!.Handle(request));
            var host = await TestServiceHost.CreateAsync(settings =>
            {
                settings.SyncReadProgress = syncReadProgress;
                if (configureServer)
                {
                    settings.Servers.Add(new KomgaServer
                    {
                        Id = ServerId,
                        Name = "Loopback",
                        BaseUrl = server.BaseUrl,
                        Username = "reader",
                        Password = "secret"
                    });
                }
            });
            context = new Context(host, server, new KomgaSyncService(host.Library, host.KomgaApiServiceFactory, host.Settings, host.Database));
            return context;
        }

        private LoopbackResponse Handle(LoopbackRequest request)
        {
            var path = request.PathAndQuery.Split('?')[0];
            if (request.Method == "GET" && path == $"/api/v1/books/{BookId}")
            {
                return BookJson is null ? LoopbackResponse.Status(404) : LoopbackResponse.Json(BookJson);
            }

            if (request.Method == "PATCH" && path == $"/api/v1/books/{BookId}/read-progress")
            {
                return LoopbackResponse.Status(ReadProgressStatus);
            }

            return LoopbackResponse.Status(404);
        }

        public async Task<Comic> AddKomgaComicAsync(int currentPage, DateTime? readProgressLastModified, bool isCompleted = false)
        {
            var comic = new Comic
            {
                Title = "Wolf Strips 001",
                FilePath = Host.Temp.Combine("komga", "book.cbz"),
                PageCount = 20,
                Format = ComicFormat.Cbz,
                Source = ComicSource.Komga,
                KomgaId = BookId,
                KomgaServerId = ServerId,
                CurrentPage = currentPage,
                IsCompleted = isCompleted,
                ReadProgressLastModified = readProgressLastModified
            };
            await Host.Database.SaveComicAsync(comic);
            return comic;
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            await Host.DisposeAsync();
        }
    }

    private static (int Page, bool Completed) ParseProgress(LoopbackRequest request)
    {
        using var document = JsonDocument.Parse(request.Body);
        return (document.RootElement.GetProperty("page").GetInt32(), document.RootElement.GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task KomgaIsNewer_UpdatesTheLocalProgress()
    {
        await using var context = await Context.CreateAsync();
        var komgaModified = DateTime.UtcNow.AddMinutes(-1);
        context.BookJson = KomgaJson.Book(BookId, KomgaJson.ReadProgress(page: 8, completed: false, komgaModified));
        var comic = await context.AddKomgaComicAsync(currentPage: 2, readProgressLastModified: DateTime.UtcNow.AddDays(-1));

        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Equal("Updated from Komga", comic.KomgaSyncStatus);
        Assert.Equal(7, comic.CurrentPage);
        Assert.False(comic.IsCompleted);
        var stored = await context.Host.Database.GetComicAsync(comic.Id);
        Assert.Equal(7, stored!.CurrentPage);
        Assert.Empty(context.ReadProgressRequests);
        Assert.Null(await context.Host.Database.GetPendingKomgaReadProgressAsync(comic.Id));
    }

    [Fact]
    public async Task LocalIsNewer_PushesTheProgressToKomga()
    {
        await using var context = await Context.CreateAsync();
        context.BookJson = KomgaJson.Book(BookId, KomgaJson.ReadProgress(page: 3, completed: false, DateTime.UtcNow.AddDays(-2)));
        var comic = await context.AddKomgaComicAsync(currentPage: 9, readProgressLastModified: DateTime.UtcNow.AddMinutes(-5));

        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Equal("Synced to Komga", comic.KomgaSyncStatus);
        var push = Assert.Single(context.ReadProgressRequests);
        Assert.Equal((10, false), ParseProgress(push));
        Assert.Null(await context.Host.Database.GetPendingKomgaReadProgressAsync(comic.Id));
        Assert.Equal(9, (await context.Host.Database.GetComicAsync(comic.Id))!.CurrentPage);
    }

    /// <summary>
    /// sqlite-net reads DateTime values back with DateTimeKind.Unspecified. They are UTC: treating them as local time
    /// (ToUniversalTime) shifted them by the UTC offset, so outside of UTC the wrong side "won". These run in any
    /// timezone, but only fail with the old behavior on machines which are not on UTC.
    /// </summary>
    [Fact]
    public async Task UnspecifiedLocalTimestamp_IsUtc_KomgaOneMinuteNewer()
    {
        await using var context = await Context.CreateAsync();
        var localUtc = DateTime.UtcNow.AddHours(-3);
        context.BookJson = KomgaJson.Book(BookId, KomgaJson.ReadProgress(page: 12, completed: false, localUtc.AddMinutes(1)));
        var comic = await context.AddKomgaComicAsync(currentPage: 4, readProgressLastModified: DateTime.SpecifyKind(localUtc, DateTimeKind.Unspecified));

        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Equal("Updated from Komga", comic.KomgaSyncStatus);
        Assert.Equal(11, comic.CurrentPage);
        Assert.Empty(context.ReadProgressRequests);
    }

    [Fact]
    public async Task UnspecifiedLocalTimestamp_IsUtc_KomgaOneMinuteOlder()
    {
        await using var context = await Context.CreateAsync();
        var localUtc = DateTime.UtcNow.AddHours(-3);
        context.BookJson = KomgaJson.Book(BookId, KomgaJson.ReadProgress(page: 12, completed: false, localUtc.AddMinutes(-1)));
        var comic = await context.AddKomgaComicAsync(currentPage: 4, readProgressLastModified: DateTime.SpecifyKind(localUtc, DateTimeKind.Unspecified));

        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Equal("Synced to Komga", comic.KomgaSyncStatus);
        Assert.Equal(4, comic.CurrentPage);
        Assert.Equal((5, false), ParseProgress(Assert.Single(context.ReadProgressRequests)));
    }

    /// <summary>
    /// The same through the database (SyncAllComicsAsync loads the comics, their dates come back Unspecified)
    /// </summary>
    [Fact]
    public async Task SyncAllComicsAsync_ComicsFromTheDatabase_CompareInUtc()
    {
        await using var context = await Context.CreateAsync();
        var localUtc = DateTime.UtcNow.AddHours(-3);
        context.BookJson = KomgaJson.Book(BookId, KomgaJson.ReadProgress(page: 12, completed: false, localUtc.AddMinutes(-1)));
        var comic = await context.AddKomgaComicAsync(currentPage: 4, readProgressLastModified: localUtc);

        await context.Sync.SyncAllComicsAsync();

        // Local is newer: pushed to Komga, not overwritten by the older Komga progress
        Assert.Equal((5, false), ParseProgress(Assert.Single(context.ReadProgressRequests)));
        Assert.Equal(4, (await context.Host.Database.GetComicAsync(comic.Id))!.CurrentPage);
    }

    [Fact]
    public async Task SameTime_IsInSync()
    {
        await using var context = await Context.CreateAsync();
        var modified = DateTime.UtcNow.AddHours(-1);
        context.BookJson = KomgaJson.Book(BookId, KomgaJson.ReadProgress(page: 4, completed: false, modified));
        var comic = await context.AddKomgaComicAsync(currentPage: 3, readProgressLastModified: modified.AddSeconds(1));

        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Equal("In sync", comic.KomgaSyncStatus);
        Assert.Empty(context.ReadProgressRequests);
    }

    [Fact]
    public async Task NoProgressOnKomga_PushesTheLocalProgress()
    {
        await using var context = await Context.CreateAsync();
        var comic = await context.AddKomgaComicAsync(currentPage: 19, readProgressLastModified: DateTime.UtcNow, isCompleted: true);

        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Equal("Synced to Komga", comic.KomgaSyncStatus);
        Assert.Equal((20, true), ParseProgress(Assert.Single(context.ReadProgressRequests)));
    }

    [Fact]
    public async Task PushFails_KeepsThePendingProgressForALaterRetry()
    {
        await using var context = await Context.CreateAsync();
        context.ReadProgressStatus = 500;
        var comic = await context.AddKomgaComicAsync(currentPage: 5, readProgressLastModified: DateTime.UtcNow);

        await context.Sync.PushProgressToKomgaAsync(comic);

        Assert.Equal("Sync failed", comic.KomgaSyncStatus);
        var pending = await context.Host.Database.GetPendingKomgaReadProgressAsync(comic.Id);
        Assert.NotNull(pending);
        Assert.Equal(5, pending.Page);
        Assert.Equal(BookId, pending.BookId);

        // The server is back: the pending progress is pushed first during the next sync
        context.ReadProgressStatus = 204;
        context.BookJson = KomgaJson.Book(BookId, KomgaJson.ReadProgress(page: 6, completed: false, DateTime.UtcNow.AddDays(-1)));
        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Null(await context.Host.Database.GetPendingKomgaReadProgressAsync(comic.Id));
        Assert.All(context.ReadProgressRequests, request => Assert.Equal((6, false), ParseProgress(request)));
    }

    [Fact]
    public async Task PushProgressToKomgaAsync_SendsTheCurrentPage()
    {
        await using var context = await Context.CreateAsync();
        var comic = await context.AddKomgaComicAsync(currentPage: 0, readProgressLastModified: DateTime.UtcNow);

        await context.Sync.PushProgressToKomgaAsync(comic);

        Assert.Equal("Synced to Komga", comic.KomgaSyncStatus);
        Assert.Equal((1, false), ParseProgress(Assert.Single(context.ReadProgressRequests)));
        var authorization = Assert.Single(context.Server.Requests).Headers["Authorization"];
        Assert.StartsWith("Basic ", authorization);
    }

    [Fact]
    public async Task BookNotOnKomga_ChangesNothing()
    {
        await using var context = await Context.CreateAsync();
        context.BookJson = null;
        var comic = await context.AddKomgaComicAsync(currentPage: 4, readProgressLastModified: DateTime.UtcNow);

        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Equal(4, comic.CurrentPage);
        Assert.Empty(context.ReadProgressRequests);
    }

    [Fact]
    public async Task UnknownServer_ReportsServerNotFound()
    {
        await using var context = await Context.CreateAsync(configureServer: false);
        var comic = await context.AddKomgaComicAsync(currentPage: 4, readProgressLastModified: DateTime.UtcNow);

        await context.Sync.SyncComicReadProgressAsync(comic);

        Assert.Equal("Server not found", comic.KomgaSyncStatus);
        Assert.Empty(context.Server.Requests);
    }

    [Fact]
    public async Task SyncDisabled_DoesNothing()
    {
        await using var context = await Context.CreateAsync(syncReadProgress: false);
        var comic = await context.AddKomgaComicAsync(currentPage: 4, readProgressLastModified: DateTime.UtcNow);

        await context.Sync.SyncComicReadProgressAsync(comic);
        await context.Sync.PushProgressToKomgaAsync(comic);
        await context.Sync.SyncAllComicsAsync();

        Assert.Null(comic.KomgaSyncStatus);
        Assert.Empty(context.Server.Requests);
    }

    [Fact]
    public async Task LocalComic_IsIgnored()
    {
        await using var context = await Context.CreateAsync();
        var comic = new Comic { Title = "Local", FilePath = "/comics/local.cbz", PageCount = 10, Source = ComicSource.Local, CurrentPage = 3 };
        await context.Host.Database.SaveComicAsync(comic);

        await context.Sync.SyncComicReadProgressAsync(comic);
        await context.Sync.PushProgressToKomgaAsync(comic);

        Assert.Null(comic.KomgaSyncStatus);
        Assert.Empty(context.Server.Requests);
    }

    [Fact]
    public async Task SyncAllComicsAsync_UpdatesTheLibraryFromKomga()
    {
        await using var context = await Context.CreateAsync();
        context.BookJson = KomgaJson.Book(BookId, KomgaJson.ReadProgress(page: 20, completed: true, DateTime.UtcNow));
        var comic = await context.AddKomgaComicAsync(currentPage: 1, readProgressLastModified: DateTime.UtcNow.AddDays(-3));

        await context.Sync.SyncAllComicsAsync();

        var stored = await context.Host.Database.GetComicAsync(comic.Id);
        Assert.NotNull(stored);
        Assert.Equal(19, stored.CurrentPage);
        Assert.True(stored.IsCompleted);
    }
}
