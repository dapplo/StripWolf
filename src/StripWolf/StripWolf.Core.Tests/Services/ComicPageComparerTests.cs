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

using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

public sealed class ComicPageComparerTests
{
    private static int Compare(string? x, string? y) => ComicPageComparer.Instance.Compare(x, y);

    [Theory]
    [InlineData("page2.jpg", "page10.jpg")]
    [InlineData("page9.jpg", "page10.jpg")]
    [InlineData("page1.jpg", "page2.png")]
    [InlineData("Page1.jpg", "page2.jpg")]
    [InlineData("page1.jpg", "PAGE2.jpg")]
    [InlineData("page3-4.jpg", "page10.jpg")]
    [InlineData("cover.jpg", "page1.jpg")]
    [InlineData("page", "page1")]
    [InlineData("001.jpg", "002.jpg")]
    [InlineData("1.jpg", "02.jpg")]
    [InlineData("ch2/p10.jpg", "ch10/p1.jpg")]
    [InlineData("ch2/p2.jpg", "ch2/p10.jpg")]
    [InlineData("ch2\\p2.jpg", "ch2/p10.jpg")]
    [InlineData("Vol 2/Chapter 9/01.jpg", "Vol 10/Chapter 1/01.jpg")]
    public void Compare_SortsNaturally(string smaller, string larger)
    {
        Assert.True(Compare(smaller, larger) < 0, $"Expected '{smaller}' < '{larger}'");
        Assert.True(Compare(larger, smaller) > 0, $"Expected '{larger}' > '{smaller}'");
    }

    [Theory]
    [InlineData("page1.jpg", "page1.jpg")]
    [InlineData("PAGE1.JPG", "page1.jpg")]
    [InlineData("dir\\page1.jpg", "dir/page1.jpg")]
    public void Compare_EqualNames_ReturnsZero(string x, string y)
    {
        Assert.Equal(0, Compare(x, y));
    }

    [Fact]
    public void Compare_HandlesNull()
    {
        Assert.Equal(0, Compare(null, null));
        Assert.True(Compare(null, "a.jpg") < 0);
        Assert.True(Compare("a.jpg", null) > 0);
    }

    [Fact]
    public void Compare_NumbersLargerThanLong_DoNotThrow()
    {
        var huge = "page" + new string('9', 40) + ".jpg";

        Assert.True(Compare(huge, "page1.jpg") > 0);
        Assert.True(Compare("page1.jpg", huge) < 0);
    }

    [Fact]
    public void Compare_FolderContents_SortBeforeAFileWithTheSameName()
    {
        // At the same level a directory sorts before a file with the same name
        Assert.True(Compare("a/1.jpg", "a") < 0);
        Assert.True(Compare("a", "a/1.jpg") > 0);
    }

    [Fact]
    public void OrderBy_ProducesReadingOrder()
    {
        string[] unsorted = ["page10.jpg", "Page3.jpg", "page2.jpg", "cover.jpg", "page1.jpg", "page11.jpg", "page20.jpg"];

        var sorted = unsorted.OrderBy(name => name, ComicPageComparer.Instance).ToArray();

        Assert.Equal(new[] { "cover.jpg", "page1.jpg", "page2.jpg", "Page3.jpg", "page10.jpg", "page11.jpg", "page20.jpg" }, sorted);
    }
}
