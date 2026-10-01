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

using System.IO.Compression;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

public sealed class ComicConverterServiceTests
{
    /// <summary>
    /// Formats which are converted to CBZ (CBR, CB7 and CBT, solid and non-solid)
    /// </summary>
    public static TheoryData<string> ConvertibleArchives() => new()
    {
        TestFiles.Cbr,
        TestFiles.CbrSolid,
        TestFiles.Cb7,
        TestFiles.Cb7Solid,
        TestFiles.Cbt
    };

    public static TheoryData<string, ComicFormat, bool> AnalyzableArchives() => new()
    {
        { TestFiles.CbzWithComicInfo, ComicFormat.Cbz, true },
        { TestFiles.CbzWithoutComicInfo, ComicFormat.Cbz, false },
        { TestFiles.Cbt, ComicFormat.Cbt, true },
        { TestFiles.Cb7, ComicFormat.Cb7, true },
        { TestFiles.Cb7Solid, ComicFormat.Cb7, true },
        { TestFiles.Cbr, ComicFormat.Cbr, true },
        { TestFiles.CbrSolid, ComicFormat.Cbr, true }
    };

    [Theory]
    [InlineData("a.cbz", ComicArchiveType.Zip)]
    [InlineData("a.CBR", ComicArchiveType.Rar)]
    [InlineData("a.cb7", ComicArchiveType.SevenZip)]
    [InlineData("a.cbt", ComicArchiveType.Tar)]
    [InlineData("a.cba", ComicArchiveType.Ace)]
    [InlineData("a.pdf", ComicArchiveType.Unknown)]
    [InlineData("a.zip", ComicArchiveType.Unknown)]
    public void GetArchiveType_UsesTheExtension(string fileName, ComicArchiveType expected)
    {
        Assert.Equal(expected, ComicConverterService.GetArchiveType(fileName));
    }

    [Theory]
    [InlineData("a.cbz", true)]
    [InlineData("a.cbr", true)]
    [InlineData("a.cb7", true)]
    [InlineData("a.cbt", true)]
    [InlineData("a.cba", false)]
    [InlineData("a.epub", false)]
    public void IsSupported(string fileName, bool expected)
    {
        Assert.Equal(expected, ComicConverterService.IsSupported(fileName));
    }

    [Theory]
    [InlineData(TestFiles.CbrSolid, true)]
    [InlineData(TestFiles.Cbr, false)]
    [InlineData(TestFiles.Cb7Solid, false)]
    [InlineData(TestFiles.CbzWithComicInfo, false)]
    public void IsSolidRar(string fixture, bool expected)
    {
        Assert.Equal(expected, ComicConverterService.IsSolidRar(TestFiles.Get(fixture)));
    }

    [Fact]
    public async Task IsSolidRar_CorruptFile_ReturnsFalse()
    {
        using var temp = new TempDirectory("solid");
        var path = temp.Combine("corrupt.cbr");
        await File.WriteAllTextAsync(path, "Rar! but not really", TestContext.Current.CancellationToken);

        Assert.False(ComicConverterService.IsSolidRar(path));
    }

    [Theory]
    [InlineData(TestFiles.CbzWithComicInfo, false)]
    [InlineData(TestFiles.Cbr, false)]
    [InlineData(TestFiles.CbrSolid, true)]
    [InlineData(TestFiles.Cb7, false)]
    [InlineData(TestFiles.Cb7Solid, true)]
    [InlineData(TestFiles.Cbt, false)]
    public void NeedsConversion(string fixture, bool expected)
    {
        Assert.Equal(expected, ComicConverterService.NeedsConversion(TestFiles.Get(fixture)));
    }

    [Theory]
    [MemberData(nameof(ConvertibleArchives))]
    public async Task ConvertToCbzAsync_KeepsPagesPageOrderAndComicInfo(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var outputDirectory = host.Temp.Combine("converted");
        var progress = new RecordingProgress<double>();

        var cbzPath = await host.ComicConverter.ConvertToCbzAsync(TestFiles.Get(fixture), outputDirectory, progress);

        Assert.Equal(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(fixture) + ".cbz"), cbzPath);
        Assert.True(File.Exists(cbzPath));

        // Only the pages and ComicInfo.xml are copied: Thumbs.db, notes.txt and __MACOSX are dropped
        var entries = TestFiles.ZipEntryNames(cbzPath).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(TestFiles.ExpectedPageNames.Append("ComicInfo.xml").Order(StringComparer.Ordinal), entries);

        var pageNames = await host.Reader.GetPageNamesAsync(cbzPath);
        Assert.Equal(TestFiles.ExpectedPageNames, pageNames);
        for (var index = 0; index < pageNames.Count; index++)
        {
            Assert.Equal(TestFiles.PageBytes(TestFiles.ExpectedPageNames[index]), await host.Reader.GetPageAsync(cbzPath, index));
        }

        var comicInfo = await host.ComicConverter.ExtractComicInfoAsync(cbzPath);
        Assert.NotNull(comicInfo);
        Assert.Equal(TestFiles.ComicInfoTitle, comicInfo.Title);
        Assert.Equal(TestFiles.ComicInfoSeries, comicInfo.Series);

        Assert.NotEmpty(progress.Values);
        Assert.Equal(1d, progress.Values[^1]);
        Assert.All(progress.Values, value => Assert.InRange(value, 0d, 1d));
    }

    [Theory]
    [MemberData(nameof(ConvertibleArchives))]
    public async Task ConvertToCbzForImportAsync_ReturnsImportData(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var outputDirectory = host.Temp.Combine("converted");

        using var importData = await host.ComicConverter.ConvertToCbzForImportAsync(TestFiles.Get(fixture), outputDirectory);

        Assert.Equal(ComicFormat.Cbz, importData.Format);
        Assert.Equal(4, importData.PageCount);
        Assert.True(File.Exists(importData.FilePath));
        Assert.Equal(new FileInfo(importData.FilePath).Length, importData.FileSize);
        Assert.NotNull(importData.ComicInfo);
        Assert.Equal(TestFiles.ComicInfoTitle, importData.ComicInfo.Title);
        // The cover is the first page in reading order, not the first entry in the archive
        Assert.NotNull(importData.CoverImageStream);
        Assert.Equal(TestFiles.PageBytes("page1.jpg"), TestFiles.ReadAll(importData.CoverImageStream));
    }

    /// <summary>
    /// JPEG/PNG are already compressed: they are stored, only ComicInfo.xml (and BMP/TIFF) is deflated
    /// </summary>
    [Theory]
    [MemberData(nameof(ConvertibleArchives))]
    public async Task ConvertToCbzAsync_StoresCompressedImages_DeflatesXml(string fixture)
    {
        await using var host = await TestServiceHost.CreateAsync();

        var cbzPath = await host.ComicConverter.ConvertToCbzAsync(TestFiles.Get(fixture), host.Temp.Combine("converted"));

        using var archive = ZipFile.OpenRead(cbzPath);
        foreach (var entry in archive.Entries)
        {
            if (ComicConstants.IsImageFile(entry.FullName))
            {
                Assert.True(entry.CompressedLength >= entry.Length,
                    $"{entry.FullName} was compressed ({entry.Length} -> {entry.CompressedLength} bytes)");
            }
            else
            {
                Assert.Equal("ComicInfo.xml", entry.FullName);
                Assert.True(entry.CompressedLength < entry.Length, "ComicInfo.xml should be deflated");
            }
        }
    }

    [Fact]
    public async Task ConvertToCbzAsync_DeflatesUncompressedImageFormats()
    {
        await using var host = await TestServiceHost.CreateAsync();
        // A blank 64x64 24-bit BMP: very compressible
        var bmp = CreateBlankBmp(64, 64);
        var source = host.Temp.Combine("bitmaps.cbt");
        CreateTar(source, ("page1.bmp", bmp), ("page2.jpg", TestFiles.PageBytes("page1.jpg")));

        var cbzPath = await host.ComicConverter.ConvertToCbzAsync(source, host.Temp.Combine("converted"));

        using var archive = ZipFile.OpenRead(cbzPath);
        var bmpEntry = archive.GetEntry("page1.bmp");
        var jpgEntry = archive.GetEntry("page2.jpg");
        Assert.NotNull(bmpEntry);
        Assert.NotNull(jpgEntry);
        Assert.Equal(bmp.Length, bmpEntry.Length);
        Assert.True(bmpEntry.CompressedLength < bmpEntry.Length / 4, $"BMP not deflated: {bmpEntry.CompressedLength} of {bmpEntry.Length}");
        Assert.True(jpgEntry.CompressedLength >= jpgEntry.Length);
    }

    private static byte[] CreateBlankBmp(int width, int height)
    {
        var rowSize = (width * 3 + 3) / 4 * 4;
        var pixelBytes = rowSize * height;
        var bmp = new byte[54 + pixelBytes];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
        BitConverter.TryWriteBytes(bmp.AsSpan(10), 54);
        BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bmp.AsSpan(18), width);
        BitConverter.TryWriteBytes(bmp.AsSpan(22), height);
        BitConverter.TryWriteBytes(bmp.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(bmp.AsSpan(28), (short)24);
        BitConverter.TryWriteBytes(bmp.AsSpan(34), pixelBytes);
        Array.Fill(bmp, (byte)0xFF, 54, pixelBytes);
        return bmp;
    }

    private static void CreateTar(string path, params (string Name, byte[] Content)[] entries)
    {
        using var stream = File.Create(path);
        using var writer = new System.Formats.Tar.TarWriter(stream, System.Formats.Tar.TarEntryFormat.Ustar);
        foreach (var (name, content) in entries)
        {
            var entry = new System.Formats.Tar.UstarTarEntry(System.Formats.Tar.TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(content)
            };
            writer.WriteEntry(entry);
        }
    }

    [Fact]
    public async Task ConvertToCbzAsync_FromStream_UsesTheSourceName()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var outputDirectory = host.Temp.Combine("from-stream");
        await using var input = File.OpenRead(TestFiles.Get(TestFiles.Cb7Solid));

        var cbzPath = await host.ComicConverter.ConvertToCbzAsync(input, "Downloaded Book.cb7", ComicArchiveType.SevenZip, outputDirectory);

        Assert.Equal(Path.Combine(outputDirectory, "Downloaded Book.cbz"), cbzPath);
        Assert.Equal(TestFiles.ExpectedPageNames, await host.Reader.GetPageNamesAsync(cbzPath));
    }

    [Fact]
    public async Task ConvertToCbzAsync_OverwritesAnExistingCbz()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var outputDirectory = host.Temp.CreateDirectory("existing");
        var existing = Path.Combine(outputDirectory, "comic.cbz");
        await File.WriteAllTextAsync(existing, "old content", TestContext.Current.CancellationToken);

        var cbzPath = await host.ComicConverter.ConvertToCbzAsync(TestFiles.Get(TestFiles.Cbt), outputDirectory);

        Assert.Equal(existing, cbzPath);
        Assert.Equal(4, (await host.Reader.GetPageNamesAsync(cbzPath)).Count);
    }

    [Fact]
    public async Task ConvertToCbzAsync_InvalidInput_Throws()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var outputDirectory = host.Temp.Combine("invalid");
        var ace = host.Temp.Combine("old.cba");
        await File.WriteAllTextAsync(ace, "ACE", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<FileNotFoundException>(() => host.ComicConverter.ConvertToCbzAsync(host.Temp.Combine("missing.cbr"), outputDirectory));
        await Assert.ThrowsAsync<NotSupportedException>(() => host.ComicConverter.ConvertToCbzAsync(ace, outputDirectory));
        await Assert.ThrowsAsync<NotSupportedException>(() => host.ComicConverter.ConvertToCbzAsync(Stream.Null, "x.bin", ComicArchiveType.Unknown, outputDirectory));
        await Assert.ThrowsAsync<NotSupportedException>(() => host.ComicConverter.ConvertToCbzAsync(Stream.Null, "x.cba", ComicArchiveType.Ace, outputDirectory));
    }

    [Theory]
    [MemberData(nameof(AnalyzableArchives))]
    public async Task AnalyzeArchiveForImportAsync_ReadsPageCountCoverAndComicInfo(string fixture, ComicFormat expectedFormat, bool hasComicInfo)
    {
        await using var host = await TestServiceHost.CreateAsync();
        var path = TestFiles.Get(fixture);

        using var importData = await host.ComicConverter.AnalyzeArchiveForImportAsync(path);

        Assert.Equal(path, importData.FilePath);
        Assert.Equal(expectedFormat, importData.Format);
        Assert.Equal(4, importData.PageCount);
        Assert.Equal(new FileInfo(path).Length, importData.FileSize);
        Assert.NotNull(importData.CoverImageStream);
        Assert.Equal(TestFiles.PageBytes("page1.jpg"), TestFiles.ReadAll(importData.CoverImageStream));
        if (hasComicInfo)
        {
            Assert.NotNull(importData.ComicInfo);
            Assert.Equal(TestFiles.ComicInfoTitle, importData.ComicInfo.Title);
            Assert.Equal("7", importData.ComicInfo.Number);
        }
        else
        {
            Assert.Null(importData.ComicInfo);
        }
    }

    [Fact]
    public async Task AnalyzeArchiveForImportAsync_Subfolders_CoverIsTheFirstPageInReadingOrder()
    {
        await using var host = await TestServiceHost.CreateAsync();

        using var importData = await host.ComicConverter.AnalyzeArchiveForImportAsync(TestFiles.Get(TestFiles.CbzWithSubfolders));

        Assert.Equal(4, importData.PageCount);
        Assert.NotNull(importData.CoverImageStream);
        Assert.Equal(TestFiles.PageBytes("page1.jpg"), TestFiles.ReadAll(importData.CoverImageStream));
    }

    [Fact]
    public async Task AnalyzeArchiveForImportAsync_UnsupportedFormat_Throws()
    {
        await using var host = await TestServiceHost.CreateAsync();
        var pdf = TestFiles.Get(TestFiles.Pdf);

        await Assert.ThrowsAsync<NotSupportedException>(() => host.ComicConverter.AnalyzeArchiveForImportAsync(pdf));
        await Assert.ThrowsAsync<FileNotFoundException>(() => host.ComicConverter.AnalyzeArchiveForImportAsync(host.Temp.Combine("missing.cbz")));
    }

    [Theory]
    [InlineData(TestFiles.CbzWithComicInfo)]
    [InlineData(TestFiles.Cbt)]
    [InlineData(TestFiles.Cb7)]
    [InlineData(TestFiles.Cb7Solid)]
    [InlineData(TestFiles.Cbr)]
    [InlineData(TestFiles.CbrSolid)]
    public async Task ExtractComicInfoAsync_ReadsComicInfoFromEveryArchiveType(string fixture)
    {
        var converter = new ComicConverterService();

        var comicInfo = await converter.ExtractComicInfoAsync(TestFiles.Get(fixture));

        Assert.NotNull(comicInfo);
        Assert.Equal(TestFiles.ComicInfoTitle, comicInfo.Title);
        Assert.Equal(TestFiles.ComicInfoSeries, comicInfo.Series);
        Assert.Equal("7", comicInfo.Number);
        Assert.Equal(4, comicInfo.PageCount);
    }

    [Fact]
    public async Task ExtractComicInfoAsync_NoComicInfoOrBrokenFile_ReturnsNull()
    {
        using var temp = new TempDirectory("comicinfo");
        var corrupt = temp.Combine("corrupt.cbz");
        await File.WriteAllTextAsync(corrupt, "definitely not a zip file", TestContext.Current.CancellationToken);
        var macOnly = temp.Combine("mac.cbz");
        // A ComicInfo.xml inside __MACOSX is not the real one
        TestFiles.CreateZip(macOnly,
            ("page1.jpg", TestFiles.PageBytes("page1.jpg")),
            ("__MACOSX/ComicInfo.xml", "<ComicInfo><Title>mac</Title></ComicInfo>"u8.ToArray()));
        var converter = new ComicConverterService();

        Assert.Null(await converter.ExtractComicInfoAsync(TestFiles.Get(TestFiles.CbzWithoutComicInfo)));
        Assert.Null(await converter.ExtractComicInfoAsync(corrupt));
        Assert.Null(await converter.ExtractComicInfoAsync(macOnly));
        Assert.Null(await converter.ExtractComicInfoAsync(TestFiles.Get(TestFiles.Pdf)));
    }

    [Fact]
    public void ParseComicInfo_InvalidData_ReturnsNull()
    {
        Assert.Null(ComicConverterService.ParseComicInfo("<<<"u8.ToArray()));
        Assert.Null(ComicConverterService.ParseComicInfo(Array.Empty<byte>()));
        Assert.Equal("x", ComicConverterService.ParseComicInfo("<ComicInfo><Title>x</Title></ComicInfo>"u8.ToArray())?.Title);
    }
}
