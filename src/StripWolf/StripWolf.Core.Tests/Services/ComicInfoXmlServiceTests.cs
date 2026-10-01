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
using System.Xml.Linq;
using StripWolf.Core.Models;
using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

public sealed class ComicInfoXmlServiceTests
{
    private static ComicInfo? Read(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return ComicInfoXmlService.Read(stream);
    }

    private static string Write(ComicInfo info)
    {
        using var stream = new MemoryStream();
        ComicInfoXmlService.Write(stream, info);
        // The writer emits a UTF-8 BOM
        return Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
    }

    private static ComicInfo RoundTrip(ComicInfo info)
    {
        using var stream = new MemoryStream();
        ComicInfoXmlService.Write(stream, info);
        stream.Position = 0;
        return ComicInfoXmlService.Read(stream) ?? throw new InvalidOperationException("Could not read the written ComicInfo");
    }

    [Fact]
    public void Read_FixtureComicInfo_ReadsAllFields()
    {
        var xml = TestFiles.ReadZipEntry(TestFiles.Get(TestFiles.CbzWithComicInfo), "ComicInfo.xml");
        using var stream = new MemoryStream(xml);

        var info = ComicInfoXmlService.Read(stream);

        Assert.NotNull(info);
        Assert.Equal("The Wolf Strip", info.Title);
        Assert.Equal("StripWolf Test Series", info.Series);
        Assert.Equal("7", info.Number);
        Assert.Equal(12, info.Count);
        Assert.Equal(2, info.Volume);
        Assert.Equal("A synthetic comic used by the automated tests.", info.Summary);
        Assert.Equal(2024, info.Year);
        Assert.Equal(5, info.Month);
        Assert.Equal(17, info.Day);
        Assert.Equal("Alice Writer", info.Writer);
        Assert.Equal("Bob Penciller", info.Penciller);
        Assert.Equal("Dapplo Test Press", info.Publisher);
        Assert.Equal(4, info.PageCount);
        Assert.Equal("en", info.LanguageISO);
        Assert.Equal(YesNo.No, info.Manga);

        Assert.NotNull(info.Pages);
        Assert.Equal(2, info.Pages.Count);
        Assert.Equal(0, info.Pages[0].Image);
        Assert.Equal(ComicPageType.FrontCover, info.Pages[0].Type);
        Assert.Equal(300, info.Pages[0].ImageWidth);
        Assert.Equal(450, info.Pages[0].ImageHeight);
        Assert.False(info.Pages[0].DoublePage);
        Assert.Equal(2, info.Pages[1].Image);
        Assert.True(info.Pages[1].DoublePage);
        Assert.Equal(600, info.Pages[1].ImageWidth);
    }

    /// <summary>
    /// Regression: ReadElementContentAsString already moves to the next node, the old read loop then skipped it,
    /// so every second of the directly following elements was lost.
    /// </summary>
    [Fact]
    public void Read_ConsecutiveElementsWithoutWhitespace_ReadsAllOfThem()
    {
        var info = Read("<ComicInfo><Title>T</Title><Series>S</Series><Number>3</Number><Volume>4</Volume><Writer>W</Writer><Penciller>P</Penciller></ComicInfo>");

        Assert.NotNull(info);
        Assert.Equal("T", info.Title);
        Assert.Equal("S", info.Series);
        Assert.Equal("3", info.Number);
        Assert.Equal(4, info.Volume);
        Assert.Equal("W", info.Writer);
        Assert.Equal("P", info.Penciller);
    }

    [Fact]
    public void Read_ConsecutiveEmptyElements_ReadsAllOfThem()
    {
        var info = Read("<ComicInfo><Title/><Series/><Number/><Writer>W</Writer><Summary></Summary><Publisher>Pub</Publisher></ComicInfo>");

        Assert.NotNull(info);
        Assert.Equal(string.Empty, info.Title);
        Assert.Equal(string.Empty, info.Series);
        Assert.Equal(string.Empty, info.Number);
        Assert.Equal("W", info.Writer);
        Assert.Equal(string.Empty, info.Summary);
        Assert.Equal("Pub", info.Publisher);
    }

    [Fact]
    public void Read_UnknownElements_AreSkippedWithTheirChildren()
    {
        var info = Read("""
            <?xml version="1.0" encoding="utf-8"?>
            <ComicInfo xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <Unknown><Title>Not this one</Title><Deeper><Series>Nor this</Series></Deeper></Unknown>
              <Title>Real title</Title><Empty/><Series>Real series</Series>
              <!-- a comment -->
              <GTIN>1234567890123</GTIN>
              <Number>1</Number>
            </ComicInfo>
            """);

        Assert.NotNull(info);
        Assert.Equal("Real title", info.Title);
        Assert.Equal("Real series", info.Series);
        Assert.Equal("1", info.Number);
    }

    [Fact]
    public void Read_ElementsAfterPages_AreRead()
    {
        var info = Read("<ComicInfo><Pages><Page Image=\"0\" Type=\"FrontCover\"/><Page Image=\"1\" Bookmark=\"Chapter 1\"/></Pages><Title>After pages</Title><Pages/><Series>After empty pages</Series></ComicInfo>");

        Assert.NotNull(info);
        Assert.Equal("After pages", info.Title);
        Assert.Equal("After empty pages", info.Series);
    }

    [Fact]
    public void Read_Pages_ReadsAllAttributes()
    {
        var info = Read("""
            <ComicInfo>
              <Pages>
                <Page Image="0" Type="FrontCover" ImageWidth="1000" ImageHeight="1500" ImageSize="123456" />
                <Page Image="5" Type="Story" DoublePage="Yes" Bookmark="Chapter 2" />
                <Page Image="6" Type="NoSuchType" DoublePage="false" />
              </Pages>
            </ComicInfo>
            """);

        Assert.NotNull(info);
        Assert.NotNull(info.Pages);
        Assert.Equal(3, info.Pages.Count);
        Assert.Equal(ComicPageType.FrontCover, info.Pages[0].Type);
        Assert.Equal(1000, info.Pages[0].ImageWidth);
        Assert.Equal(1500, info.Pages[0].ImageHeight);
        Assert.Equal(123456L, info.Pages[0].ImageSize);
        Assert.Equal(5, info.Pages[1].Image);
        Assert.True(info.Pages[1].DoublePage);
        Assert.Equal("Chapter 2", info.Pages[1].Bookmark);
        Assert.Equal(ComicPageType.Story, info.Pages[1].Type);
        Assert.Null(info.Pages[2].Type);
        Assert.False(info.Pages[2].DoublePage);
    }

    [Fact]
    public void Read_InvalidNumbers_AreIgnored()
    {
        var info = Read("<ComicInfo><Count>many</Count><Year>2020</Year><Month>May</Month><CommunityRating>4.5</CommunityRating><Manga>Maybe</Manga></ComicInfo>");

        Assert.NotNull(info);
        Assert.Null(info.Count);
        Assert.Equal(2020, info.Year);
        Assert.Null(info.Month);
        Assert.Equal(4.5m, info.CommunityRating);
        Assert.Null(info.Manga);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<Other><Title>x</Title></Other>")]
    [InlineData("<ComicInfo><Title>unclosed")]
    public void Read_InvalidDocuments_ReturnNull(string xml)
    {
        Assert.Null(Read(xml));
    }

    [Fact]
    public void Write_OnlyWritesSetValues()
    {
        var xml = Write(new ComicInfo { Title = "Only a title", Pages = [] });

        var root = XDocument.Parse(xml).Root;
        Assert.NotNull(root);
        Assert.Equal("ComicInfo", root.Name.LocalName);
        Assert.Equal(new[] { "Title" }, root.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void Write_LeavesTheStreamOpen()
    {
        using var stream = new MemoryStream();

        ComicInfoXmlService.Write(stream, new ComicInfo { Title = "Open" });

        Assert.True(stream.CanWrite);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void WriteAndRead_RoundTripsAllFields()
    {
        var original = CreateFullComicInfo();

        var copy = RoundTrip(original);

        AssertEquivalent(original, copy);
        // Writing the copy produces exactly the same document
        Assert.Equal(Write(original), Write(copy));
    }

    [Fact]
    public void WriteAndRead_IsCultureInvariant()
    {
        var original = CreateFullComicInfo();
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // German uses a comma as decimal separator
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var xml = Write(original);
            var copy = RoundTrip(original);

            Assert.Contains("<CommunityRating>4.5</CommunityRating>", xml);
            Assert.Equal(4.5m, copy.CommunityRating);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    public static TheoryData<AgeRating> AllAgeRatings()
    {
        var data = new TheoryData<AgeRating>();
        foreach (var rating in Enum.GetValues<AgeRating>())
        {
            data.Add(rating);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllAgeRatings))]
    public void WriteAndRead_AgeRating_RoundTrips(AgeRating rating)
    {
        var copy = RoundTrip(new ComicInfo { AgeRating = rating });

        Assert.Equal(rating, copy.AgeRating);
    }

    [Theory]
    [InlineData(AgeRating.AdultsOnly18Plus, "Adults Only 18+")]
    [InlineData(AgeRating.Everyone10Plus, "Everyone 10+")]
    [InlineData(AgeRating.KidsToAdults, "Kids to Adults")]
    [InlineData(AgeRating.MA15Plus, "MA15+")]
    [InlineData(AgeRating.R18Plus, "R18+")]
    [InlineData(AgeRating.X18Plus, "X18+")]
    [InlineData(AgeRating.RatingPending, "Rating Pending")]
    [InlineData(AgeRating.Teen, "Teen")]
    public void Write_AgeRating_UsesTheSchemaValue(AgeRating rating, string expected)
    {
        var xml = Write(new ComicInfo { AgeRating = rating });

        Assert.Equal(expected, XDocument.Parse(xml).Root?.Element("AgeRating")?.Value);
        Assert.Equal(expected, ComicInfoXmlService.GetAgeRatingString(rating));
    }

    [Theory]
    [InlineData(" Teen ", true, AgeRating.Teen)]
    [InlineData("EVERYONE", true, AgeRating.Everyone)]
    [InlineData("Everyone10Plus", true, AgeRating.Everyone10Plus)]
    [InlineData("", false, AgeRating.Unknown)]
    [InlineData("   ", false, AgeRating.Unknown)]
    [InlineData(null, false, AgeRating.Unknown)]
    [InlineData("Not a rating", false, AgeRating.Unknown)]
    [InlineData("99", false, AgeRating.Unknown)]
    public void TryParseAgeRating(string? value, bool expectedResult, AgeRating expectedRating)
    {
        var result = ComicInfoXmlService.TryParseAgeRating(value, out var rating);

        Assert.Equal(expectedResult, result);
        if (expectedResult)
        {
            Assert.Equal(expectedRating, rating);
        }
    }

    [Fact]
    public void Read_UnknownAgeRating_IsIgnored()
    {
        var info = Read("<ComicInfo><AgeRating>Ages 3 and up</AgeRating><Title>T</Title></ComicInfo>");

        Assert.NotNull(info);
        Assert.Null(info.AgeRating);
        Assert.Equal("T", info.Title);
    }

    /// <summary>
    /// Regression: child markup inside a text element (HTML in a Summary) made ReadElementContentAsString throw,
    /// and the whole ComicInfo was lost. The text of the children is kept.
    /// </summary>
    [Fact]
    public void Read_ChildMarkupInTextElements_KeepsTheText()
    {
        var info = Read("<ComicInfo><Summary>a<b>x</b> y</Summary><Title>After</Title><Notes>line<br/>break</Notes><Writer><i>Alice</i></Writer><Series>S</Series></ComicInfo>");

        Assert.NotNull(info);
        Assert.Equal("ax y", info.Summary);
        Assert.Equal("After", info.Title);
        Assert.Equal("linebreak", info.Notes);
        Assert.Equal("Alice", info.Writer);
        Assert.Equal("S", info.Series);
    }

    [Fact]
    public void Read_NestedChildMarkupAndCData()
    {
        var info = Read("<ComicInfo><Summary><p>One <b>two <i>three</i></b></p><p>four</p></Summary><Notes><![CDATA[<raw> & text]]></Notes><Count>3</Count></ComicInfo>");

        Assert.NotNull(info);
        Assert.Equal("One two threefour", info.Summary);
        Assert.Equal("<raw> & text", info.Notes);
        Assert.Equal(3, info.Count);
    }

    [Fact]
    public void Read_NumberWithChildMarkup_IsParsed()
    {
        var info = Read("<ComicInfo><Year><span>2021</span></Year><Month>4</Month></ComicInfo>");

        Assert.NotNull(info);
        Assert.Equal(2021, info.Year);
        Assert.Equal(4, info.Month);
    }

    /// <summary>
    /// Regression: the reader only removed the spaces and used a case sensitive Enum.TryParse, so the schema values
    /// ("Adults Only 18+", "Kids to Adults", "MA15+", ...) were not recognized and the rating was lost.
    /// </summary>
    [Theory]
    [InlineData("MA15Plus", AgeRating.MA15Plus)]
    [InlineData("kids to adults", AgeRating.KidsToAdults)]
    [InlineData("Adults Only 18+", AgeRating.AdultsOnly18Plus)]
    [InlineData("Everyone 10+", AgeRating.Everyone10Plus)]
    [InlineData("Kids to Adults", AgeRating.KidsToAdults)]
    [InlineData("Mature 17+", AgeRating.Mature17Plus)]
    [InlineData("MA15+", AgeRating.MA15Plus)]
    [InlineData("R18+", AgeRating.R18Plus)]
    [InlineData("X18+", AgeRating.X18Plus)]
    public void Read_AgeRating_SchemaValues(string value, AgeRating expected)
    {
        var info = Read($"<ComicInfo><AgeRating>{value}</AgeRating></ComicInfo>");

        Assert.Equal(expected, info?.AgeRating);
    }

    private static ComicInfo CreateFullComicInfo()
    {
        return new ComicInfo
        {
            Title = "Title & <Special> \"chars\"",
            Series = "Series",
            Number = "12.5",
            Count = 50,
            Volume = 3,
            AlternateSeries = "Alt series",
            AlternateNumber = "7",
            AlternateCount = 9,
            Summary = "Line 1\nLine 2",
            Notes = "Notes",
            Year = 1999,
            Month = 12,
            Day = 31,
            Writer = "Writer A, Writer B",
            Penciller = "Penciller",
            Inker = "Inker",
            Colorist = "Colorist",
            Letterer = "Letterer",
            CoverArtist = "Cover artist",
            Editor = "Editor",
            Publisher = "Publisher",
            Imprint = "Imprint",
            Genre = "Genre",
            Tags = "tag1, tag2",
            Web = "https://example.com/comic",
            PageCount = 3,
            LanguageISO = "nl",
            Format = "Trade Paperback",
            BlackAndWhite = YesNo.Yes,
            Manga = YesNo.No,
            PageProgressionDirection = "rtl",
            Characters = "Wolf",
            Teams = "Pack",
            Locations = "Forest",
            StoryArc = "Arc",
            StoryArcNumber = "2",
            SeriesGroup = "Group",
            AgeRating = AgeRating.Teen,
            CommunityRating = 4.5m,
            ScanInformation = "Scanned",
            Pages =
            [
                new ComicPageInfo { Image = 0, Type = ComicPageType.FrontCover, ImageWidth = 300, ImageHeight = 450, ImageSize = 5448 },
                new ComicPageInfo { Image = 1, Type = ComicPageType.Story, DoublePage = true, Bookmark = "Start" },
                new ComicPageInfo { Image = 2 }
            ]
        };
    }

    private static void AssertEquivalent(ComicInfo expected, ComicInfo actual)
    {
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Series, actual.Series);
        Assert.Equal(expected.Number, actual.Number);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Volume, actual.Volume);
        Assert.Equal(expected.AlternateSeries, actual.AlternateSeries);
        Assert.Equal(expected.AlternateNumber, actual.AlternateNumber);
        Assert.Equal(expected.AlternateCount, actual.AlternateCount);
        Assert.Equal(expected.Summary, actual.Summary);
        Assert.Equal(expected.Notes, actual.Notes);
        Assert.Equal(expected.Year, actual.Year);
        Assert.Equal(expected.Month, actual.Month);
        Assert.Equal(expected.Day, actual.Day);
        Assert.Equal(expected.Writer, actual.Writer);
        Assert.Equal(expected.Penciller, actual.Penciller);
        Assert.Equal(expected.Inker, actual.Inker);
        Assert.Equal(expected.Colorist, actual.Colorist);
        Assert.Equal(expected.Letterer, actual.Letterer);
        Assert.Equal(expected.CoverArtist, actual.CoverArtist);
        Assert.Equal(expected.Editor, actual.Editor);
        Assert.Equal(expected.Publisher, actual.Publisher);
        Assert.Equal(expected.Imprint, actual.Imprint);
        Assert.Equal(expected.Genre, actual.Genre);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.Web, actual.Web);
        Assert.Equal(expected.PageCount, actual.PageCount);
        Assert.Equal(expected.LanguageISO, actual.LanguageISO);
        Assert.Equal(expected.Format, actual.Format);
        Assert.Equal(expected.BlackAndWhite, actual.BlackAndWhite);
        Assert.Equal(expected.Manga, actual.Manga);
        Assert.Equal(expected.PageProgressionDirection, actual.PageProgressionDirection);
        Assert.Equal(expected.Characters, actual.Characters);
        Assert.Equal(expected.Teams, actual.Teams);
        Assert.Equal(expected.Locations, actual.Locations);
        Assert.Equal(expected.StoryArc, actual.StoryArc);
        Assert.Equal(expected.StoryArcNumber, actual.StoryArcNumber);
        Assert.Equal(expected.SeriesGroup, actual.SeriesGroup);
        Assert.Equal(expected.AgeRating, actual.AgeRating);
        Assert.Equal(expected.CommunityRating, actual.CommunityRating);
        Assert.Equal(expected.ScanInformation, actual.ScanInformation);

        Assert.NotNull(actual.Pages);
        Assert.Equal(expected.Pages!.Count, actual.Pages.Count);
        for (var index = 0; index < expected.Pages.Count; index++)
        {
            Assert.Equal(expected.Pages[index].Image, actual.Pages[index].Image);
            Assert.Equal(expected.Pages[index].Type, actual.Pages[index].Type);
            Assert.Equal(expected.Pages[index].DoublePage, actual.Pages[index].DoublePage);
            Assert.Equal(expected.Pages[index].ImageWidth, actual.Pages[index].ImageWidth);
            Assert.Equal(expected.Pages[index].ImageHeight, actual.Pages[index].ImageHeight);
            Assert.Equal(expected.Pages[index].ImageSize, actual.Pages[index].ImageSize);
            Assert.Equal(expected.Pages[index].Bookmark, actual.Pages[index].Bookmark);
        }
    }
}
