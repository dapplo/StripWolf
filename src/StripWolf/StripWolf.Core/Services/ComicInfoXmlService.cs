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

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using StripWolf.Core.Models;

namespace StripWolf.Core.Services;

/// <summary>
/// Service for reading and writing ComicInfo.xml metadata in a Native AOT compatible way.
/// Avoids using XmlSerializer which relies on dynamic code generation.
/// </summary>
public static class ComicInfoXmlService
{
    /// <summary>
    /// Reads ComicInfo metadata from a stream using XmlReader (AOT compatible).
    /// </summary>
    public static ComicInfo? Read(Stream stream)
    {
        try
        {
            var info = new ComicInfo();
            var settings = new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true };
            using var reader = XmlReader.Create(stream, settings);

            if (!reader.ReadToFollowing("ComicInfo")) return null;

            using var subReader = reader.ReadSubtree();
            // Position on <ComicInfo> and then on its first child.
            // Note: ReadElementContentAsString() already moves the reader to the next node. The previous
            // "while (subReader.Read())" loop then skipped that node, so every second field (e.g. Series after Title)
            // was lost when the elements directly follow each other.
            subReader.Read();
            subReader.Read();
            while (!subReader.EOF)
            {
                if (subReader.NodeType != XmlNodeType.Element)
                {
                    subReader.Read();
                    continue;
                }

                var name = subReader.Name;
                switch (name)
                {
                    case "Title": info.Title = ReadElementText(subReader); break;
                    case "Series": info.Series = ReadElementText(subReader); break;
                    case "Number": info.Number = ReadElementText(subReader); break;
                    case "Count": if (int.TryParse(ReadElementText(subReader), out var count)) info.Count = count; break;
                    case "Volume": if (int.TryParse(ReadElementText(subReader), out var vol)) info.Volume = vol; break;
                    case "AlternateSeries": info.AlternateSeries = ReadElementText(subReader); break;
                    case "AlternateNumber": info.AlternateNumber = ReadElementText(subReader); break;
                    case "AlternateCount": if (int.TryParse(ReadElementText(subReader), out var acount)) info.AlternateCount = acount; break;
                    case "Summary": info.Summary = ReadElementText(subReader); break;
                    case "Notes": info.Notes = ReadElementText(subReader); break;
                    case "Year": if (int.TryParse(ReadElementText(subReader), out var year)) info.Year = year; break;
                    case "Month": if (int.TryParse(ReadElementText(subReader), out var month)) info.Month = month; break;
                    case "Day": if (int.TryParse(ReadElementText(subReader), out var day)) info.Day = day; break;
                    case "Writer": info.Writer = ReadElementText(subReader); break;
                    case "Penciller": info.Penciller = ReadElementText(subReader); break;
                    case "Inker": info.Inker = ReadElementText(subReader); break;
                    case "Colorist": info.Colorist = ReadElementText(subReader); break;
                    case "Letterer": info.Letterer = ReadElementText(subReader); break;
                    case "CoverArtist": info.CoverArtist = ReadElementText(subReader); break;
                    case "Editor": info.Editor = ReadElementText(subReader); break;
                    case "Publisher": info.Publisher = ReadElementText(subReader); break;
                    case "Imprint": info.Imprint = ReadElementText(subReader); break;
                    case "Genre": info.Genre = ReadElementText(subReader); break;
                    case "Tags": info.Tags = ReadElementText(subReader); break;
                    case "Web": info.Web = ReadElementText(subReader); break;
                    case "PageCount": if (int.TryParse(ReadElementText(subReader), out var pc)) info.PageCount = pc; break;
                    case "LanguageISO": info.LanguageISO = ReadElementText(subReader); break;
                    case "Format": info.Format = ReadElementText(subReader); break;
                    case "BlackAndWhite": if (Enum.TryParse<YesNo>(ReadElementText(subReader), out var bw)) info.BlackAndWhite = bw; break;
                    case "Manga": if (Enum.TryParse<YesNo>(ReadElementText(subReader), out var m)) info.Manga = m; break;
                    case "PageProgressionDirection": info.PageProgressionDirection = ReadElementText(subReader); break;
                    case "Characters": info.Characters = ReadElementText(subReader); break;
                    case "Teams": info.Teams = ReadElementText(subReader); break;
                    case "Locations": info.Locations = ReadElementText(subReader); break;
                    case "StoryArc": info.StoryArc = ReadElementText(subReader); break;
                    case "StoryArcNumber": info.StoryArcNumber = ReadElementText(subReader); break;
                    case "SeriesGroup": info.SeriesGroup = ReadElementText(subReader); break;
                    case "AgeRating": if (TryParseAgeRating(ReadElementText(subReader), out var ar)) info.AgeRating = ar; break;
                    case "CommunityRating": if (decimal.TryParse(ReadElementText(subReader), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cr)) info.CommunityRating = cr; break;
                    case "ScanInformation": info.ScanInformation = ReadElementText(subReader); break;
                    case "Pages":
                        info.Pages = ReadPages(subReader);
                        // ReadSubtree leaves the reader on the (end) element of Pages, move past it
                        subReader.Read();
                        break;
                    default:
                        // Unknown element: skip it including its children
                        subReader.Skip();
                        break;
                }
            }

            return info;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the text of the current element and moves past its end tag, like ReadElementContentAsString.
    /// Unlike ReadElementContentAsString it tolerates child markup (e.g. HTML in a Summary: "a&lt;b&gt;x&lt;/b&gt;" written
    /// unescaped), which used to throw and made the whole ComicInfo unreadable. The text of child elements is kept.
    /// </summary>
    private static string ReadElementText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return string.Empty;
        }

        var depth = reader.Depth;
        var text = new System.Text.StringBuilder();
        reader.Read();
        while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                text.Append(reader.Value);
            }

            reader.Read();
        }

        // Move past the end tag
        reader.Read();
        return text.ToString();
    }

    private static List<ComicPageInfo> ReadPages(XmlReader reader)
    {
        var pages = new List<ComicPageInfo>();
        using var pagesReader = reader.ReadSubtree();
        while (pagesReader.ReadToFollowing("Page"))
        {
            var page = new ComicPageInfo();
            if (int.TryParse(pagesReader.GetAttribute("Image"), out var img)) page.Image = img;
            page.TypeString = pagesReader.GetAttribute("Type");
            var dp = pagesReader.GetAttribute("DoublePage");
            page.DoublePage = dp?.Equals("Yes", StringComparison.OrdinalIgnoreCase) == true || dp?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            if (int.TryParse(pagesReader.GetAttribute("ImageWidth"), out var iw)) page.ImageWidth = iw;
            if (int.TryParse(pagesReader.GetAttribute("ImageHeight"), out var ih)) page.ImageHeight = ih;
            if (long.TryParse(pagesReader.GetAttribute("ImageSize"), out var isz)) page.ImageSize = isz;
            page.Bookmark = pagesReader.GetAttribute("Bookmark");
            pages.Add(page);
        }
        return pages;
    }

    /// <summary>
    /// Writes ComicInfo metadata to a stream using XmlWriter (AOT compatible).
    /// </summary>
    public static void Write(Stream stream, ComicInfo info)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = System.Text.Encoding.UTF8,
            OmitXmlDeclaration = false,
            CloseOutput = false // Ensure the stream remains open for the caller (important for MemoryStream and Zip entries)
        };

        using var writer = XmlWriter.Create(stream, settings);
        Write(writer, info);
        writer.Flush(); // Ensure all content is written before returning
    }

    /// <summary>
    /// Writes ComicInfo metadata using an existing XmlWriter.
    /// </summary>
    public static void Write(XmlWriter writer, ComicInfo info)
    {
        writer.WriteStartElement("ComicInfo");
        
        if (!string.IsNullOrEmpty(info.Title)) writer.WriteElementString("Title", info.Title);
        if (!string.IsNullOrEmpty(info.Series)) writer.WriteElementString("Series", info.Series);
        if (!string.IsNullOrEmpty(info.Number)) writer.WriteElementString("Number", info.Number);
        if (info.Count.HasValue) writer.WriteElementString("Count", info.Count.Value.ToString());
        if (info.Volume.HasValue) writer.WriteElementString("Volume", info.Volume.Value.ToString());
        if (!string.IsNullOrEmpty(info.AlternateSeries)) writer.WriteElementString("AlternateSeries", info.AlternateSeries);
        if (!string.IsNullOrEmpty(info.AlternateNumber)) writer.WriteElementString("AlternateNumber", info.AlternateNumber);
        if (info.AlternateCount.HasValue) writer.WriteElementString("AlternateCount", info.AlternateCount.Value.ToString());
        if (!string.IsNullOrEmpty(info.Summary)) writer.WriteElementString("Summary", info.Summary);
        if (!string.IsNullOrEmpty(info.Notes)) writer.WriteElementString("Notes", info.Notes);
        if (info.Year.HasValue) writer.WriteElementString("Year", info.Year.Value.ToString());
        if (info.Month.HasValue) writer.WriteElementString("Month", info.Month.Value.ToString());
        if (info.Day.HasValue) writer.WriteElementString("Day", info.Day.Value.ToString());
        if (!string.IsNullOrEmpty(info.Writer)) writer.WriteElementString("Writer", info.Writer);
        if (!string.IsNullOrEmpty(info.Penciller)) writer.WriteElementString("Penciller", info.Penciller);
        if (!string.IsNullOrEmpty(info.Inker)) writer.WriteElementString("Inker", info.Inker);
        if (!string.IsNullOrEmpty(info.Colorist)) writer.WriteElementString("Colorist", info.Colorist);
        if (!string.IsNullOrEmpty(info.Letterer)) writer.WriteElementString("Letterer", info.Letterer);
        if (!string.IsNullOrEmpty(info.CoverArtist)) writer.WriteElementString("CoverArtist", info.CoverArtist);
        if (!string.IsNullOrEmpty(info.Editor)) writer.WriteElementString("Editor", info.Editor);
        if (!string.IsNullOrEmpty(info.Publisher)) writer.WriteElementString("Publisher", info.Publisher);
        if (!string.IsNullOrEmpty(info.Imprint)) writer.WriteElementString("Imprint", info.Imprint);
        if (!string.IsNullOrEmpty(info.Genre)) writer.WriteElementString("Genre", info.Genre);
        if (!string.IsNullOrEmpty(info.Tags)) writer.WriteElementString("Tags", info.Tags);
        if (!string.IsNullOrEmpty(info.Web)) writer.WriteElementString("Web", info.Web);
        if (info.PageCount.HasValue) writer.WriteElementString("PageCount", info.PageCount.Value.ToString());
        if (!string.IsNullOrEmpty(info.LanguageISO)) writer.WriteElementString("LanguageISO", info.LanguageISO);
        if (!string.IsNullOrEmpty(info.Format)) writer.WriteElementString("Format", info.Format);
        if (info.BlackAndWhite.HasValue) writer.WriteElementString("BlackAndWhite", info.BlackAndWhite.Value.ToString());
        if (info.Manga.HasValue) writer.WriteElementString("Manga", info.Manga.Value.ToString());
        if (!string.IsNullOrEmpty(info.PageProgressionDirection)) writer.WriteElementString("PageProgressionDirection", info.PageProgressionDirection);
        if (!string.IsNullOrEmpty(info.Characters)) writer.WriteElementString("Characters", info.Characters);
        if (!string.IsNullOrEmpty(info.Teams)) writer.WriteElementString("Teams", info.Teams);
        if (!string.IsNullOrEmpty(info.Locations)) writer.WriteElementString("Locations", info.Locations);
        if (!string.IsNullOrEmpty(info.StoryArc)) writer.WriteElementString("StoryArc", info.StoryArc);
        if (!string.IsNullOrEmpty(info.StoryArcNumber)) writer.WriteElementString("StoryArcNumber", info.StoryArcNumber);
        if (!string.IsNullOrEmpty(info.SeriesGroup)) writer.WriteElementString("SeriesGroup", info.SeriesGroup);
        if (info.AgeRating.HasValue) writer.WriteElementString("AgeRating", GetAgeRatingString(info.AgeRating.Value));
        if (info.CommunityRating.HasValue) writer.WriteElementString("CommunityRating", info.CommunityRating.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(info.ScanInformation)) writer.WriteElementString("ScanInformation", info.ScanInformation);

        if (info.Pages is { Count: > 0 })
        {
            writer.WriteStartElement("Pages");
            foreach (var page in info.Pages)
            {
                writer.WriteStartElement("Page");
                writer.WriteAttributeString("Image", page.Image.ToString());
                if (!string.IsNullOrEmpty(page.TypeString)) writer.WriteAttributeString("Type", page.TypeString);
                if (page.DoublePage) writer.WriteAttributeString("DoublePage", "Yes");
                if (page.ImageWidth > 0) writer.WriteAttributeString("ImageWidth", page.ImageWidth.ToString());
                if (page.ImageHeight > 0) writer.WriteAttributeString("ImageHeight", page.ImageHeight.ToString());
                if (page.ImageSize > 0) writer.WriteAttributeString("ImageSize", page.ImageSize.ToString());
                if (!string.IsNullOrEmpty(page.Bookmark)) writer.WriteAttributeString("Bookmark", page.Bookmark);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    /// <summary>
    /// The AgeRating values as written in ComicInfo.xml (ComicInfo schema v2.0).
    /// </summary>
    private static readonly (AgeRating Rating, string SchemaValue)[] AgeRatingSchemaValues =
    [
        (AgeRating.Unknown, "Unknown"),
        (AgeRating.AdultsOnly18Plus, "Adults Only 18+"),
        (AgeRating.EarlyChildhood, "Early Childhood"),
        (AgeRating.Everyone, "Everyone"),
        (AgeRating.Everyone10Plus, "Everyone 10+"),
        (AgeRating.G, "G"),
        (AgeRating.KidsToAdults, "Kids to Adults"),
        (AgeRating.M, "M"),
        (AgeRating.MA15Plus, "MA15+"),
        (AgeRating.Mature17Plus, "Mature 17+"),
        (AgeRating.PG, "PG"),
        (AgeRating.R18Plus, "R18+"),
        (AgeRating.RatingPending, "Rating Pending"),
        (AgeRating.Teen, "Teen"),
        (AgeRating.X18Plus, "X18+")
    ];

    /// <summary>
    /// Previously MA15+, R18+ and X18+ were written as "MA15Plus" etc., and reading removed the spaces and parsed the
    /// enum case sensitively, so "Adults Only 18+", "Kids to Adults" and all "+" values were lost.
    /// </summary>
    internal static string GetAgeRatingString(AgeRating rating)
    {
        foreach (var (candidate, schemaValue) in AgeRatingSchemaValues)
        {
            if (candidate == rating)
            {
                return schemaValue;
            }
        }

        return rating.ToString();
    }

    internal static bool TryParseAgeRating(string? value, out AgeRating rating)
    {
        rating = AgeRating.Unknown;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var (candidate, schemaValue) in AgeRatingSchemaValues)
        {
            if (string.Equals(schemaValue, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                rating = candidate;
                return true;
            }
        }

        // Also accept the enum names (e.g. "MA15Plus") which older StripWolf versions wrote
        var enumName = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("+", "Plus", StringComparison.Ordinal);
        return Enum.TryParse(enumName, ignoreCase: true, out rating) && Enum.IsDefined(rating);
    }
}
