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
using Xunit;

namespace StripWolf.Core.Tests;

/// <summary>
/// EPUB parsing (VersOne.Epub) is real, the WebView which paginates and renders the chapters is faked.
/// Analyze/ExtractComicInfo don't need the WebView at all.
/// </summary>
public sealed class EpubToCbzConverterServiceTests
{
    [Fact]
    public async Task AnalyzeEpubForImportAsync_ReadsMetadataAndCover_WithoutTheWebView()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var epub = TestFiles.Get(TestFiles.Epub);
        var progress = new RecordingProgress<double>();

        using var importData = await host.EpubConverter.AnalyzeEpubForImportAsync(epub, progress: progress, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(epub, importData.FilePath);
        Assert.Equal(ComicFormat.Epub, importData.Format);
        Assert.Equal(0, importData.PageCount);
        Assert.Equal(new FileInfo(epub).Length, importData.FileSize);
        Assert.NotNull(importData.ComicInfo);
        Assert.Equal(TestFiles.EpubTitle, importData.ComicInfo.Title);
        Assert.Equal(TestFiles.EpubAuthor, importData.ComicInfo.Writer);
        Assert.Equal(TestFiles.EpubDescription, importData.ComicInfo.Summary);
        Assert.Null(importData.ComicInfo.PageCount);
        // The cover is the manifest item with the "cover-image" property
        Assert.NotNull(importData.CoverImageStream);
        Assert.Equal(TestFiles.PageBytes("page1.jpg"), TestFiles.ReadAll(importData.CoverImageStream));
        Assert.NotEmpty(progress.Values);
        Assert.Empty(host.WebView.Sessions);
    }

    [Fact]
    public async Task ExtractComicInfoAsync_ReadsTheEpubMetadata()
    {
        await using var host = await TestServiceHost.CreateAsync();

        var comicInfo = await host.EpubConverter.ExtractComicInfoAsync(TestFiles.Get(TestFiles.Epub), TestContext.Current.CancellationToken);

        Assert.NotNull(comicInfo);
        Assert.Equal(TestFiles.EpubTitle, comicInfo.Title);
        Assert.Equal(TestFiles.EpubAuthor, comicInfo.Writer);
        Assert.Equal(TestFiles.EpubDescription, comicInfo.Summary);
        Assert.Empty(host.WebView.Sessions);
        await Assert.ThrowsAsync<FileNotFoundException>(() => host.EpubConverter.ExtractComicInfoAsync(host.Temp.Combine("missing.epub"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConvertEpubToCbzForImportAsync_RendersEveryChapter()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var outputDirectory = host.Temp.Combine("out");
        var progress = new RecordingProgress<double>();

        using var importData = await host.EpubConverter.ConvertEpubToCbzForImportAsync(
            TestFiles.Get(TestFiles.Epub), outputDirectory, progress: progress, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(outputDirectory, "comic.cbz"), importData.FilePath);
        Assert.Equal(ComicFormat.Cbz, importData.Format);
        Assert.Equal(4, importData.PageCount);
        Assert.Equal(new FileInfo(importData.FilePath).Length, importData.FileSize);
        Assert.Equal(TestFiles.EpubTitle, importData.ComicInfo?.Title);
        Assert.Equal(4, importData.ComicInfo?.PageCount);
        Assert.NotNull(importData.CoverImageStream);
        Assert.Equal(host.WebView.PageImage, TestFiles.ReadAll(importData.CoverImageStream));

        var entries = TestFiles.ZipEntryNames(importData.FilePath).Order(StringComparer.Ordinal);
        Assert.Equal(new[] { "ComicInfo.xml", "Page_001.png", "Page_002.png", "Page_003.png", "Page_004.png" }, entries);
        Assert.Equal(host.WebView.PageImage, TestFiles.ReadZipEntry(importData.FilePath, "Page_003.png"));

        var storedComicInfo = await host.ComicConverter.ExtractComicInfoAsync(importData.FilePath);
        Assert.Equal(TestFiles.EpubTitle, storedComicInfo?.Title);
        Assert.Equal(4, storedComicInfo?.PageCount);

        // One WebView session for the whole book with the default viewport, disposed at the end
        var session = Assert.Single(host.WebView.Sessions);
        Assert.Equal(700, session.ViewportWidth);
        Assert.Equal(1050, session.ViewportHeight);
        Assert.Equal(1d, session.RenderScale);
        Assert.True(session.IsDisposed);
        Assert.Equal(4, session.LoadedHtml.Count);

        Assert.Equal(1d, progress.Values[^1]);
    }

    [Fact]
    public async Task ConvertEpubToCbzForImportAsync_ExtractsTheResourcesAndSanitizesTheHtml()
    {
        await using var host = await TestServiceHost.CreateAsync();

        using var importData = await host.EpubConverter.ConvertEpubToCbzForImportAsync(
            TestFiles.Get(TestFiles.Epub), host.Temp.Combine("out"), cancellationToken: TestContext.Current.CancellationToken);

        var session = Assert.Single(host.WebView.Sessions);
        // The images referenced by the chapters existed on disk (relative to the injected <base href>) while rendering
        Assert.Empty(session.MissingResources);

        Assert.All(session.LoadedHtml, html =>
        {
            Assert.Contains("<base href=\"file:", html);
            Assert.Contains("Content-Security-Policy", html);
            Assert.Contains("background: #ffffff", html);
        });

        // Chapter 2 contains a <script>, an onclick handler and a javascript: link
        var chapter2 = Assert.Single(session.LoadedHtml, html => html.Contains("page2.png", StringComparison.Ordinal));
        Assert.DoesNotContain("stripwolf-script", chapter2);
        Assert.DoesNotContain("onclick", chapter2, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stripwolf-onclick", chapter2);
        Assert.DoesNotContain("javascript:", chapter2, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(EpubConversionTheme.Light, "#ffffff")]
    [InlineData(EpubConversionTheme.Dark, "#000000")]
    public async Task ConvertEpubToCbzForImportAsync_UsesTheConfiguredTheme(EpubConversionTheme theme, string expectedBackground)
    {
        await using var host = await TestServiceHost.CreateAsync(settings => settings.EpubConversionTheme = theme);

        using var importData = await host.EpubConverter.ConvertEpubToCbzForImportAsync(
            TestFiles.Get(TestFiles.Epub), host.Temp.Combine("out"), cancellationToken: TestContext.Current.CancellationToken);

        var session = Assert.Single(host.WebView.Sessions);
        Assert.All(session.LoadedHtml, html => Assert.Contains($"background: {expectedBackground}", html));
    }

    [Theory]
    [InlineData(EpubOutputResolution.Low, 1d)]
    [InlineData(EpubOutputResolution.Medium, 2d)]
    [InlineData(EpubOutputResolution.High, 3d)]
    public async Task ConvertEpubToCbzForImportAsync_UsesTheConfiguredResolution(EpubOutputResolution resolution, double expectedScale)
    {
        await using var host = await TestServiceHost.CreateAsync(settings => settings.EpubOutputResolution = resolution);

        using var importData = await host.EpubConverter.ConvertEpubToCbzForImportAsync(
            TestFiles.Get(TestFiles.Epub), host.Temp.Combine("out"), viewportWidth: 640, viewportHeight: 960, cancellationToken: TestContext.Current.CancellationToken);

        var session = Assert.Single(host.WebView.Sessions);
        Assert.Equal(expectedScale, session.RenderScale);
        Assert.Equal(640, session.ViewportWidth);
        Assert.Equal(960, session.ViewportHeight);
    }

    [Fact]
    public async Task ConvertEpubToCbzForImportAsync_MultiPageAndEmptyChapters()
    {
        await using var host = await TestServiceHost.CreateAsync();
        // Chapter 3 (the double page spread) needs two pages, chapter 2 none at all
        host.WebView.PageCountForHtml = html =>
            html.Contains("page3-4.jpg", StringComparison.Ordinal) ? 2 :
            html.Contains("page2.png", StringComparison.Ordinal) ? 0 : 1;

        using var importData = await host.EpubConverter.ConvertEpubToCbzForImportAsync(
            TestFiles.Get(TestFiles.Epub), host.Temp.Combine("out"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, importData.PageCount);
        Assert.Equal(new[] { "ComicInfo.xml", "Page_001.png", "Page_002.png", "Page_003.png", "Page_004.png" },
            TestFiles.ZipEntryNames(importData.FilePath).Order(StringComparer.Ordinal));
        var session = Assert.Single(host.WebView.Sessions);
        var chapter3Load = session.LoadedHtml.ToList().FindIndex(html => html.Contains("page3-4.jpg", StringComparison.Ordinal));
        Assert.Equal(new[] { 0, 1 }, session.Captures.Where(capture => capture.LoadIndex == chapter3Load).Select(capture => capture.PageIndex));
    }

    [Fact]
    public async Task ConvertEpubToCbzForImportAsync_InvalidInput_Throws()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var notAnEpub = host.Temp.CopyFixture(TestFiles.Epub, "book.zip");
        var outputDirectory = host.Temp.Combine("out");

        await Assert.ThrowsAsync<FileNotFoundException>(() => host.EpubConverter.ConvertEpubToCbzForImportAsync(host.Temp.Combine("missing.epub"), outputDirectory));
        await Assert.ThrowsAsync<NotSupportedException>(() => host.EpubConverter.ConvertEpubToCbzForImportAsync(notAnEpub, outputDirectory));
        await Assert.ThrowsAsync<NotSupportedException>(() => host.EpubConverter.CreateReaderSessionAsync(notAnEpub));
    }

    [Fact]
    public async Task ConvertEpubToCbzForImportAsync_Cancelled_Throws()
    {
        await using var host = await TestServiceHost.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.EpubConverter.ConvertEpubToCbzForImportAsync(TestFiles.Get(TestFiles.Epub), host.Temp.Combine("out"), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ReaderSession_RendersPagesOnDemand()
    {
        await using var host = await TestServiceHost.CreateAsync();
        host.WebView.PageCountForHtml = html => html.Contains("page3-4.jpg", StringComparison.Ordinal) ? 2 : 1;

        var readerSession = await host.EpubConverter.CreateReaderSessionAsync(TestFiles.Get(TestFiles.Epub), cancellationToken: TestContext.Current.CancellationToken);
        var webViewSession = Assert.Single(host.WebView.Sessions);
        try
        {
            // Pages: chapter 1, chapter 2, chapter 3 (2 pages), chapter 4
            Assert.Equal(5, readerSession.PageCount);
            Assert.Equal(TestFiles.EpubTitle, readerSession.ComicInfo.Title);
            Assert.Equal(5, readerSession.ComicInfo.PageCount);
            var loadsAfterPagination = webViewSession.LoadedHtml.Count;

            using var page = new MemoryStream();
            await readerSession.RenderPageToStreamAsync(3, page, TestContext.Current.CancellationToken);
            Assert.Equal(host.WebView.PageImage, page.ToArray());
            Assert.Equal(loadsAfterPagination + 1, webViewSession.LoadedHtml.Count);
            Assert.Contains("page3-4.jpg", webViewSession.LoadedHtml[^1]);
            Assert.Equal(1, webViewSession.Captures[^1].PageIndex);

            // Same chapter: no reload
            await readerSession.RenderPageToStreamAsync(2, Stream.Null, TestContext.Current.CancellationToken);
            Assert.Equal(loadsAfterPagination + 1, webViewSession.LoadedHtml.Count);
            Assert.Equal(0, webViewSession.Captures[^1].PageIndex);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => readerSession.RenderPageToStreamAsync(5, Stream.Null));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => readerSession.RenderPageToStreamAsync(-1, Stream.Null));
        }
        finally
        {
            await readerSession.DisposeAsync();
        }

        Assert.True(webViewSession.IsDisposed);
    }

    [Fact]
    public async Task ReaderSession_DisposeWaitsForTheRunningRender_LaterRendersThrow()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var readerSession = await host.EpubConverter.CreateReaderSessionAsync(TestFiles.Get(TestFiles.Epub), cancellationToken: TestContext.Current.CancellationToken);
        var webViewSession = Assert.Single(host.WebView.Sessions);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.WebView.RenderGate = gate;
        try
        {
            using var page = new MemoryStream();
            var render = readerSession.RenderPageToStreamAsync(0, page, TestContext.Current.CancellationToken);
            await host.WebView.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            var dispose = readerSession.DisposeAsync().AsTask();
            await Task.Delay(200, TestContext.Current.CancellationToken);

            // The WebView session isn't torn down underneath the running capture
            Assert.False(dispose.IsCompleted);
            Assert.False(webViewSession.IsDisposed);

            gate.SetResult();
            await render.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await dispose.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal(host.WebView.PageImage, page.ToArray());
            Assert.True(webViewSession.IsDisposed);
            Assert.False(webViewSession.DisposedDuringCapture);

            await Assert.ThrowsAsync<ObjectDisposedException>(() => readerSession.RenderPageToStreamAsync(1, Stream.Null));
            // Disposing twice is harmless
            await readerSession.DisposeAsync();
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task IncrementalConversionSession_RendersOnePageAfterTheOther()
    {
        await using var host = await TestServiceHost.CreateAsync();

        var session = await host.EpubConverter.CreateIncrementalConversionSessionAsync(TestFiles.Get(TestFiles.Epub), cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            var results = new List<EpubToCbzConverterService.EpubIncrementalPageResult>();
            while (await session.RenderNextPageToStreamAsync(Stream.Null, TestContext.Current.CancellationToken) is { } result)
            {
                results.Add(result);
                Assert.True(results.Count <= 10, "The session doesn't end");
            }

            Assert.Equal(new[] { 0, 1, 2, 3 }, results.Select(result => result.ChapterIndex));
            Assert.All(results, result => Assert.Equal(0, result.PageIndexInChapter));
            Assert.Equal(new[] { true, true, true, false }, results.Select(result => result.HasMorePages));
            Assert.Equal(4, session.NextChapterIndex);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task IncrementalConversionSession_ResumesAtTheGivenChapter()
    {
        await using var host = await TestServiceHost.CreateAsync();

        var session = await host.EpubConverter.CreateIncrementalConversionSessionAsync(TestFiles.Get(TestFiles.Epub), nextChapterIndex: 2, cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            var first = await session.RenderNextPageToStreamAsync(Stream.Null, TestContext.Current.CancellationToken);

            Assert.NotNull(first);
            Assert.Equal(2, first.ChapterIndex);
            Assert.Equal(0, first.PageIndexInChapter);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
