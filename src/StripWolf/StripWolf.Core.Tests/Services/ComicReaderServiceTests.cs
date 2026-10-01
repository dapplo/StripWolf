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

public sealed class ComicReaderServiceTests
{
    public static TheoryData<string> StandardArchives()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in TestFiles.StandardArchives)
        {
            data.Add(fixture);
        }

        return data;
    }

    public static TheoryData<string> SolidArchives() => new() { TestFiles.Cb7Solid, TestFiles.CbrSolid };

    [Theory]
    [InlineData("comic.cbz", ComicFormat.Cbz)]
    [InlineData("COMIC.CBZ", ComicFormat.Cbz)]
    [InlineData("comic.cbr", ComicFormat.Cbr)]
    [InlineData("comic.cb7", ComicFormat.Cb7)]
    [InlineData("comic.cbt", ComicFormat.Cbt)]
    [InlineData("comic.pdf", ComicFormat.Pdf)]
    [InlineData("comic.epub", ComicFormat.Epub)]
    [InlineData("folder/comic.Cb7", ComicFormat.Cb7)]
    [InlineData("comic.zip", ComicFormat.Unknown)]
    [InlineData("comic.cba", ComicFormat.Unknown)]
    [InlineData("comic", ComicFormat.Unknown)]
    public void GetComicFormat_UsesTheExtension(string fileName, ComicFormat expected)
    {
        Assert.Equal(expected, ComicReaderService.GetComicFormat(fileName));
    }

    [Theory]
    [MemberData(nameof(StandardArchives))]
    public async Task GetPageNamesAsync_ReturnsOnlyImages_InNaturalOrder(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();

        var names = await host.Reader.GetPageNamesAsync(TestFiles.Get(fixture));

        // Thumbs.db, notes.txt, ComicInfo.xml and __MACOSX/._page1.jpg are filtered, page10 sorts after page3-4
        Assert.Equal(TestFiles.ExpectedPageNames, names);
    }

    [Theory]
    [MemberData(nameof(StandardArchives))]
    public async Task GetPageNamesWithoutCacheAsync_ReturnsTheSameNames(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();

        var names = await host.Reader.GetPageNamesWithoutCacheAsync(TestFiles.Get(fixture));

        Assert.Equal(TestFiles.ExpectedPageNames, names);
    }

    [Fact]
    public async Task GetPageNamesAsync_SortsSubfoldersNaturally()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.CbzWithSubfolders);

        var names = await host.Reader.GetPageNamesAsync(path);

        Assert.Equal(TestFiles.SubfolderPages.Select(page => page.Name), names);
        for (var index = 0; index < names.Count; index++)
        {
            Assert.Equal(TestFiles.PageBytes(TestFiles.SubfolderPages[index].Page), await host.Reader.GetPageAsync(path, index));
        }
    }

    [Theory]
    [MemberData(nameof(StandardArchives))]
    public async Task GetPageAsync_ReturnsTheBytesOfEveryPage(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(fixture);

        for (var index = 0; index < TestFiles.ExpectedPageNames.Length; index++)
        {
            var data = await host.Reader.GetPageAsync(path, index);
            Assert.Equal(TestFiles.PageBytes(TestFiles.ExpectedPageNames[index]), data);
        }
    }

    [Theory]
    [MemberData(nameof(StandardArchives))]
    public async Task GetPageWithoutCacheAsync_ReturnsTheBytesOfEveryPage(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(fixture);

        // Backwards, to make sure there is no dependency on reading sequentially
        for (var index = TestFiles.ExpectedPageNames.Length - 1; index >= 0; index--)
        {
            var data = await host.Reader.GetPageWithoutCacheAsync(path, index);
            Assert.Equal(TestFiles.PageBytes(TestFiles.ExpectedPageNames[index]), data);
        }
    }

    [Theory]
    [MemberData(nameof(StandardArchives))]
    public async Task CopyPageAsync_WritesThePageToTheStream(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(fixture);

        using var cached = new MemoryStream();
        await host.Reader.CopyPageAsync(path, 2, cached);
        using var uncached = new MemoryStream();
        await host.Reader.CopyPageWithoutCacheAsync(path, 3, uncached);

        Assert.Equal(TestFiles.PageBytes("page3-4.jpg"), cached.ToArray());
        Assert.Equal(TestFiles.PageBytes("page10.jpg"), uncached.ToArray());
    }

    [Theory]
    [MemberData(nameof(SolidArchives))]
    public async Task GetPageAsync_SolidArchive_RandomAccessReturnsTheRightPages(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(fixture);

        // Solid archives are read sequentially and pages around the requested one are put in the cache:
        // jumping around must still return the right page every time
        foreach (var index in new[] { 3, 0, 2, 1, 3, 0, 1 })
        {
            var data = await host.Reader.GetPageAsync(path, index);
            Assert.Equal(TestFiles.PageBytes(TestFiles.ExpectedPageNames[index]), data);
        }
    }

    [Theory]
    [MemberData(nameof(StandardArchives))]
    public async Task GetPageAsync_OutOfRange_Throws(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(fixture);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.Reader.GetPageAsync(path, -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.Reader.GetPageAsync(path, TestFiles.ExpectedPageNames.Length));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.Reader.GetPageWithoutCacheAsync(path, 99));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.Reader.CopyPageAsync(path, 4, Stream.Null));
    }

    [Theory]
    [MemberData(nameof(StandardArchives))]
    public async Task GetComicInfoAsync_ReturnsPageCountAndFileSize(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(fixture);

        var (pageCount, fileSize) = await host.Reader.GetComicInfoAsync(path);
        var (pageCountWithoutCache, fileSizeWithoutCache) = await host.Reader.GetComicInfoWithoutCacheAsync(path);

        Assert.Equal(4, pageCount);
        Assert.Equal(new FileInfo(path).Length, fileSize);
        Assert.Equal(4, pageCountWithoutCache);
        Assert.Equal(fileSize, fileSizeWithoutCache);
    }

    [Fact]
    public async Task GetComicInfoAsync_MissingFile_ThrowsFileNotFound()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.Combine("missing.cbz");

        await Assert.ThrowsAsync<FileNotFoundException>(() => host.Reader.GetComicInfoAsync(path));
        await Assert.ThrowsAsync<FileNotFoundException>(() => host.Reader.GetComicInfoWithoutCacheAsync(path));
    }

    [Fact]
    public async Task UnsupportedFormat_ThrowsNotSupported()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.Combine("document.txt");
        await File.WriteAllTextAsync(path, "not a comic", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<NotSupportedException>(() => host.Reader.GetPageNamesAsync(path));
        await Assert.ThrowsAsync<NotSupportedException>(() => host.Reader.GetPageAsync(path, 0));
    }

    [Fact]
    public async Task CorruptArchive_Throws()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.Combine("corrupt.cbz");
        await File.WriteAllBytesAsync(path, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0x00, 0x01, 0x02 }, TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => host.Reader.GetPageNamesAsync(path));
    }

    [Fact]
    public async Task GetPageAsync_UsesTheCache_UntilClearCache()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "cached.cbz");

        var firstPage = await host.Reader.GetPageAsync(path, 0);
        var names = await host.Reader.GetPageNamesAsync(path);

        // Replace the file behind the reader's back
        TestFiles.CreateZip(path, ("other.jpg", TestFiles.PageBytes("page10.jpg")));

        Assert.Equal(firstPage, await host.Reader.GetPageAsync(path, 0));
        Assert.Same(names, await host.Reader.GetPageNamesAsync(path));

        await host.Reader.ClearCacheAsync();

        Assert.Equal(new[] { "other.jpg" }, await host.Reader.GetPageNamesAsync(path));
        Assert.Equal(TestFiles.PageBytes("page10.jpg"), await host.Reader.GetPageAsync(path, 0));
    }

    [Fact]
    public async Task WithoutCacheMethods_DoNotPopulateTheCache()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "uncached.cbz");

        await host.Reader.GetPageNamesWithoutCacheAsync(path);
        await host.Reader.GetPageWithoutCacheAsync(path, 0);
        await host.Reader.GetComicInfoWithoutCacheAsync(path);

        TestFiles.CreateZip(path, ("other.jpg", TestFiles.PageBytes("page10.jpg")));

        Assert.Equal(new[] { "other.jpg" }, await host.Reader.GetPageNamesAsync(path));
        Assert.Equal(TestFiles.PageBytes("page10.jpg"), await host.Reader.GetPageAsync(path, 0));
    }

    [Fact]
    public async Task Directory_ReadsPageFilesInNaturalOrder()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var directory = host.Temp.CreateDirectory("shadow");
        await File.WriteAllBytesAsync(Path.Combine(directory, "Page_10.png"), TestFiles.PageBytes("page10.jpg"), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, "Page_2.png"), TestFiles.PageBytes("page2.png"), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, "Page_1.png"), TestFiles.PageBytes("page1.jpg"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "ComicInfo.xml"), "<ComicInfo/>", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, "cover.png"), TestFiles.PageBytes("page2.png"), TestContext.Current.CancellationToken);

        var names = await host.Reader.GetPageNamesAsync(directory);
        var (pageCount, fileSize) = await host.Reader.GetComicInfoAsync(directory);

        Assert.Equal(new[] { "Page_1.png", "Page_2.png", "Page_10.png" }, names);
        Assert.Equal(3, pageCount);
        Assert.Equal(0, fileSize);
        Assert.Equal(TestFiles.PageBytes("page10.jpg"), await host.Reader.GetPageAsync(directory, 2));
    }

    [Fact]
    public async Task ExtractCoverAsync_WritesTheFirstPage()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var outputDirectory = host.Temp.CreateDirectory("cover");

        var coverPath = await host.Reader.ExtractCoverAsync(TestFiles.Get(TestFiles.CbrSolid), outputDirectory);

        Assert.Equal(Path.Combine(outputDirectory, "cover.jpg"), coverPath);
        Assert.Equal(TestFiles.PageBytes("page1.jpg"), await File.ReadAllBytesAsync(coverPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Pdf_IsRenderedWithThePdfRenderer()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Pdf);

        var names = await host.Reader.GetPageNamesAsync(path);
        var (pageCount, fileSize) = await host.Reader.GetComicInfoAsync(path);
        var secondPage = await host.Reader.GetPageAsync(path, 1);

        Assert.Equal(new[] { "Page_00001.jpg", "Page_00002.jpg", "Page_00003.jpg" }, names);
        Assert.Equal(3, pageCount);
        Assert.Equal(new FileInfo(path).Length, fileSize);
        Assert.Equal(TestFiles.PageBytes(TestFiles.FakePdfPages[1]), secondPage);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.Reader.GetPageAsync(path, 3));
    }

    [Fact]
    public async Task Pdf_ReusesOneRenderSession_AndClearCacheDisposesIt()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Pdf);

        await host.Reader.GetPageNamesAsync(path);
        await host.Reader.GetPageAsync(path, 0);
        await host.Reader.GetPageAsync(path, 2);

        var session = Assert.Single(host.PdfRenderer.Sessions);
        Assert.False(session.IsDisposed);

        await host.Reader.ClearCacheAsync();

        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task Pdf_WithoutCache_UsesThePageCountOnly()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Pdf);

        var (pageCount, fileSize) = await host.Reader.GetComicInfoWithoutCacheAsync(path);
        var names = await host.Reader.GetPageNamesWithoutCacheAsync(path);

        Assert.Equal(3, pageCount);
        Assert.Equal(new FileInfo(path).Length, fileSize);
        Assert.Equal(3, names.Count);
        Assert.Empty(host.PdfRenderer.Sessions);
    }

    [Fact]
    public async Task Epub_IsPaginatedWithTheWebView()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Epub);

        var names = await host.Reader.GetPageNamesAsync(path);
        var (pageCount, fileSize) = await host.Reader.GetComicInfoAsync(path);
        var page = await host.Reader.GetPageAsync(path, 2);

        // One page per spine item (the fake WebView returns one page per chapter)
        Assert.Equal(new[] { "Page_00001.png", "Page_00002.png", "Page_00003.png", "Page_00004.png" }, names);
        Assert.Equal(4, pageCount);
        Assert.Equal(new FileInfo(path).Length, fileSize);
        Assert.Equal(host.WebView.PageImage, page);

        var session = Assert.Single(host.WebView.Sessions);
        await host.Reader.ClearCacheAsync();
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task Epub_WithoutCache_DisposesTheSession()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Epub);

        var (pageCount, _) = await host.Reader.GetComicInfoWithoutCacheAsync(path);

        Assert.Equal(4, pageCount);
        Assert.All(host.WebView.Sessions, session => Assert.True(session.IsDisposed));
    }

    [Fact]
    public async Task Pdf_RenderedPagesAreCached()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Pdf);

        var first = await host.Reader.GetPageAsync(path, 1);
        var second = await host.Reader.GetPageAsync(path, 1);

        Assert.Equal(first, second);
        // Rendering is the expensive part: the second request is served from the cache (on every OS)
        Assert.Equal(new[] { 1 }, Assert.Single(host.PdfRenderer.Sessions).RenderedPages);
    }

    [Fact]
    public async Task Epub_RenderedPagesAreCached()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Epub);

        await host.Reader.GetPageAsync(path, 3);
        await host.Reader.GetPageAsync(path, 3);

        Assert.Single(Assert.Single(host.WebView.Sessions).Captures);
    }

    [Fact]
    public async Task Pdf_ClearCacheAsync_WaitsForTheRunningRender()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Pdf);
        await host.Reader.GetPageNamesAsync(path);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.PdfRenderer.RenderGate = gate;
        try
        {
            var render = host.Reader.GetPageAsync(path, 1);
            await host.PdfRenderer.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var session = Assert.Single(host.PdfRenderer.Sessions);

            var clear = host.Reader.ClearCacheAsync();
            await Task.Delay(200, TestContext.Current.CancellationToken);

            // The session is only closed after the render running in it has finished
            Assert.False(clear.IsCompleted);
            Assert.False(session.IsDisposed);

            gate.SetResult();
            Assert.Equal(TestFiles.PageBytes(TestFiles.FakePdfPages[1]), await render);
            await clear.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.True(session.IsDisposed);
            Assert.False(session.DisposedDuringRender);

            // Reading again opens a new session
            Assert.Equal(TestFiles.PageBytes(TestFiles.FakePdfPages[2]), await host.Reader.GetPageAsync(path, 2));
            Assert.Equal(2, host.PdfRenderer.Sessions.Count);
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task Epub_ClearCacheAsync_WaitsForTheRunningRender()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Epub);
        await host.Reader.GetPageNamesAsync(path);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.WebView.RenderGate = gate;
        try
        {
            var render = host.Reader.GetPageAsync(path, 0);
            await host.WebView.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var session = Assert.Single(host.WebView.Sessions);

            var clear = host.Reader.ClearCacheAsync();
            await Task.Delay(200, TestContext.Current.CancellationToken);

            Assert.False(clear.IsCompleted);
            Assert.False(session.IsDisposed);

            gate.SetResult();
            Assert.Equal(host.WebView.PageImage, await render);
            await clear.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.True(session.IsDisposed);
            Assert.False(session.DisposedDuringCapture);
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task ClearCache_DisposesTheSessionsInTheBackground()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(TestFiles.Pdf);
        await host.Reader.GetPageAsync(path, 0);
        var session = Assert.Single(host.PdfRenderer.Sessions);

        host.Reader.ClearCache();

        // Nothing is rendering, the background disposal finishes right away
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!session.IsDisposed && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(session.IsDisposed);
    }

    [Theory]
    [MemberData(nameof(SolidArchives))]
    public async Task SolidArchive_CachesTheFollowingPages(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.CopyFixture(fixture, "solid" + Path.GetExtension(fixture));

        Assert.Equal(TestFiles.PageBytes("page1.jpg"), await host.Reader.GetPageAsync(path, 0));

        // Destroy the archive: the next 3 pages must come from the cache now
        await File.WriteAllTextAsync(path, "not an archive anymore", TestContext.Current.CancellationToken);
        for (var index = 1; index <= 3; index++)
        {
            Assert.Equal(TestFiles.PageBytes(TestFiles.ExpectedPageNames[index]), await host.Reader.GetPageAsync(path, index));
        }
    }

    [Theory]
    [MemberData(nameof(SolidArchives))]
    public async Task SolidArchive_CachesThePreviousPage_NotTheOnesBefore(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.CopyFixture(fixture, "solid" + Path.GetExtension(fixture));

        await host.Reader.GetPageAsync(path, 2);
        await File.WriteAllTextAsync(path, "not an archive anymore", TestContext.Current.CancellationToken);

        Assert.Equal(TestFiles.PageBytes(TestFiles.ExpectedPageNames[1]), await host.Reader.GetPageAsync(path, 1));
        Assert.Equal(TestFiles.PageBytes(TestFiles.ExpectedPageNames[3]), await host.Reader.GetPageAsync(path, 3));
        await Assert.ThrowsAnyAsync<Exception>(() => host.Reader.GetPageAsync(path, 0));
    }

    [Theory]
    [MemberData(nameof(SolidArchives))]
    public async Task SolidArchive_WithoutCache_DoesNotReadAhead(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.CopyFixture(fixture, "solid" + Path.GetExtension(fixture));

        Assert.Equal(TestFiles.PageBytes("page1.jpg"), await host.Reader.GetPageWithoutCacheAsync(path, 0));
        await File.WriteAllTextAsync(path, "not an archive anymore", TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => host.Reader.GetPageAsync(path, 1));
    }
}
