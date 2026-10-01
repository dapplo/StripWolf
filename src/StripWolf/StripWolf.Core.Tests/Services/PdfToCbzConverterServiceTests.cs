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
/// The PDF conversion with a fake <see cref="IPdfRenderer"/>: the real PDFium renderer lives in StripWolf.Desktop.
/// </summary>
public sealed class PdfToCbzConverterServiceTests
{
    private static readonly PdfMetadata Metadata = new()
    {
        Title = "PDF Title",
        Author = "Pdf Author",
        Subject = "Pdf subject",
        Keywords = "pdf, test",
        Creator = "img2pdf",
        CreationDate = new DateTime(2023, 2, 14, 10, 30, 0)
    };

    [Fact]
    public async Task ConvertPdfToCbzForImportAsync_WritesAllPagesAndComicInfo()
    {
        using var temp = new TempDirectory("pdf");
        var renderer = new FakePdfRenderer(TestFiles.FakePdfPageBytes(), Metadata);
        var converter = new PdfToCbzConverterService(renderer);
        var progress = new RecordingProgress<double>();
        var outputDirectory = temp.Combine("out");

        using var importData = await converter.ConvertPdfToCbzForImportAsync(TestFiles.Get(TestFiles.Pdf), outputDirectory, progress);

        Assert.Equal(Path.Combine(outputDirectory, "comic.cbz"), importData.FilePath);
        Assert.Equal(ComicFormat.Cbz, importData.Format);
        Assert.Equal(3, importData.PageCount);
        Assert.Equal(new FileInfo(importData.FilePath).Length, importData.FileSize);

        var entries = TestFiles.ZipEntryNames(importData.FilePath);
        Assert.Equal(new[] { "ComicInfo.xml", "Page_00001.jpg", "Page_00002.jpg", "Page_00003.jpg" }, entries.Order(StringComparer.Ordinal));
        for (var index = 0; index < TestFiles.FakePdfPages.Length; index++)
        {
            Assert.Equal(TestFiles.PageBytes(TestFiles.FakePdfPages[index]), TestFiles.ReadZipEntry(importData.FilePath, $"Page_{index + 1:D5}.jpg"));
        }

        Assert.NotNull(importData.CoverImageStream);
        Assert.Equal(TestFiles.PageBytes(TestFiles.FakePdfPages[0]), TestFiles.ReadAll(importData.CoverImageStream));

        var comicInfo = importData.ComicInfo;
        Assert.NotNull(comicInfo);
        Assert.Equal("PDF Title", comicInfo.Title);
        Assert.Equal("Pdf Author", comicInfo.Writer);
        Assert.Equal("Pdf subject", comicInfo.Summary);
        Assert.Equal("pdf, test", comicInfo.Tags);
        Assert.Equal("Created with: img2pdf", comicInfo.Notes);
        Assert.Equal(2023, comicInfo.Year);
        Assert.Equal(2, comicInfo.Month);
        Assert.Equal(14, comicInfo.Day);

        // The ComicInfo.xml in the CBZ contains the same data
        var storedComicInfo = await new ComicConverterService().ExtractComicInfoAsync(importData.FilePath);
        Assert.Equal("PDF Title", storedComicInfo?.Title);
        Assert.Equal("Pdf Author", storedComicInfo?.Writer);

        Assert.Equal(new[] { 1d / 3, 2d / 3, 1d }, progress.Values);
        var session = Assert.Single(renderer.Sessions);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task ConvertPdfToCbzAsync_WithoutMetadata_WritesNoComicInfo()
    {
        using var temp = new TempDirectory("pdf");
        var converter = new PdfToCbzConverterService(new FakePdfRenderer(TestFiles.FakePdfPageBytes()));

        var cbzPath = await converter.ConvertPdfToCbzAsync(TestFiles.Get(TestFiles.Pdf), temp.DirectoryPath);

        Assert.Equal(new[] { "Page_00001.jpg", "Page_00002.jpg", "Page_00003.jpg" }, TestFiles.ZipEntryNames(cbzPath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ConvertPdfToCbzAsync_MetadataWithoutTitle_UsesTheFileName()
    {
        using var temp = new TempDirectory("pdf");
        var pdf = temp.CopyFixture(TestFiles.Pdf, "My Comic 01.pdf");
        var converter = new PdfToCbzConverterService(new FakePdfRenderer(TestFiles.FakePdfPageBytes(), new PdfMetadata { Author = "Somebody" }));

        using var importData = await converter.ConvertPdfToCbzForImportAsync(pdf, temp.Combine("out"));

        Assert.Equal("My Comic 01", importData.ComicInfo?.Title);
        Assert.Null(importData.ComicInfo?.Notes);
        Assert.Null(importData.ComicInfo?.Year);
        Assert.EndsWith("My Comic 01.cbz", importData.FilePath);
    }

    [Fact]
    public async Task ConvertPdfToCbzAsync_OverwritesAnExistingCbz()
    {
        using var temp = new TempDirectory("pdf");
        var existing = temp.Combine("comic.cbz");
        await File.WriteAllTextAsync(existing, "old", TestContext.Current.CancellationToken);
        var converter = new PdfToCbzConverterService(new FakePdfRenderer(TestFiles.FakePdfPageBytes()));

        var cbzPath = await converter.ConvertPdfToCbzAsync(TestFiles.Get(TestFiles.Pdf), temp.DirectoryPath);

        Assert.Equal(existing, cbzPath);
        Assert.Equal(3, TestFiles.ZipEntryNames(cbzPath).Count);
    }

    [Fact]
    public async Task ConvertPdfToCbzAsync_MissingFile_ThrowsFileNotFound()
    {
        using var temp = new TempDirectory("pdf");
        var converter = new PdfToCbzConverterService(new FakePdfRenderer(TestFiles.FakePdfPageBytes()));

        await Assert.ThrowsAsync<FileNotFoundException>(() => converter.ConvertPdfToCbzAsync(temp.Combine("missing.pdf"), temp.DirectoryPath));
        await Assert.ThrowsAsync<FileNotFoundException>(() => converter.AnalyzePdfForImportAsync(temp.Combine("missing.pdf")));
        await Assert.ThrowsAsync<FileNotFoundException>(() => converter.ExtractComicInfoAsync(temp.Combine("missing.pdf")));
    }

    [Fact]
    public async Task AnalyzePdfForImportAsync_ReadsCoverAndComicInfo_WithoutConverting()
    {
        var renderer = new FakePdfRenderer(TestFiles.FakePdfPageBytes(), Metadata);
        var converter = new PdfToCbzConverterService(renderer);
        var pdf = TestFiles.Get(TestFiles.Pdf);

        using var importData = await converter.AnalyzePdfForImportAsync(pdf);

        Assert.Equal(pdf, importData.FilePath);
        Assert.Equal(ComicFormat.Pdf, importData.Format);
        Assert.Equal(3, importData.PageCount);
        Assert.Equal(new FileInfo(pdf).Length, importData.FileSize);
        Assert.Equal("PDF Title", importData.ComicInfo?.Title);
        Assert.Equal(3, importData.ComicInfo?.PageCount);
        Assert.NotNull(importData.CoverImageStream);
        Assert.Equal(TestFiles.PageBytes(TestFiles.FakePdfPages[0]), TestFiles.ReadAll(importData.CoverImageStream));

        var session = Assert.Single(renderer.Sessions);
        Assert.Equal(new[] { 0 }, session.RenderedPages);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task AnalyzePdfForImportAsync_EmptyPdf_HasNoCover()
    {
        var converter = new PdfToCbzConverterService(new FakePdfRenderer([]));

        using var importData = await converter.AnalyzePdfForImportAsync(TestFiles.Get(TestFiles.Pdf));

        Assert.Equal(0, importData.PageCount);
        Assert.Null(importData.CoverImageStream);
    }

    [Fact]
    public async Task ExtractComicInfoAsync_WithoutMetadata()
    {
        var converter = new PdfToCbzConverterService(new FakePdfRenderer(TestFiles.FakePdfPageBytes()));
        var pdf = TestFiles.Get(TestFiles.Pdf);

        Assert.Null(await converter.ExtractComicInfoAsync(pdf));

        var withPageCount = await converter.ExtractComicInfoAsync(pdf, knownPageCount: 3);
        Assert.NotNull(withPageCount);
        Assert.Equal("comic", withPageCount.Title);
        Assert.Equal(3, withPageCount.PageCount);
    }

    [Fact]
    public void RendererSettings_ArePassedThrough()
    {
        var renderer = new FakePdfRenderer(TestFiles.FakePdfPageBytes());
        var converter = new PdfToCbzConverterService(renderer)
        {
            RenderDpi = 300,
            JpegQuality = 70
        };

        Assert.Equal(300, renderer.RenderDpi);
        Assert.Equal(70, renderer.JpegQuality);
        Assert.Equal(300, converter.RenderDpi);
        Assert.Equal(3, converter.GetPageCount("ignored.pdf"));
    }

    [Theory]
    [InlineData("comic.pdf", true)]
    [InlineData("COMIC.PDF", true)]
    [InlineData("comic.cbz", false)]
    [InlineData("pdf", false)]
    public void IsPdfFile(string fileName, bool expected)
    {
        Assert.Equal(expected, PdfToCbzConverterService.IsPdfFile(fileName));
    }
}
