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
using System.Text;
using StripWolf.Core.Models;
using StripWolf.Core.Models.Komga;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

/// <summary>
/// LibraryService with the real database/settings/converters in a temporary app data directory and fakes for the PDF
/// renderer and the WebView.
/// </summary>
public sealed class LibraryServiceTests
{
    [Fact]
    public async Task ImportLocalComicAsync_Cbz_UsesTheComicInfo()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var source = host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, Path.Combine("external", "wolf.cbz"));
        var libraryChanged = 0;
        host.Library.LibraryChanged += (_, _) => Interlocked.Increment(ref libraryChanged);

        var comic = await host.Library.ImportLocalComicAsync(source);

        Assert.True(comic.Id > 0);
        Assert.Equal(TestFiles.ComicInfoTitle, comic.Title);
        Assert.Equal(TestFiles.ComicInfoSeries, comic.SeriesName);
        Assert.Equal(7f, comic.Number!.Value);
        Assert.Equal("A synthetic comic used by the automated tests.", comic.Summary);
        Assert.Equal("Dapplo Test Press", comic.Publisher);
        Assert.Equal("Alice Writer, Bob Penciller", comic.Authors);
        Assert.Equal(new DateTime(2024, 5, 17), comic.ReleaseDate!.Value);
        Assert.Equal(4, comic.PageCount);
        Assert.Equal(new FileInfo(source).Length, comic.FileSize);
        Assert.Equal(ComicFormat.Cbz, comic.Format);
        Assert.Equal(ComicSource.Local, comic.Source);
        // A CBZ is read in place, not copied
        Assert.Equal(source, comic.FilePath);
        Assert.True(File.Exists(source));
        Assert.Equal(1, libraryChanged);

        Assert.NotNull(comic.CoverPath);
        Assert.True(File.Exists(comic.CoverPath));
        Assert.StartsWith(Path.Combine(host.AppDataDirectory, "Covers"), comic.CoverPath);

        var stored = await host.Database.GetComicByFilePathAsync(source);
        Assert.Equal(comic.Id, stored?.Id);
    }

    [Fact]
    public async Task ImportLocalComicAsync_WithoutComicInfo_UsesFileNameAndSeriesFallback()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var source = host.Temp.CopyFixture(TestFiles.CbzWithoutComicInfo, Path.Combine("external", "My Comic 01.cbz"));

        var comic = await host.Library.ImportLocalComicAsync(source, seriesNameFallback: "  My \t Series  ");

        Assert.Equal("My Comic 01", comic.Title);
        Assert.Equal("My Series", comic.SeriesName);
        Assert.Null(comic.Number);
        Assert.Equal(4, comic.PageCount);
    }

    [Fact]
    public async Task ImportLocalComicAsync_SameFileTwice_ReturnsTheExistingComic()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var source = host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, Path.Combine("external", "wolf.cbz"));

        var first = await host.Library.ImportLocalComicAsync(source);
        var second = await host.Library.ImportLocalComicAsync(source);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await host.Library.GetAllComicsAsync());
    }

    [Fact]
    public async Task ImportLocalComicAsync_UnsupportedFormat_Throws()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var source = host.Temp.Combine("notes.txt");
        await File.WriteAllTextAsync(source, "not a comic", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<NotSupportedException>(() => host.Library.ImportLocalComicAsync(source));
        Assert.Empty(await host.Library.GetAllComicsAsync());
    }

    [Fact]
    public async Task ImportLocalComicAsync_SolidCbr_IsConvertedToCbz()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var source = host.Temp.CopyFixture(TestFiles.CbrSolid, Path.Combine("external", "solid.cbr"));

        var comic = await host.Library.ImportLocalComicAsync(source);

        Assert.Equal(ComicFormat.Cbz, comic.Format);
        Assert.Equal(Path.Combine(host.Library.ComicsDirectory, "solid.cbz"), comic.FilePath);
        Assert.Equal(TestFiles.ComicInfoTitle, comic.Title);
        Assert.Equal(4, comic.PageCount);
        Assert.Equal(TestFiles.ExpectedPageNames, await host.Reader.GetPageNamesAsync(comic.FilePath));
        // The original file outside of the app data directory is kept
        Assert.True(File.Exists(source));
    }

    /// <summary>
    /// Solid CB7 is converted to CBZ at import, like solid CBR (reading a page needs everything before it decompressed).
    /// Non-solid CBR/CB7 support random access and CBT is uncompressed: those are read in place.
    /// </summary>
    [Theory]
    [InlineData(TestFiles.Cbr, ComicFormat.Cbr)]
    [InlineData(TestFiles.Cb7, ComicFormat.Cb7)]
    [InlineData(TestFiles.Cb7Solid, ComicFormat.Cbz)]
    [InlineData(TestFiles.Cbt, ComicFormat.Cbt)]
    public async Task ImportLocalComicAsync_OtherArchives(string fixture, ComicFormat expectedFormat)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var source = host.Temp.CopyFixture(fixture, Path.Combine("external", fixture));

        var comic = await host.Library.ImportLocalComicAsync(source);

        Assert.Equal(expectedFormat, comic.Format);
        if (expectedFormat == ComicFormat.Cbz)
        {
            Assert.Equal(Path.Combine(host.Library.ComicsDirectory, Path.GetFileNameWithoutExtension(fixture) + ".cbz"), comic.FilePath);
        }
        else
        {
            Assert.Equal(source, comic.FilePath);
        }

        // The external original is never touched
        Assert.True(File.Exists(source));
        Assert.Equal(TestFiles.ComicInfoTitle, comic.Title);
        Assert.Equal(4, comic.PageCount);
        Assert.True(File.Exists(comic.FilePath));
        Assert.Equal(TestFiles.ExpectedPageNames, await host.Reader.GetPageNamesAsync(comic.FilePath));
        Assert.Equal(TestFiles.PageBytes("page10.jpg"), await host.Reader.GetPageAsync(comic.FilePath, 3));
        Assert.NotNull(comic.CoverPath);
    }

    private static KomgaBook CreateKomgaBook(string mediaType, long sizeBytes) => new()
    {
        Id = "0BOOK1",
        SeriesId = "0SER1",
        SeriesTitle = "Wolf Strips",
        Name = "Wolf Strips 001",
        Number = 1,
        SizeBytes = sizeBytes,
        Media = new KomgaMedia { MediaType = mediaType, PagesCount = 4 }
    };

    [Fact]
    public async Task DownloadFromKomgaAsync_Cb7_IsConvertedAndNamedWithTheBookId()
    {
        var archive = await File.ReadAllBytesAsync(TestFiles.Get(TestFiles.Cb7Solid), TestContext.Current.CancellationToken);
        await using var komga = new LoopbackHttpServer(request =>
            request.Method == "GET" && request.PathAndQuery.StartsWith("/api/v1/books/0BOOK1/file", StringComparison.Ordinal)
                ? LoopbackResponse.Binary(archive, "application/x-7z-compressed")
                : LoopbackResponse.Status(404));
        await using var host = await TestServiceHost.CreateAsync(settings => settings.Servers.Add(new KomgaServer
        {
            Id = 9,
            Name = "Loopback",
            BaseUrl = komga.BaseUrl,
            Username = "reader",
            Password = "secret"
        }));
        var book = CreateKomgaBook("application/x-7z-compressed", archive.Length);

        var comic = await host.Library.DownloadFromKomgaAsync(book, serverId: 9, cancellationToken: TestContext.Current.CancellationToken);

        // The book id makes the file name unique (same series/name in two libraries or servers)
        var expectedPath = Path.Combine(host.Library.ComicsDirectory, "Wolf Strips - Wolf Strips 001 [0BOOK1].cbz");
        Assert.Equal(expectedPath, comic.FilePath);
        Assert.Equal(ComicFormat.Cbz, comic.Format);
        Assert.Equal(ComicSource.Komga, comic.Source);
        Assert.Equal("0BOOK1", comic.KomgaId);
        Assert.Equal(9, comic.KomgaServerId);
        Assert.Equal(4, comic.PageCount);
        Assert.NotNull(comic.CoverPath);
        Assert.Equal(TestFiles.ExpectedPageNames, await host.Reader.GetPageNamesAsync(comic.FilePath));
        // Only the converted CBZ is left: no downloaded .cb7, .partial or .partial.validator
        Assert.Equal(new[] { Path.GetFileName(expectedPath) }, Directory.GetFiles(host.Library.ComicsDirectory).Select(file => Path.GetFileName(file)));
        Assert.Contains(komga.Requests, request => request.PathAndQuery.StartsWith("/api/v1/books/0BOOK1/file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CleanupPendingKomgaDownload_RemovesTheDownloadAndItsResumeFiles()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var book = CreateKomgaBook("application/zip", 100);
        var download = Path.Combine(host.Library.ComicsDirectory, "Wolf Strips - Wolf Strips 001 [0BOOK1].cbz");
        var other = Path.Combine(host.Library.ComicsDirectory, "Wolf Strips - Wolf Strips 001 [0BOOK2].cbz");
        foreach (var file in new[] { download, download + ".partial", download + ".partial.validator", other })
        {
            await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);
        }

        host.Library.CleanupPendingKomgaDownload(book);

        Assert.Equal(new[] { other }, Directory.GetFiles(host.Library.ComicsDirectory));
    }

    [Fact]
    public async Task ImportLocalComicAsync_Pdf_IsConvertedOnImport()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var source = host.Temp.CopyFixture(TestFiles.Pdf, Path.Combine("external", "book.pdf"));

        var comic = await host.Library.ImportLocalComicAsync(source);

        Assert.Equal(ComicFormat.Cbz, comic.Format);
        Assert.Equal(Path.Combine(host.Library.ComicsDirectory, "book.cbz"), comic.FilePath);
        Assert.Equal("PDF Title", comic.Title);
        Assert.Equal(3, comic.PageCount);
        Assert.Equal(TestFiles.PageBytes(TestFiles.FakePdfPages[2]), await host.Reader.GetPageAsync(comic.FilePath, 2));
        Assert.NotNull(comic.CoverPath);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ImportLocalComicAsync_Pdf_ConvertWhileReading_KeepsThePdf()
    {
        await using var host = await TestServiceHost.CreateAsync(settings => settings.UnsupportedFormatHandlingMode = UnsupportedFormatHandlingMode.ConvertWhileReading);
        var source = host.Temp.CopyFixture(TestFiles.Pdf, Path.Combine("external", "book.pdf"));

        var comic = await host.Library.ImportLocalComicAsync(source);

        Assert.Equal(ComicFormat.Pdf, comic.Format);
        Assert.Equal(source, comic.FilePath);
        Assert.Equal(3, comic.PageCount);
        Assert.Empty(Directory.GetFiles(host.Library.ComicsDirectory));
    }

    [Fact]
    public async Task ImportLocalComicAsync_Epub_IsConvertedOnImport()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var source = host.Temp.CopyFixture(TestFiles.Epub, Path.Combine("external", "book.epub"));

        var comic = await host.Library.ImportLocalComicAsync(source);

        Assert.Equal(ComicFormat.Cbz, comic.Format);
        Assert.Equal(Path.Combine(host.Library.ComicsDirectory, "book.cbz"), comic.FilePath);
        Assert.Equal(TestFiles.EpubTitle, comic.Title);
        Assert.Equal(TestFiles.EpubAuthor, comic.Authors);
        Assert.Equal(4, comic.PageCount);
        Assert.Null(await host.Library.GetEpubConversionStateAsync(comic.Id));
    }

    [Fact]
    public async Task ImportLocalComicAsync_Epub_ConvertWhileReading_StoresTheSourceAndAPendingConversion()
    {
        await using var host = await TestServiceHost.CreateAsync(settings => settings.UnsupportedFormatHandlingMode = UnsupportedFormatHandlingMode.ConvertWhileReading);
        var source = host.Temp.CopyFixture(TestFiles.Epub, Path.Combine("external", "book.epub"));

        var comic = await host.Library.ImportLocalComicAsync(source);

        Assert.Equal(ComicFormat.Epub, comic.Format);
        Assert.Equal(Path.Combine(host.Library.ComicsDirectory, "book.epub"), comic.FilePath);
        Assert.True(File.Exists(comic.FilePath));
        Assert.True(File.Exists(source));
        Assert.Equal(TestFiles.EpubTitle, comic.Title);
        Assert.NotNull(comic.CoverPath);
        // No page was rendered yet
        Assert.Empty(host.WebView.Sessions);

        var state = await host.Library.GetEpubConversionStateAsync(comic.Id);
        Assert.NotNull(state);
        Assert.Equal(EpubConversionStatus.Pending, state.Status);
        Assert.Equal(comic.FilePath, state.SourceEpubPath);
    }

    [Fact]
    public async Task ScanAndImportDirectoryAsync_ImportsAllComics_WithTheFolderAsSeriesFallback()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var root = host.Temp.CreateDirectory("scan");
        host.Temp.CopyFixture(TestFiles.CbzWithoutComicInfo, Path.Combine("scan", "one.cbz"));
        host.Temp.CopyFixture(TestFiles.CbzWithoutComicInfo, Path.Combine("scan", "Series_A", "two.cbz"));
        host.Temp.CopyFixture(TestFiles.CbzWithoutComicInfo, Path.Combine("scan", "__MACOSX", "hidden.cbz"));
        await File.WriteAllTextAsync(Path.Combine(root, "readme.txt"), "not a comic", TestContext.Current.CancellationToken);
        var libraryChanged = 0;
        host.Library.LibraryChanged += (_, _) => Interlocked.Increment(ref libraryChanged);

        var files = host.Library.GetSupportedComicFilesInDirectory(root);
        var comics = await host.Library.ScanAndImportDirectoryAsync(root);

        Assert.Equal(2, files.Count);
        Assert.Equal(new[] { "one", "two" }, comics.Select(comic => comic.Title).Order(StringComparer.Ordinal));
        Assert.Null(comics.Single(comic => comic.Title == "one").SeriesName);
        Assert.Equal("Series_A", comics.Single(comic => comic.Title == "two").SeriesName);
        // One notification for the whole scan
        Assert.Equal(1, libraryChanged);
    }

    [Fact]
    public async Task ScanAndImportDirectoryAsync_MissingDirectory_ReturnsNothing()
    {
        await using var host = await TestServiceHost.CreateAsync();

        Assert.Empty(await host.Library.ScanAndImportDirectoryAsync(host.Temp.Combine("missing")));
    }

    [Fact]
    public async Task UpdateReadingProgressAsync_CompletesOnTheLastPage()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var comic = await host.Library.ImportLocalComicAsync(host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "progress.cbz"));

        await host.Library.UpdateReadingProgressAsync(comic, 1);
        Assert.Equal(1, comic.CurrentPage);
        Assert.False(comic.IsCompleted);
        Assert.NotNull(comic.ReadProgressLastModified);

        await host.Library.UpdateReadingProgressAsync(comic, 3);
        Assert.True(comic.IsCompleted);

        var stored = await host.Database.GetComicAsync(comic.Id);
        Assert.Equal(3, stored!.CurrentPage);
        Assert.True(stored.IsCompleted);
        Assert.Null(await host.Database.GetPendingKomgaReadProgressAsync(comic.Id));
    }

    [Fact]
    public async Task UpdateReadingProgressAsync_KomgaComic_QueuesTheProgressForSync()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var comic = new Comic
        {
            Title = "Komga",
            FilePath = host.Temp.Combine("komga.cbz"),
            PageCount = 10,
            Source = ComicSource.Komga,
            KomgaId = "0BOOK1",
            KomgaServerId = 3
        };
        await host.Database.SaveComicAsync(comic);

        await host.Library.UpdateReadingProgressAsync(comic, 4, notifyLibraryChanged: false);

        Assert.Equal("Pending sync", comic.KomgaSyncStatus);
        var pending = await host.Database.GetPendingKomgaReadProgressAsync(comic.Id);
        Assert.NotNull(pending);
        Assert.Equal(4, pending.Page);
        Assert.Equal("0BOOK1", pending.BookId);
        Assert.Equal(3, pending.ServerId);

        // Progress coming from Komga (with a timestamp) is not queued again
        await host.Database.DeletePendingKomgaReadProgressAsync(comic.Id);
        await host.Library.UpdateReadingProgressAsync(comic, 6, DateTime.UtcNow);
        Assert.Null(await host.Database.GetPendingKomgaReadProgressAsync(comic.Id));
        Assert.Equal(6, (await host.Database.GetComicAsync(comic.Id))!.CurrentPage);
    }

    [Fact]
    public async Task UpdateReadingProgressAsync_NotifyLibraryChanged()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var comic = await host.Library.ImportLocalComicAsync(host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "notify.cbz"));
        var libraryChanged = 0;
        host.Library.LibraryChanged += (_, _) => Interlocked.Increment(ref libraryChanged);

        await host.Library.UpdateReadingProgressAsync(comic, 1, notifyLibraryChanged: false);
        Assert.Equal(0, libraryChanged);

        await host.Library.UpdateReadingProgressAsync(comic, 2);
        Assert.Equal(1, libraryChanged);
    }

    [Fact]
    public async Task UpdateComicMetadataAsync_UpdatesTheDatabaseAndTheComicInfoInTheCbz()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "metadata.cbz");
        var comic = await host.Library.ImportLocalComicAsync(path);

        await host.Library.UpdateComicMetadataAsync(comic, new ComicInfo
        {
            Title = "Renamed",
            Series = "Other Series",
            Number = "8",
            Writer = "New Writer",
            Year = 2020,
            Month = 2,
            Day = 29
        });

        var stored = await host.Database.GetComicAsync(comic.Id);
        Assert.NotNull(stored);
        Assert.Equal("Renamed", stored.Title);
        Assert.Equal("Other Series", stored.SeriesName);
        Assert.Equal(8f, stored.Number!.Value);
        Assert.Equal("New Writer", stored.Authors);
        Assert.Equal(new DateTime(2020, 2, 29), stored.ReleaseDate!.Value);

        var comicInfo = await host.ComicConverter.ExtractComicInfoAsync(path);
        Assert.NotNull(comicInfo);
        Assert.Equal("Renamed", comicInfo.Title);
        Assert.Equal("Other Series", comicInfo.Series);
        Assert.Equal(1, TestFiles.ZipEntryNames(path).Count(name => name.EndsWith("ComicInfo.xml", StringComparison.OrdinalIgnoreCase)));

        // The pages are still intact
        await host.Reader.ClearCacheAsync();
        Assert.Equal(TestFiles.ExpectedPageNames, await host.Reader.GetPageNamesAsync(path));
        Assert.Equal(TestFiles.PageBytes("page2.png"), await host.Reader.GetPageAsync(path, 1));
    }

    /// <summary>
    /// Regression: ImportLocalComicAsync and UpdateComicMetadataAsync parsed the ComicInfo number with float.TryParse in
    /// the current culture: with a German UI "1.5" became 15 (the dot is the group separator there).
    /// </summary>
    [Fact]
    public async Task ImportLocalComicAsync_DecimalNumber_IsCultureInvariant()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = host.Temp.Combine("decimal.cbz");
        TestFiles.CreateZip(path,
            ("page1.jpg", TestFiles.PageBytes("page1.jpg")),
            ("ComicInfo.xml", Encoding.UTF8.GetBytes("<ComicInfo><Title>Half</Title><Number>1.5</Number></ComicInfo>")));
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var comic = await host.Library.ImportLocalComicAsync(path);

            Assert.Equal(1.5f, comic.Number!.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task RemoveComicFromLibraryAsync_KeepsTheFile_DeleteComicAsync_DeletesIt()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var keepPath = host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "keep.cbz");
        var deletePath = host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "delete.cbz");
        var keep = await host.Library.ImportLocalComicAsync(keepPath);
        var delete = await host.Library.ImportLocalComicAsync(deletePath);

        await host.Library.RemoveComicFromLibraryAsync(keep);
        await host.Library.DeleteComicAsync(delete);

        Assert.True(File.Exists(keepPath));
        Assert.False(File.Exists(deletePath));
        Assert.False(File.Exists(keep.CoverPath));
        Assert.False(File.Exists(delete.CoverPath));
        Assert.Empty(await host.Library.GetAllComicsAsync());
    }

    [Fact]
    public async Task CleanupMissingFilesAsync_RemovesComicsWithoutFile()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var existing = await host.Library.ImportLocalComicAsync(host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "exists.cbz"));
        var missingPath = host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "missing.cbz");
        var missing = await host.Library.ImportLocalComicAsync(missingPath);
        File.Delete(missingPath);

        var removed = await host.Library.CleanupMissingFilesAsync();

        Assert.Equal(1, removed);
        Assert.Equal(existing.Id, Assert.Single(await host.Library.GetAllComicsAsync()).Id);
        Assert.Null(await host.Library.GetComicAsync(missing.Id));
    }

    [Fact]
    public async Task GetNextComicInSeriesAsync_UsesTheNumberOrder()
    {
        await using var host = await TestServiceHost.CreateAsync();
        async Task<Comic> AddAsync(string title, string? series, float? number)
        {
            var comic = new Comic { Title = title, SeriesName = series, Number = number, FilePath = host.Temp.Combine(title + ".cbz"), PageCount = 1 };
            await host.Database.SaveComicAsync(comic);
            return comic;
        }

        var third = await AddAsync("Third", "Wolves", 3);
        var first = await AddAsync("First", "Wolves", 1);
        var second = await AddAsync("Second", "  Wolves ", 2);
        await AddAsync("Other", "Foxes", 2);
        var loner = await AddAsync("Loner", null, 1);

        Assert.Equal(second.Id, (await host.Library.GetNextComicInSeriesAsync(first.Id))?.Id);
        Assert.Equal(third.Id, (await host.Library.GetNextComicInSeriesAsync(second.Id))?.Id);
        Assert.Null(await host.Library.GetNextComicInSeriesAsync(third.Id));
        Assert.Null(await host.Library.GetNextComicInSeriesAsync(loner.Id));
    }

    [Fact]
    public async Task DeferLibraryChanged_RaisesOnceWhenTheOutermostScopeEnds()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var first = await host.Library.ImportLocalComicAsync(host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "a.cbz"));
        var second = await host.Library.ImportLocalComicAsync(host.Temp.CopyFixture(TestFiles.CbzWithComicInfo, "b.cbz"));
        var libraryChanged = 0;
        host.Library.LibraryChanged += (_, _) => Interlocked.Increment(ref libraryChanged);

        using (host.Library.DeferLibraryChanged())
        {
            using (host.Library.DeferLibraryChanged())
            {
                await host.Library.RemoveComicFromLibraryAsync(first);
            }

            await host.Library.RemoveComicFromLibraryAsync(second);
            Assert.Equal(0, libraryChanged);
        }

        Assert.Equal(1, libraryChanged);
    }

    [Theory]
    [InlineData("the_walking_dead (2003)", "The Walking Dead")]
    [InlineData("SPIDER_MAN  2", "Spider Man 2")]
    [InlineData("Saga", "Saga")]
    [InlineData("(2003) annual", "")]
    [InlineData("", "")]
    public void GetSuggestedSeriesNameFromDirectoryName(string directoryName, string expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal(expected, LibraryService.GetSuggestedSeriesNameFromDirectoryName(directoryName));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void GetDirectorySeriesNameFallback_UsesTheRelativeFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "library");

        Assert.Equal("Marvel / Spider-Man", LibraryService.GetDirectorySeriesNameFallback(Path.Combine(root, "Marvel", "Spider-Man", "issue1.cbz"), root));
        Assert.Null(LibraryService.GetDirectorySeriesNameFallback(Path.Combine(root, "issue1.cbz"), root));
        Assert.Null(LibraryService.GetDirectorySeriesNameFallback(Path.Combine(Path.GetTempPath(), "elsewhere", "issue1.cbz"), root));
        Assert.Null(LibraryService.GetDirectorySeriesNameFallback(string.Empty, root));
    }

    [Fact]
    public void GetDirectoryDisplayName_IgnoresTrailingSeparators()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Series X");

        Assert.Equal("Series X", LibraryService.GetDirectoryDisplayName(directory + Path.DirectorySeparatorChar));
        Assert.Equal("Series X", LibraryService.GetDirectoryDisplayName(directory));
        Assert.Equal(string.Empty, LibraryService.GetDirectoryDisplayName("  "));
    }

    [Fact]
    public void SanitizeFileName_ReplacesInvalidCharacters()
    {
        var sanitized = LibraryService.SanitizeFileName("Wolf/Strips 001");

        Assert.Equal("Wolf_Strips 001", sanitized);
        Assert.DoesNotContain(sanitized, character => Path.GetInvalidFileNameChars().Contains(character));
    }
}
