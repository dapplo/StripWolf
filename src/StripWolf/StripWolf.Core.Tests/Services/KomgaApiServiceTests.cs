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

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

/// <summary>
/// KomgaApiService against an in-memory Komga (fake HttpMessageHandler via the internal constructor)
/// </summary>
public sealed class KomgaApiServiceTests
{
    private const string BaseUrl = "https://komga.example.com";

    private static KomgaServer Server(Action<KomgaServer>? configure = null)
    {
        var server = new KomgaServer
        {
            Id = 1,
            Name = "Test",
            BaseUrl = BaseUrl,
            Username = "reader@example.com",
            Password = "pa:ss wörd"
        };
        configure?.Invoke(server);
        return server;
    }

    private static (KomgaApiService Service, FakeKomgaServer Komga) Create(Func<RecordedRequest, HttpResponseMessage> responder, KomgaServer? server = null)
    {
        var komga = new FakeKomgaServer(responder);
        var service = new KomgaApiService(komga.CreateHandler);
        service.Configure(server ?? Server());
        return (service, komga);
    }

    /// <summary>
    /// Routes by path (without query), 404 for everything else
    /// </summary>
    private static Func<RecordedRequest, HttpResponseMessage> Routes(params (string Path, Func<RecordedRequest, HttpResponseMessage> Response)[] routes)
    {
        return request =>
        {
            foreach (var (path, response) in routes)
            {
                if (string.Equals(request.Path, path, StringComparison.Ordinal))
                {
                    return response(request);
                }
            }

            return FakeKomgaServer.Status(HttpStatusCode.NotFound);
        };
    }

    [Fact]
    public async Task NotConfigured_Throws()
    {
        using var service = new KomgaApiService(new FakeKomgaServer(_ => FakeKomgaServer.Status(HttpStatusCode.OK)).CreateHandler);

        Assert.False(service.IsConfigured);
        Assert.Null(service.BaseUrl);
        Assert.Null(service.CurrentServerId);
        Assert.Equal(string.Empty, service.GetBookThumbnailUrl("0BOOK1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLibrariesAsync());
        Assert.Equal((false, "Service is not configured"), await service.TestConnectionWithDetailsAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://komga.example.com", "https://komga.example.com/api/v1/libraries")]
    [InlineData("https://komga.example.com/", "https://komga.example.com/api/v1/libraries")]
    [InlineData("https://komga.example.com/api/v1/", "https://komga.example.com/api/v1/libraries")]
    [InlineData("https://komga.example.com/api", "https://komga.example.com/api/v1/libraries")]
    [InlineData("  https://example.com/komga/  ", "https://example.com/komga/api/v1/libraries")]
    [InlineData("http://192.168.1.10:25600/komga/api/v1", "http://192.168.1.10:25600/komga/api/v1/libraries")]
    public async Task Configure_NormalizesTheBaseUrl(string baseUrl, string expectedRequestUrl)
    {
        var (service, komga) = Create(_ => FakeKomgaServer.Json("[]"), Server(server => server.BaseUrl = baseUrl));
        using var serviceLifetime = service;

        await service.GetLibrariesAsync();

        Assert.Equal(expectedRequestUrl, Assert.Single(komga.Requests).Uri.ToString());
        Assert.Equal(baseUrl, service.BaseUrl);
    }

    [Fact]
    public void ThumbnailUrls_AreAbsolute()
    {
        var (service, _) = Create(_ => FakeKomgaServer.Status(HttpStatusCode.OK), Server(server => server.BaseUrl = "https://example.com/komga/"));
        using var serviceLifetime = service;

        Assert.Equal("https://example.com/komga/api/v1/books/0BOOK1/thumbnail", service.GetBookThumbnailUrl("0BOOK1"));
        Assert.Equal("https://example.com/komga/api/v1/series/0SER1/thumbnail", service.GetSeriesThumbnailUrl("0SER1"));
        Assert.Equal("https://example.com/komga/api/v1/readlists/0RL1/thumbnail", service.GetReadListThumbnailUrl("0RL1"));
        Assert.Equal(1, service.CurrentServerId);
    }

    [Fact]
    public async Task UsernameAndPassword_UseBasicAuthentication()
    {
        var (service, komga) = Create(_ => FakeKomgaServer.Json("[]"));
        using var serviceLifetime = service;

        await service.GetLibrariesAsync();

        var request = Assert.Single(komga.Requests);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("reader@example.com:pa:ss wörd"));
        Assert.Equal($"Basic {expected}", request.Header("Authorization"));
        Assert.Null(request.Header("X-API-Key"));
        Assert.Contains("application/json", request.Header("Accept"));
    }

    [Fact]
    public async Task ApiKey_IsPreferredOverUsernameAndPassword()
    {
        var (service, komga) = Create(_ => FakeKomgaServer.Json("[]"), Server(server => server.ApiKey = "my-api-key"));
        using var serviceLifetime = service;

        await service.GetLibrariesAsync();

        var request = Assert.Single(komga.Requests);
        Assert.Equal("my-api-key", request.Header("X-API-Key"));
        Assert.Null(request.Header("Authorization"));
    }

    [Fact]
    public async Task CustomHeaders_AreSent_EmptyOnesAreSkipped()
    {
        var (service, komga) = Create(_ => FakeKomgaServer.Json("[]"), Server(server => server.CustomHeaders =
        [
            new KomgaHeader { Name = "CF-Access-Client-Id", Value = "client-id" },
            new KomgaHeader { Name = "X-Empty", Value = "" },
            new KomgaHeader { Name = "", Value = "no name" }
        ]));
        using var serviceLifetime = service;

        await service.GetLibrariesAsync();

        var request = Assert.Single(komga.Requests);
        Assert.Equal("client-id", request.Header("CF-Access-Client-Id"));
        Assert.Null(request.Header("X-Empty"));
    }

    [Fact]
    public async Task Configure_AgainReplacesTheClientAndAuthentication()
    {
        var komga = new FakeKomgaServer(_ => FakeKomgaServer.Json("[]"));
        using var service = new KomgaApiService(komga.CreateHandler);

        service.Configure(Server());
        await service.GetLibrariesAsync();
        service.Configure(Server(server =>
        {
            server.Id = 2;
            server.BaseUrl = "https://other.example.com";
            server.ApiKey = "key";
        }));
        await service.GetLibrariesAsync();

        Assert.Equal(2, komga.HandlersCreated);
        Assert.Equal(2, service.CurrentServerId);
        var second = komga.Requests[^1];
        Assert.Equal("other.example.com", second.Uri.Host);
        Assert.Equal("key", second.Header("X-API-Key"));
        Assert.Null(second.Header("Authorization"));
    }

    [Fact]
    public async Task GetLibrariesAsync_ParsesTheResponse()
    {
        var (service, _) = Create(Routes(("/api/v1/libraries", _ => FakeKomgaServer.Json(KomgaJson.Libraries))));
        using var serviceLifetime = service;

        var libraries = await service.GetLibrariesAsync();

        Assert.Equal(2, libraries.Count);
        Assert.Equal("0LIB1", libraries[0].Id);
        Assert.Equal("Comics", libraries[0].Name);
        Assert.Equal("/data/comics", libraries[0].Root);
        Assert.True(libraries[0].ImportComicInfoBook);
        Assert.Null(libraries[0].OneshotsDirectory);
        Assert.True(libraries[1].Unavailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ErrorStatus_ThrowsHttpRequestException(HttpStatusCode statusCode)
    {
        var (service, _) = Create(_ => FakeKomgaServer.Status(statusCode));
        using var serviceLifetime = service;

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetLibrariesAsync());
        Assert.Equal(statusCode, exception.StatusCode);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetSeriesAsync(0, 20));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetBooksForSeriesAsync("0SER1"));
    }

    [Fact]
    public async Task GetSeriesAsync_BuildsTheQueryAndParsesThePage()
    {
        var (service, komga) = Create(Routes(("/api/v1/series", _ => FakeKomgaServer.Json(KomgaJson.SeriesPage(0, last: false)))));
        using var serviceLifetime = service;

        var page = await service.GetSeriesAsync(page: 0, size: 1, libraryId: "0LIB1", searchPrefix: "0-9");

        var query = Assert.Single(komga.Requests).Query;
        Assert.Contains("page=0", query);
        Assert.Contains("size=1", query);
        Assert.Contains("library_id=0LIB1", query);
        Assert.Contains("sort=metadata.titleSort,asc", query);
        Assert.Contains("search_regex=^[0-9].*,TITLE", query);

        Assert.False(page.Last);
        Assert.True(page.First);
        Assert.Equal(2, page.TotalElements);
        Assert.Equal(0, page.Pageable?.PageNumber);
        var series = Assert.Single(page.Content);
        Assert.Equal("0SER1", series.Id);
        Assert.Equal("Wolf Strips", series.Name);
        Assert.Equal(3, series.BooksCount);
        Assert.Equal("LEFT_TO_RIGHT", series.Metadata?.ReadingDirection);
        Assert.Equal(12, series.Metadata?.AgeRating);
        Assert.Equal(new[] { "Comedy" }, series.Metadata?.Genres);
    }

    [Theory]
    [InlineData("A.B", "^A\\.B.*,TITLE")]
    [InlineData("C++ (", "^C\\+\\+\\ \\(.*,TITLE")]
    [InlineData("Wolf", "^Wolf.*,TITLE")]
    public async Task GetSeriesAsync_SearchPrefix_IsRegexEscaped(string prefix, string expectedRegex)
    {
        var (service, komga) = Create(Routes(("/api/v1/series", _ => FakeKomgaServer.Json(KomgaJson.SeriesPage(0, last: true)))));
        using var serviceLifetime = service;

        await service.GetSeriesAsync(searchPrefix: prefix);

        Assert.Contains("search_regex=" + expectedRegex, Assert.Single(komga.Requests).Query);
    }

    [Fact]
    public async Task GetSeriesAsync_ById_NotFoundReturnsNull()
    {
        var (service, _) = Create(Routes(("/api/v1/series/0SER1", _ => FakeKomgaServer.Json(KomgaJson.Series))));
        using var serviceLifetime = service;

        Assert.Equal("Wolf Strips", (await service.GetSeriesAsync("0SER1"))?.Name);
        Assert.Null(await service.GetSeriesAsync("0MISSING"));
    }

    [Fact]
    public async Task SearchSeriesAndBooks_EscapeTheQuery()
    {
        var (service, komga) = Create(Routes(
            ("/api/v1/series", _ => FakeKomgaServer.Json(KomgaJson.SeriesPage(0, last: true))),
            ("/api/v1/books", _ => FakeKomgaServer.Json(KomgaJson.BooksPage(0, true, "0BOOK1")))));
        using var serviceLifetime = service;

        await service.SearchSeriesAsync("wolf & co");
        var books = await service.SearchBooksAsync("über?");

        Assert.Contains("search=wolf & co", komga.Requests[0].Query);
        Assert.Contains("%26", komga.Requests[0].Uri.Query);
        Assert.Contains("search=über?", komga.Requests[1].Query);
        Assert.Equal("0BOOK1", Assert.Single(books.Content).Id);
    }

    [Fact]
    public async Task GetBookAsync_ParsesTheBook()
    {
        var lastModified = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var (service, _) = Create(Routes(("/api/v1/books/0BOOK1", _ => FakeKomgaServer.Json(KomgaJson.Book("0BOOK1", KomgaJson.ReadProgress(5, false, lastModified))))));
        using var serviceLifetime = service;

        var book = await service.GetBookAsync("0BOOK1");

        Assert.NotNull(book);
        Assert.Equal("0BOOK1", book.Id);
        Assert.Equal("0SER1", book.SeriesId);
        Assert.Equal("Wolf Strips", book.SeriesTitle);
        Assert.Equal(1.5f, book.Number);
        Assert.Equal(15105, book.SizeBytes);
        Assert.Equal("abc123", book.FileHash);
        Assert.Equal("application/zip", book.Media?.MediaType);
        Assert.Equal(4, book.Media?.PagesCount);
        Assert.Equal("The Wolf Strip", book.Metadata?.Title);
        Assert.Equal("2024-05-17", book.Metadata?.ReleaseDate);
        Assert.Equal(new[] { "writer", "penciller" }, book.Metadata?.Authors.Select(author => author.Role));
        Assert.NotNull(book.ReadProgress);
        Assert.Equal(5, book.ReadProgress.Page);
        Assert.False(book.ReadProgress.Completed);
        Assert.Equal(lastModified, book.ReadProgress.LastModified.ToUniversalTime());

        Assert.Null(await service.GetBookAsync("0MISSING"));
    }

    [Fact]
    public async Task GetBookAsync_ServerError_Throws()
    {
        var (service, _) = Create(_ => FakeKomgaServer.Status(HttpStatusCode.InternalServerError));
        using var serviceLifetime = service;

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetBookAsync("0BOOK1"));
    }

    [Fact]
    public async Task GetAllBooksForSeriesAsync_ReadsAllPages()
    {
        var (service, komga) = Create(Routes(("/api/v1/series/0SER1/books", request =>
            request.Query.Contains("page=0", StringComparison.Ordinal)
                ? FakeKomgaServer.Json(KomgaJson.BooksPage(0, false, "0BOOK1", "0BOOK2"))
                : FakeKomgaServer.Json(KomgaJson.BooksPage(1, true, "0BOOK3")))));
        using var serviceLifetime = service;

        var books = await service.GetAllBooksForSeriesAsync("0SER1", pageSize: 2);

        Assert.Equal(new[] { "0BOOK1", "0BOOK2", "0BOOK3" }, books.Select(book => book.Id));
        Assert.Equal(2, komga.Requests.Count);
        Assert.Contains("size=2", komga.Requests[0].Query);
    }

    [Fact]
    public async Task GetBookPagesAsync_ParsesThePages()
    {
        var (service, _) = Create(Routes(("/api/v1/books/0BOOK1/pages", _ => FakeKomgaServer.Json(KomgaJson.BookPages))));
        using var serviceLifetime = service;

        var pages = await service.GetBookPagesAsync("0BOOK1");

        Assert.Equal(2, pages.Count);
        Assert.Equal("page2.png", pages[1].FileName);
        Assert.Equal(450, pages[1].Height);
    }

    [Fact]
    public async Task Thumbnails_ReturnTheBytes_OrNullOnError()
    {
        var thumbnail = TestFiles.PageBytes("page1.jpg");
        var (service, komga) = Create(Routes(
            ("/api/v1/books/0BOOK1/thumbnail", _ => FakeKomgaServer.Bytes(thumbnail)),
            ("/api/v1/series/0SER1/thumbnail", _ => FakeKomgaServer.Bytes(thumbnail)),
            ("/api/v1/readlists/0RL1/thumbnail", _ => FakeKomgaServer.Status(HttpStatusCode.InternalServerError)),
            ("/api/v1/books/0BOOK1/pages/1", _ => FakeKomgaServer.Bytes(thumbnail))));
        using var serviceLifetime = service;

        Assert.Equal(thumbnail, await service.GetBookThumbnailAsync("0BOOK1", TestContext.Current.CancellationToken));
        Assert.Equal(thumbnail, await service.GetSeriesThumbnailAsync("0SER1", TestContext.Current.CancellationToken));
        Assert.Null(await service.GetReadListThumbnailAsync("0RL1", TestContext.Current.CancellationToken));
        Assert.Null(await service.GetBookThumbnailAsync("0MISSING", TestContext.Current.CancellationToken));
        Assert.Equal(thumbnail, await service.GetBookPageAsync("0BOOK1", 1));
        Assert.Null(await service.GetBookPageAsync("0BOOK1", 2));

        // Images are requested with */* (Komga answers 406 for an image request accepting only JSON)
        Assert.Contains("*/*", komga.Requests[0].Header("Accept"));
    }

    [Fact]
    public async Task UpdateReadProgressAsync_SendsAPatchWithTheProgress()
    {
        var (service, komga) = Create(Routes(("/api/v1/books/0BOOK1/read-progress", _ => FakeKomgaServer.Status(HttpStatusCode.NoContent))));
        using var serviceLifetime = service;

        Assert.True(await service.UpdateReadProgressAsync("0BOOK1", 5));
        Assert.True(await service.MarkBookAsReadAsync("0BOOK1", 20));

        Assert.All(komga.Requests, request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("application/json", request.ContentType);
        });
        using var first = JsonDocument.Parse(komga.Requests[0].Body!);
        Assert.Equal(5, first.RootElement.GetProperty("page").GetInt32());
        Assert.False(first.RootElement.GetProperty("completed").GetBoolean());
        using var second = JsonDocument.Parse(komga.Requests[1].Body!);
        Assert.Equal(20, second.RootElement.GetProperty("page").GetInt32());
        Assert.True(second.RootElement.GetProperty("completed").GetBoolean());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UpdateReadProgressAsync_Error_ReturnsFalse(HttpStatusCode statusCode)
    {
        var (service, _) = Create(_ => FakeKomgaServer.Status(statusCode));
        using var serviceLifetime = service;

        Assert.False(await service.UpdateReadProgressAsync("0BOOK1", 5));
        Assert.False(await service.DeleteReadProgressAsync("0BOOK1"));
    }

    [Fact]
    public async Task DeleteReadProgressAsync_SendsADelete()
    {
        var (service, komga) = Create(_ => FakeKomgaServer.Status(HttpStatusCode.NoContent));
        using var serviceLifetime = service;

        Assert.True(await service.DeleteReadProgressAsync("0BOOK1"));

        var request = Assert.Single(komga.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/api/v1/books/0BOOK1/read-progress", request.Path);
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_Success()
    {
        var (service, komga) = Create(Routes(("/api/v1/libraries", _ => FakeKomgaServer.Json(KomgaJson.Libraries))));
        using var serviceLifetime = service;

        Assert.Equal((true, (string?)null), await service.TestConnectionWithDetailsAsync(TestContext.Current.CancellationToken));
        Assert.True(await service.TestConnectionAsync(TestContext.Current.CancellationToken));
        Assert.All(komga.Requests, request => Assert.Equal("/api/v1/libraries", request.Path));
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_ReportsTheStatus()
    {
        var (service, _) = Create(_ => FakeKomgaServer.Status(HttpStatusCode.Unauthorized));
        using var serviceLifetime = service;

        var (success, message) = await service.TestConnectionWithDetailsAsync(TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.StartsWith("401", message);
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_NotFound_HintsAtTheUrl()
    {
        var (service, _) = Create(_ => FakeKomgaServer.Status(HttpStatusCode.NotFound));
        using var serviceLifetime = service;

        var (success, message) = await service.TestConnectionWithDetailsAsync(TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.StartsWith("404", message);
        Assert.Contains("check server URL", message);
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_NetworkError_ReturnsTheMessage()
    {
        var komga = new FakeKomgaServer((_, _) => throw new HttpRequestException("No such host is known"));
        using var service = new KomgaApiService(komga.CreateHandler);
        service.Configure(Server());

        var (success, message) = await service.TestConnectionWithDetailsAsync(TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.Equal("No such host is known", message);
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_Cancelled()
    {
        var komga = new FakeKomgaServer(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return FakeKomgaServer.Status(HttpStatusCode.OK);
        });
        using var service = new KomgaApiService(komga.CreateHandler);
        service.Configure(Server());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var (success, message) = await service.TestConnectionWithDetailsAsync(cancellation.Token);

        Assert.False(success);
        Assert.Equal("Operation cancelled", message);
    }

    /// <summary>
    /// The connection test gives up after 8 seconds (instead of the HttpClient timeout of 2 minutes)
    /// </summary>
    [Fact]
    public async Task TestConnectionWithDetailsAsync_UnresponsiveServer_TimesOut()
    {
        var komga = new FakeKomgaServer(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return FakeKomgaServer.Status(HttpStatusCode.OK);
        });
        using var service = new KomgaApiService(komga.CreateHandler);
        service.Configure(Server());

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var (success, message) = await service.TestConnectionWithDetailsAsync(TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.Contains("timed out", message);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Thumbnail_Cancelled_Throws()
    {
        var komga = new FakeKomgaServer(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return FakeKomgaServer.Status(HttpStatusCode.OK);
        });
        using var service = new KomgaApiService(komga.CreateHandler);
        service.Configure(Server());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetBookThumbnailAsync("0BOOK1", cancellation.Token));
    }

    [Fact]
    public async Task DownloadBookToFileAsync_WritesTheFile()
    {
        var content = await File.ReadAllBytesAsync(TestFiles.Get(TestFiles.CbzWithComicInfo), TestContext.Current.CancellationToken);
        var (service, komga) = Create(Routes(("/api/v1/books/0BOOK1/file", _ => FakeKomgaServer.Bytes(content, "application/zip"))));
        using var serviceLifetime = service;
        using var temp = new TempDirectory("download");
        var outputPath = temp.Combine("downloads", "book.cbz");
        var progress = new RecordingProgress<double>();

        var result = await service.DownloadBookToFileAsync("0BOOK1", outputPath, progress, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(content, await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(outputPath + ".partial"));
        Assert.Equal(1d, progress.Values[^1]);
        Assert.Contains("*/*", komga.Requests[0].Header("Accept"));
    }

    [Fact]
    public async Task DownloadBookToFileAsync_NotFound_Fails()
    {
        var (service, _) = Create(_ => FakeKomgaServer.Status(HttpStatusCode.NotFound));
        using var serviceLifetime = service;
        using var temp = new TempDirectory("download");
        var outputPath = temp.Combine("book.cbz");

        var result = await service.DownloadBookToFileAsync("0BOOK1", outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("404", result.ErrorMessage);
        Assert.False(File.Exists(outputPath));
    }

    private const int DownloadChunkSize = 4 * 1024 * 1024;

    private static byte[] CreateContent(int length, int seed = 42)
    {
        var content = new byte[length];
        new Random(seed).NextBytes(content);
        return content;
    }

    /// <summary>
    /// Serves a book file like Komga: honors Range, answers with the complete file (200) when the If-Range validator
    /// doesn't match the current ETag / Last-Modified.
    /// </summary>
    private static HttpResponseMessage ServeFile(RecordedRequest request, byte[] content, EntityTagHeaderValue? entityTag, DateTimeOffset? lastModified)
    {
        var range = request.Header("Range");
        var ifRange = request.Header("If-Range");
        var validatorMatches = ifRange is null ||
                               (entityTag is not null && !entityTag.IsWeak && ifRange == entityTag.Tag) ||
                               (lastModified is not null && ifRange == lastModified.Value.ToString("R", CultureInfo.InvariantCulture));

        HttpResponseMessage response;
        if (range is not null && validatorMatches)
        {
            var bounds = range["bytes=".Length..].Split('-');
            var start = long.Parse(bounds[0], CultureInfo.InvariantCulture);
            var end = Math.Min(long.Parse(bounds[1], CultureInfo.InvariantCulture), content.Length - 1);
            response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(content, (int)start, (int)(end - start + 1))
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, content.Length);
        }
        else
        {
            response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        }

        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        if (entityTag is not null)
        {
            response.Headers.ETag = entityTag;
        }

        if (lastModified is not null)
        {
            response.Content.Headers.LastModified = lastModified;
        }

        return response;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadBookToFileAsync_LargeFile_StoresAValidatorAndResumesWithIfRange(bool withETag)
    {
        var content = CreateContent(DownloadChunkSize + 1000);
        var entityTag = withETag ? new EntityTagHeaderValue("\"v1\"") : null;
        var lastModified = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        using var temp = new TempDirectory("download");
        var outputPath = temp.Combine("book.cbz");
        var validatorPath = outputPath + ".partial.validator";
        var validatorsSeen = new List<string?>();
        var (service, komga) = Create(request =>
        {
            validatorsSeen.Add(File.Exists(validatorPath) ? File.ReadAllText(validatorPath) : null);
            return ServeFile(request, content, entityTag, lastModified);
        });
        using var serviceLifetime = service;

        var result = await service.DownloadBookToFileAsync("0BOOK1", outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(content, await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.Equal(2, komga.Requests.Count);
        Assert.Equal($"bytes=0-{DownloadChunkSize - 1}", komga.Requests[0].Header("Range"));
        Assert.Null(komga.Requests[0].Header("If-Range"));
        Assert.StartsWith($"bytes={DownloadChunkSize}-", komga.Requests[1].Header("Range"));

        // The strong ETag is preferred, Last-Modified otherwise
        var expectedIfRange = withETag ? "\"v1\"" : lastModified.ToString("R", CultureInfo.InvariantCulture);
        Assert.Equal(expectedIfRange, komga.Requests[1].Header("If-Range"));
        Assert.Null(validatorsSeen[0]);
        Assert.Equal(withETag ? "etag:\"v1\"" : "date:" + expectedIfRange, validatorsSeen[1]?.Trim());

        // Nothing left behind
        Assert.False(File.Exists(outputPath + ".partial"));
        Assert.False(File.Exists(validatorPath));
    }

    [Fact]
    public async Task DownloadBookToFileAsync_WeakETagOnly_StoresNoValidator()
    {
        var content = CreateContent(DownloadChunkSize + 10);
        using var temp = new TempDirectory("download");
        var outputPath = temp.Combine("book.cbz");
        var validatorPath = outputPath + ".partial.validator";
        var validatorExisted = false;
        var (service, komga) = Create(request =>
        {
            validatorExisted |= File.Exists(validatorPath);
            return ServeFile(request, content, new EntityTagHeaderValue("\"weak\"", isWeak: true), null);
        });
        using var serviceLifetime = service;

        var result = await service.DownloadBookToFileAsync("0BOOK1", outputPath, cancellationToken: TestContext.Current.CancellationToken);

        // If-Range only allows strong validators: the download still completes, just without If-Range
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(content, await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.False(validatorExisted);
        Assert.All(komga.Requests, request => Assert.Null(request.Header("If-Range")));
    }

    [Fact]
    public async Task DownloadBookToFileAsync_PartialWithValidator_IsResumed()
    {
        var content = CreateContent(5000);
        using var temp = new TempDirectory("download");
        var outputPath = temp.Combine("book.cbz");
        await File.WriteAllBytesAsync(outputPath + ".partial", content[..1000], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(outputPath + ".partial.validator", "etag:\"v1\"", TestContext.Current.CancellationToken);
        var (service, komga) = Create(request => ServeFile(request, content, new EntityTagHeaderValue("\"v1\""), null));
        using var serviceLifetime = service;

        var result = await service.DownloadBookToFileAsync("0BOOK1", outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(content, await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        var request = Assert.Single(komga.Requests);
        Assert.StartsWith("bytes=1000-", request.Header("Range"));
        Assert.Equal("\"v1\"", request.Header("If-Range"));
        Assert.False(File.Exists(outputPath + ".partial"));
        Assert.False(File.Exists(outputPath + ".partial.validator"));
    }

    [Fact]
    public async Task DownloadBookToFileAsync_PartialWithoutValidator_IsDiscarded()
    {
        var content = CreateContent(5000);
        using var temp = new TempDirectory("download");
        var outputPath = temp.Combine("book.cbz");
        // Left behind by an older version (or a server without ETag/Last-Modified): can't be verified, so not resumed
        await File.WriteAllBytesAsync(outputPath + ".partial", CreateContent(1000, seed: 7), TestContext.Current.CancellationToken);
        var (service, komga) = Create(request => ServeFile(request, content, new EntityTagHeaderValue("\"v1\""), null));
        using var serviceLifetime = service;

        var result = await service.DownloadBookToFileAsync("0BOOK1", outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(content, await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        var request = Assert.Single(komga.Requests);
        Assert.StartsWith("bytes=0-", request.Header("Range"));
        Assert.Null(request.Header("If-Range"));
    }

    [Fact]
    public async Task DownloadBookToFileAsync_FileChangedOnTheServer_RestartsTheDownload()
    {
        var oldContent = CreateContent(5000, seed: 1);
        var newContent = CreateContent(6000, seed: 2);
        using var temp = new TempDirectory("download");
        var outputPath = temp.Combine("book.cbz");
        await File.WriteAllBytesAsync(outputPath + ".partial", oldContent[..1000], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(outputPath + ".partial.validator", "etag:\"old\"", TestContext.Current.CancellationToken);
        var (service, komga) = Create(request => ServeFile(request, newContent, new EntityTagHeaderValue("\"new\""), null));
        using var serviceLifetime = service;

        var result = await service.DownloadBookToFileAsync("0BOOK1", outputPath, cancellationToken: TestContext.Current.CancellationToken);

        // The server ignored the range (200, validator mismatch): the old part is thrown away and the complete file
        // of that same response is used, it isn't requested a second time
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(newContent, await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.Single(komga.Requests);
        Assert.Equal("\"old\"", komga.Requests[0].Header("If-Range"));
        Assert.False(File.Exists(outputPath + ".partial.validator"));
    }

    [Fact]
    public async Task DownloadBookToFileAsync_ConnectionBrokenOnServerWithoutRanges_RestartsWithoutAppending()
    {
        // Komga streams the book file and ignores Range: every response is a 200 with the complete file
        var content = CreateContent(6000, seed: 3);
        using var temp = new TempDirectory("download");
        var outputPath = temp.Combine("book.cbz");
        var requestCount = 0;
        var (service, komga) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(Interlocked.Increment(ref requestCount) == 1
                    ? new BrokenStream(content, breakAfter: 2000)
                    : new MemoryStream(content))
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            response.Content.Headers.ContentLength = content.Length;
            return response;
        });
        using var serviceLifetime = service;

        var result = await service.DownloadBookToFileAsync("0BOOK1", outputPath, cancellationToken: TestContext.Current.CancellationToken);

        // Before, the complete file of the retry was appended to the 2000 bytes of the broken attempt
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(content, await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.Equal(2, komga.Requests.Count);
    }

    /// <summary>
    /// A response body which fails like a dropped connection after a number of bytes
    /// </summary>
    private sealed class BrokenStream(byte[] content, int breakAfter) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= breakAfter)
            {
                throw new IOException("The connection was reset.");
            }

            var read = Math.Min(count, breakAfter - _position);
            Array.Copy(content, _position, buffer, offset, read);
            _position += read;
            return read;
        }
    }

    [Fact]
    public async Task DownloadBookToFileAsync_Cancelled_Throws()
    {
        var (service, _) = Create(_ => FakeKomgaServer.Bytes([1, 2, 3], "application/zip"));
        using var serviceLifetime = service;
        using var temp = new TempDirectory("download");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadBookToFileAsync("0BOOK1", temp.Combine("book.cbz"), cancellationToken: cancellation.Token));
        Assert.False(File.Exists(temp.Combine("book.cbz")));
    }
}
