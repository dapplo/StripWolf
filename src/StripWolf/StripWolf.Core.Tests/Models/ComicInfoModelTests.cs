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

public sealed class ComicInfoModelTests
{
    [Theory]
    [InlineData(2024, 5, 17, "2024-05-17")]
    [InlineData(2024, null, null, "2024-01-01")]
    [InlineData(2024, 2, 29, "2024-02-29")]
    [InlineData(2023, 2, 29, null)]
    [InlineData(2024, 13, 1, null)]
    [InlineData(2024, 0, 1, null)]
    [InlineData(0, 1, 1, null)]
    [InlineData(null, 5, 17, null)]
    public void GetReleaseDate_ValidatesTheDate(int? year, int? month, int? day, string? expected)
    {
        var info = new ComicInfo { Year = year, Month = month, Day = day };

        var releaseDate = info.GetReleaseDate();

        Assert.Equal(expected, releaseDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetSimpleAuthors_SplitsAndDeduplicates()
    {
        var info = new ComicInfo { Writer = "Alice, Bob", Penciller = "Bob,Carol", Inker = "Not included" };

        Assert.Equal("Alice, Bob, Carol", info.GetSimpleAuthors());
        Assert.Equal(string.Empty, new ComicInfo().GetSimpleAuthors());
    }

    [Fact]
    public void GetAuthors_ListsTheRoles()
    {
        var info = new ComicInfo { Writer = "Alice", CoverArtist = "Dave" };

        Assert.Equal("Writer: Alice, Cover: Dave", info.GetAuthors());
    }

    [Fact]
    public void ComicPageInfo_TypeString_ParsesKnownTypesOnly()
    {
        var page = new ComicPageInfo { TypeString = "BackCover" };
        Assert.Equal(ComicPageType.BackCover, page.Type);
        Assert.Equal("BackCover", page.TypeString);

        page.TypeString = "Unknown thing";
        Assert.Null(page.Type);
        Assert.Null(page.TypeString);

        page.TypeString = null;
        Assert.Null(page.Type);
    }

    [Theory]
    [InlineData("page.jpg", true)]
    [InlineData("PAGE.JPEG", true)]
    [InlineData("page.webp", true)]
    [InlineData("page.avif", true)]
    [InlineData("dir/page.png", true)]
    [InlineData("ComicInfo.xml", false)]
    [InlineData("Thumbs.db", false)]
    [InlineData("page", false)]
    public void ComicConstants_IsImageFile(string fileName, bool expected)
    {
        Assert.Equal(expected, ComicConstants.IsImageFile(fileName));
    }

    [Theory]
    [InlineData("__MACOSX/._page1.jpg", true)]
    [InlineData("comic/__macosx/page1.jpg", true)]
    [InlineData("C:\\comics\\__MACOSX\\page1.jpg", true)]
    [InlineData("comic/page1.jpg", false)]
    [InlineData("MACOSX/page1.jpg", false)]
    [InlineData("", false)]
    public void ComicConstants_IsIgnoredImportPath(string path, bool expected)
    {
        Assert.Equal(expected, ComicConstants.IsIgnoredImportPath(path));
    }

    [Theory]
    [InlineData("ComicInfo.xml", true)]
    [InlineData("sub/comicinfo.XML", true)]
    [InlineData("ComicInfo.xml.bak", false)]
    [InlineData("MyComicInfo.xml", false)]
    public void ComicConstants_IsComicInfoFile(string fileName, bool expected)
    {
        Assert.Equal(expected, ComicConstants.IsComicInfoFile(fileName));
    }

    [Theory]
    [InlineData("a.cbz", true)]
    [InlineData("a.CBR", true)]
    [InlineData("a.cb7", true)]
    [InlineData("a.cbt", true)]
    [InlineData("a.pdf", true)]
    [InlineData("a.epub", true)]
    [InlineData("a.zip", false)]
    [InlineData("a.cba", false)]
    public void ComicConstants_IsSupportedComicFile(string fileName, bool expected)
    {
        Assert.Equal(expected, ComicConstants.IsSupportedComicFile(fileName));
    }
}
