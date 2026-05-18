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

using System.Xml.Serialization;

namespace StripWolf.Core.Models;

/// <summary>
/// Represents metadata from a ComicInfo.xml file.
/// Based on the ComicInfo.xml schema v2.0 from https://anansi-project.github.io/docs/comicinfo/schemas/v2.0
/// </summary>
[XmlRoot("ComicInfo", Namespace = "")]
public class ComicInfo
{
    /// <summary>
    /// Title of the comic
    /// </summary>
    [XmlElement("Title")]
    public string? Title { get; set; }

    /// <summary>
    /// Series name
    /// </summary>
    [XmlElement("Series")]
    public string? Series { get; set; }

    /// <summary>
    /// Issue number (can be a float like 1.5)
    /// </summary>
    [XmlElement("Number")]
    public string? Number { get; set; }

    /// <summary>
    /// Total number of issues in the series
    /// </summary>
    [XmlElement("Count")]
    public int? Count { get; set; }

    /// <summary>
    /// Volume number
    /// </summary>
    [XmlElement("Volume")]
    public int? Volume { get; set; }

    /// <summary>
    /// Alternate series name
    /// </summary>
    [XmlElement("AlternateSeries")]
    public string? AlternateSeries { get; set; }

    /// <summary>
    /// Alternate issue number
    /// </summary>
    [XmlElement("AlternateNumber")]
    public string? AlternateNumber { get; set; }

    /// <summary>
    /// Total number of issues in the alternate series
    /// </summary>
    [XmlElement("AlternateCount")]
    public int? AlternateCount { get; set; }

    /// <summary>
    /// Summary/description of the issue
    /// </summary>
    [XmlElement("Summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// Notes about the issue
    /// </summary>
    [XmlElement("Notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Publication year
    /// </summary>
    [XmlElement("Year")]
    public int? Year { get; set; }

    /// <summary>
    /// Publication month
    /// </summary>
    [XmlElement("Month")]
    public int? Month { get; set; }

    /// <summary>
    /// Publication day
    /// </summary>
    [XmlElement("Day")]
    public int? Day { get; set; }

    /// <summary>
    /// Writer(s) of the comic
    /// </summary>
    [XmlElement("Writer")]
    public string? Writer { get; set; }

    /// <summary>
    /// Penciller(s) of the comic
    /// </summary>
    [XmlElement("Penciller")]
    public string? Penciller { get; set; }

    /// <summary>
    /// Inker(s) of the comic
    /// </summary>
    [XmlElement("Inker")]
    public string? Inker { get; set; }

    /// <summary>
    /// Colorist(s) of the comic
    /// </summary>
    [XmlElement("Colorist")]
    public string? Colorist { get; set; }

    /// <summary>
    /// Letterer(s) of the comic
    /// </summary>
    [XmlElement("Letterer")]
    public string? Letterer { get; set; }

    /// <summary>
    /// Cover artist(s) of the comic
    /// </summary>
    [XmlElement("CoverArtist")]
    public string? CoverArtist { get; set; }

    /// <summary>
    /// Editor(s) of the comic
    /// </summary>
    [XmlElement("Editor")]
    public string? Editor { get; set; }

    /// <summary>
    /// Publisher name
    /// </summary>
    [XmlElement("Publisher")]
    public string? Publisher { get; set; }

    /// <summary>
    /// Imprint name (subdivision of publisher)
    /// </summary>
    [XmlElement("Imprint")]
    public string? Imprint { get; set; }

    /// <summary>
    /// Genre(s) of the comic
    /// </summary>
    [XmlElement("Genre")]
    public string? Genre { get; set; }

    /// <summary>
    /// Tags/keywords
    /// </summary>
    [XmlElement("Tags")]
    public string? Tags { get; set; }

    /// <summary>
    /// Web link/URL
    /// </summary>
    [XmlElement("Web")]
    public string? Web { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    [XmlElement("PageCount")]
    public int? PageCount { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "ja")
    /// </summary>
    [XmlElement("LanguageISO")]
    public string? LanguageISO { get; set; }

    /// <summary>
    /// Format of the comic (e.g., "TPB", "HC", "Annual")
    /// </summary>
    [XmlElement("Format")]
    public string? Format { get; set; }

    /// <summary>
    /// Whether this is a black and white comic
    /// </summary>
    [XmlElement("BlackAndWhite")]
    public YesNo? BlackAndWhite { get; set; }

    /// <summary>
    /// Whether this is a manga
    /// </summary>
    [XmlElement("Manga")]
    public YesNo? Manga { get; set; }

    /// <summary>
    /// Main character(s) in the comic
    /// </summary>
    [XmlElement("Characters")]
    public string? Characters { get; set; }

    /// <summary>
    /// Team(s) featured in the comic
    /// </summary>
    [XmlElement("Teams")]
    public string? Teams { get; set; }

    /// <summary>
    /// Location(s) in the comic
    /// </summary>
    [XmlElement("Locations")]
    public string? Locations { get; set; }

    /// <summary>
    /// Name of the story arc
    /// </summary>
    [XmlElement("StoryArc")]
    public string? StoryArc { get; set; }

    /// <summary>
    /// Story arc number
    /// </summary>
    [XmlElement("StoryArcNumber")]
    public string? StoryArcNumber { get; set; }

    /// <summary>
    /// Position in series
    /// </summary>
    [XmlElement("SeriesGroup")]
    public string? SeriesGroup { get; set; }

    /// <summary>
    /// Age rating for the content
    /// </summary>
    [XmlElement("AgeRating")]
    public AgeRating? AgeRating { get; set; }

    /// <summary>
    /// Community rating (0-5)
    /// </summary>
    [XmlElement("CommunityRating")]
    public decimal? CommunityRating { get; set; }

    /// <summary>
    /// Scan information (who scanned, scanner type, etc.)
    /// </summary>
    [XmlElement("ScanInformation")]
    public string? ScanInformation { get; set; }

    /// <summary>
    /// Page information
    /// </summary>
    [XmlArray("Pages")]
    [XmlArrayItem("Page")]
    public List<ComicPageInfo>? Pages { get; set; }

    /// <summary>
    /// Gets the combined authors string from all creator fields
    /// </summary>
    public string GetAuthors()
    {
        var authors = new List<string>();
        if (!string.IsNullOrEmpty(Writer)) authors.Add($"Writer: {Writer}");
        if (!string.IsNullOrEmpty(Penciller)) authors.Add($"Penciller: {Penciller}");
        if (!string.IsNullOrEmpty(Inker)) authors.Add($"Inker: {Inker}");
        if (!string.IsNullOrEmpty(Colorist)) authors.Add($"Colorist: {Colorist}");
        if (!string.IsNullOrEmpty(Letterer)) authors.Add($"Letterer: {Letterer}");
        if (!string.IsNullOrEmpty(CoverArtist)) authors.Add($"Cover: {CoverArtist}");
        if (!string.IsNullOrEmpty(Editor)) authors.Add($"Editor: {Editor}");
        return string.Join(", ", authors);
    }

    /// <summary>
    /// Gets the release date from Year, Month, Day fields
    /// </summary>
    public DateTime? GetReleaseDate()
    {
        if (!Year.HasValue) return null;
        var month = Month ?? 1;
        var day = Day ?? 1;
        
        // Validate ranges before creating DateTime
        if (Year.Value < 1 || Year.Value > 9999 ||
            month < 1 || month > 12 ||
            day < 1 || day > DateTime.DaysInMonth(Year.Value, month))
        {
            return null;
        }
        
        return new DateTime(Year.Value, month, day);
    }

    /// <summary>
    /// Gets a simple authors list (Writer is usually the main author)
    /// </summary>
    public string GetSimpleAuthors()
    {
        var authors = new List<string>();
        if (!string.IsNullOrEmpty(Writer)) authors.AddRange(Writer.Split(',', StringSplitOptions.TrimEntries));
        if (!string.IsNullOrEmpty(Penciller)) authors.AddRange(Penciller.Split(',', StringSplitOptions.TrimEntries));
        return string.Join(", ", authors.Distinct());
    }

    #region ShouldSerialize methods for nullable value types

    /// <summary>Determines if Count should be serialized</summary>
    public bool ShouldSerializeCount() => Count.HasValue;

    /// <summary>Determines if Volume should be serialized</summary>
    public bool ShouldSerializeVolume() => Volume.HasValue;

    /// <summary>Determines if AlternateCount should be serialized</summary>
    public bool ShouldSerializeAlternateCount() => AlternateCount.HasValue;

    /// <summary>Determines if Year should be serialized</summary>
    public bool ShouldSerializeYear() => Year.HasValue;

    /// <summary>Determines if Month should be serialized</summary>
    public bool ShouldSerializeMonth() => Month.HasValue;

    /// <summary>Determines if Day should be serialized</summary>
    public bool ShouldSerializeDay() => Day.HasValue;

    /// <summary>Determines if PageCount should be serialized</summary>
    public bool ShouldSerializePageCount() => PageCount.HasValue;

    /// <summary>Determines if BlackAndWhite should be serialized</summary>
    public bool ShouldSerializeBlackAndWhite() => BlackAndWhite.HasValue;

    /// <summary>Determines if Manga should be serialized</summary>
    public bool ShouldSerializeManga() => Manga.HasValue;

    /// <summary>Determines if AgeRating should be serialized</summary>
    public bool ShouldSerializeAgeRating() => AgeRating.HasValue;

    /// <summary>Determines if CommunityRating should be serialized</summary>
    public bool ShouldSerializeCommunityRating() => CommunityRating.HasValue;

    /// <summary>Determines if Pages should be serialized</summary>
    public bool ShouldSerializePages() => Pages is { Count: > 0 };

    #endregion
}

