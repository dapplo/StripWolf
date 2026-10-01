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
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Tar;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

/// <summary>
/// Runs the user's own comics through the readers and converters. They can't be part of the repository (copyright),
/// so these tests only run locally: set STRIPWOLF_TEST_COMICS to a folder with comics (searched recursively).
/// Excluded in CI with --filter "Category!=LocalComics", without the variable they are reported as skipped.
/// </summary>
[Trait("Category", "LocalComics")]
public sealed class LocalComicsTests
{
    public const string EnvironmentVariable = "STRIPWOLF_TEST_COMICS";

    private const string NotConfigured = "(STRIPWOLF_TEST_COMICS is not set)";
    private const string NothingFound = "(no comic files found)";

    /// <summary>
    /// Page formats ImageSharp can't decode, these are only checked for content
    /// </summary>
    private static readonly HashSet<string> NotDecodableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".avif" };

    /// <summary>
    /// The comic files relative to the folder from STRIPWOLF_TEST_COMICS, or a placeholder row so the theory is reported
    /// (as skipped) instead of failing with "no data".
    /// </summary>
    public static TheoryData<string> ComicFiles()
    {
        var data = new TheoryData<string>();
        var root = GetRoot();
        if (root is null)
        {
            data.Add(NotConfigured);
            return data;
        }

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => ComicConstants.IsSupportedComicFile(file) && !ComicConstants.IsIgnoredImportPath(Path.GetRelativePath(root, file)))
            .Select(file => Path.GetRelativePath(root, file))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            data.Add(NothingFound);
        }

        foreach (var file in files)
        {
            data.Add(file);
        }

        return data;
    }

    private static string? GetRoot()
    {
        var root = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) ? null : Path.GetFullPath(root);
    }

    /// <summary>
    /// Skips the test for the placeholder rows, returns the absolute path of the comic otherwise
    /// </summary>
    private static string ResolveOrSkip(string relativePath)
    {
        var root = GetRoot();
        Assert.SkipWhen(root is null || relativePath == NotConfigured,
            $"Set the environment variable {EnvironmentVariable} to a folder with comics to run the local comic tests.");
        Assert.SkipWhen(relativePath == NothingFound, $"No comic files found in {root}.");

        var path = Path.Combine(root!, relativePath);
        Assert.True(File.Exists(path), $"{path} doesn't exist anymore");
        return path;
    }

    [Theory]
    [MemberData(nameof(ComicFiles))]
    public async Task AllPages_CanBeReadAndDecoded(string relativePath)
    {
        var path = ResolveOrSkip(relativePath);
        var format = ComicReaderService.GetComicFormat(path);
        Assert.SkipWhen(format == ComicFormat.Pdf, "PDF pages are rendered by PDFium, which is part of StripWolf.Desktop and not available here.");
        Assert.SkipWhen(format == ComicFormat.Epub, "EPUB pages are rendered by a WebView, which is not available here (see EpubConversion_Works).");

        await using var host = await TestServiceHost.CreateAsync();
        var names = await host.Reader.GetPageNamesAsync(path);
        Assert.NotEmpty(names);

        var (pageCount, fileSize) = await host.Reader.GetComicInfoWithoutCacheAsync(path);
        Assert.Equal(names.Count, pageCount);
        Assert.Equal(new FileInfo(path).Length, fileSize);

        var failures = new List<string>();
        for (var index = 0; index < names.Count; index++)
        {
            try
            {
                var data = await host.Reader.GetPageAsync(path, index);
                if (data.Length == 0)
                {
                    failures.Add($"{index}: {names[index]} is empty");
                    continue;
                }

                if (NotDecodableExtensions.Contains(Path.GetExtension(names[index])))
                {
                    continue;
                }

                var info = SixLabors.ImageSharp.Image.Identify(data.AsSpan());
                if (info.Width <= 0 || info.Height <= 0)
                {
                    failures.Add($"{index}: {names[index]} has an invalid size {info.Width}x{info.Height}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{index}: {names[index]}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {names.Count} pages failed:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Theory]
    [MemberData(nameof(ComicFiles))]
    public async Task ImportAnalysis_MatchesTheReader(string relativePath)
    {
        var path = ResolveOrSkip(relativePath);
        var format = ComicReaderService.GetComicFormat(path);
        Assert.SkipWhen(format is ComicFormat.Pdf or ComicFormat.Epub, "Only archives are analyzed by ComicConverterService.");

        await using var host = await TestServiceHost.CreateAsync();
        var names = await host.Reader.GetPageNamesWithoutCacheAsync(path);

        using var importData = await host.ComicConverter.AnalyzeArchiveForImportAsync(path);

        Assert.Equal(names.Count, importData.PageCount);
        Assert.NotNull(importData.CoverImageStream);
        var cover = TestFiles.ReadAll(importData.CoverImageStream);
        Assert.Equal(await host.Reader.GetPageWithoutCacheAsync(path, 0), cover);
    }

    [Theory]
    [MemberData(nameof(ComicFiles))]
    public async Task ConversionToCbz_KeepsAllPages(string relativePath)
    {
        var path = ResolveOrSkip(relativePath);
        var format = ComicReaderService.GetComicFormat(path);
        Assert.SkipUnless(format is ComicFormat.Cbr or ComicFormat.Cb7 or ComicFormat.Cbt, "Only CBR, CB7 and CBT files are converted to CBZ.");

        await using var host = await TestServiceHost.CreateAsync();
        var sourceNames = await host.Reader.GetPageNamesAsync(path);

        var cbzPath = await host.ComicConverter.ConvertToCbzAsync(path, host.Temp.Combine("converted"));

        var convertedNames = await host.Reader.GetPageNamesWithoutCacheAsync(cbzPath);
        Assert.Equal(sourceNames.Count, convertedNames.Count);
        for (var index = 0; index < sourceNames.Count; index++)
        {
            var source = await host.Reader.GetPageAsync(path, index);
            var converted = await host.Reader.GetPageWithoutCacheAsync(cbzPath, index);
            Assert.True(TestFiles.Sha256(source) == TestFiles.Sha256(converted),
                $"Page {index} differs after the conversion ({sourceNames[index]} -> {convertedNames[index]})");
        }

        if (ArchiveInspector.HasComicInfo(path))
        {
            Assert.NotNull(await host.ComicConverter.ExtractComicInfoAsync(cbzPath));
        }
    }

    [Theory]
    [MemberData(nameof(ComicFiles))]
    public async Task ComicInfo_IsParsedWhenPresent(string relativePath)
    {
        var path = ResolveOrSkip(relativePath);
        var format = ComicReaderService.GetComicFormat(path);
        Assert.SkipWhen(format == ComicFormat.Pdf, "PDF metadata is read by PDFium, which is part of StripWolf.Desktop.");

        await using var host = await TestServiceHost.CreateAsync();
        if (format == ComicFormat.Epub)
        {
            var epubInfo = await host.EpubConverter.ExtractComicInfoAsync(path, TestContext.Current.CancellationToken);
            Assert.NotNull(epubInfo);
            return;
        }

        var comicInfo = await host.ComicConverter.ExtractComicInfoAsync(path);
        Assert.SkipUnless(ArchiveInspector.HasComicInfo(path), "The archive has no ComicInfo.xml.");
        Assert.True(comicInfo is not null, "The archive contains a ComicInfo.xml, but it couldn't be parsed");

        // What was read can be written and read again
        using var stream = new MemoryStream();
        ComicInfoXmlService.Write(stream, comicInfo);
        stream.Position = 0;
        var copy = ComicInfoXmlService.Read(stream);
        Assert.NotNull(copy);
        // Empty values are not written
        Assert.Equal(NullIfEmpty(comicInfo.Title), copy.Title);
        Assert.Equal(NullIfEmpty(comicInfo.Series), copy.Series);
        Assert.Equal(NullIfEmpty(comicInfo.Number), copy.Number);
        Assert.Equal(comicInfo.Pages?.Count ?? 0, copy.Pages?.Count ?? 0);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// EPUB parsing, resource extraction and HTML preparation are real, only the WebView rendering is faked
    /// (one page per chapter).
    /// </summary>
    [Theory]
    [MemberData(nameof(ComicFiles))]
    public async Task EpubConversion_Works(string relativePath)
    {
        var path = ResolveOrSkip(relativePath);
        Assert.SkipUnless(ComicReaderService.GetComicFormat(path) == ComicFormat.Epub, "Not an EPUB.");

        await using var host = await TestServiceHost.CreateAsync();

        using var importData = await host.EpubConverter.ConvertEpubToCbzForImportAsync(
            path, host.Temp.Combine("converted"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(importData.PageCount > 0, "No chapter with content found");
        Assert.True(File.Exists(importData.FilePath));
        var session = Assert.Single(host.WebView.Sessions);
        Assert.True(session.MissingResources.Count == 0,
            $"Images referenced by the chapters were not extracted:{Environment.NewLine}{string.Join(Environment.NewLine, session.MissingResources.Distinct())}");
    }

    /// <summary>
    /// Lists archive entries with the same libraries StripWolf.Core uses
    /// </summary>
    private static class ArchiveInspector
    {
        public static bool HasComicInfo(string path)
        {
            return GetEntryNames(path).Any(name => ComicConstants.IsComicInfoFile(name) && !ComicConstants.IsIgnoredImportPath(name));
        }

        private static List<string> GetEntryNames(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            switch (ComicReaderService.GetComicFormat(path))
            {
                case ComicFormat.Cbz:
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    return archive.Entries.Select(entry => entry.FullName).ToList();
                }
                case ComicFormat.Cbr:
                {
                    using var archive = RarArchive.OpenArchive(stream);
                    return archive.Entries.Where(entry => !entry.IsDirectory).Select(entry => entry.Key ?? string.Empty).ToList();
                }
                case ComicFormat.Cb7:
                {
                    using var archive = SevenZipArchive.OpenArchive(stream);
                    return archive.Entries.Where(entry => !entry.IsDirectory).Select(entry => entry.Key ?? string.Empty).ToList();
                }
                case ComicFormat.Cbt:
                {
                    using var archive = TarArchive.OpenArchive(stream);
                    return archive.Entries.Where(entry => !entry.IsDirectory).Select(entry => entry.Key ?? string.Empty).ToList();
                }
                default:
                    return [];
            }
        }
    }
}
