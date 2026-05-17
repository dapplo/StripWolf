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
using StripWolf.Models;

namespace StripWolf.Services;

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
            while (subReader.Read())
            {
                if (subReader.NodeType != XmlNodeType.Element) continue;

                var name = subReader.Name;
                switch (name)
                {
                    case "Title": info.Title = subReader.ReadElementContentAsString(); break;
                    case "Series": info.Series = subReader.ReadElementContentAsString(); break;
                    case "Number": info.Number = subReader.ReadElementContentAsString(); break;
                    case "Count": if (int.TryParse(subReader.ReadElementContentAsString(), out var count)) info.Count = count; break;
                    case "Volume": if (int.TryParse(subReader.ReadElementContentAsString(), out var vol)) info.Volume = vol; break;
                    case "AlternateSeries": info.AlternateSeries = subReader.ReadElementContentAsString(); break;
                    case "AlternateNumber": info.AlternateNumber = subReader.ReadElementContentAsString(); break;
                    case "AlternateCount": if (int.TryParse(subReader.ReadElementContentAsString(), out var acount)) info.AlternateCount = acount; break;
                    case "Summary": info.Summary = subReader.ReadElementContentAsString(); break;
                    case "Notes": info.Notes = subReader.ReadElementContentAsString(); break;
                    case "Year": if (int.TryParse(subReader.ReadElementContentAsString(), out var year)) info.Year = year; break;
                    case "Month": if (int.TryParse(subReader.ReadElementContentAsString(), out var month)) info.Month = month; break;
                    case "Day": if (int.TryParse(subReader.ReadElementContentAsString(), out var day)) info.Day = day; break;
                    case "Writer": info.Writer = subReader.ReadElementContentAsString(); break;
                    case "Penciller": info.Penciller = subReader.ReadElementContentAsString(); break;
                    case "Inker": info.Inker = subReader.ReadElementContentAsString(); break;
                    case "Colorist": info.Colorist = subReader.ReadElementContentAsString(); break;
                    case "Letterer": info.Letterer = subReader.ReadElementContentAsString(); break;
                    case "CoverArtist": info.CoverArtist = subReader.ReadElementContentAsString(); break;
                    case "Editor": info.Editor = subReader.ReadElementContentAsString(); break;
                    case "Publisher": info.Publisher = subReader.ReadElementContentAsString(); break;
                    case "Imprint": info.Imprint = subReader.ReadElementContentAsString(); break;
                    case "Genre": info.Genre = subReader.ReadElementContentAsString(); break;
                    case "Tags": info.Tags = subReader.ReadElementContentAsString(); break;
                    case "Web": info.Web = subReader.ReadElementContentAsString(); break;
                    case "PageCount": if (int.TryParse(subReader.ReadElementContentAsString(), out var pc)) info.PageCount = pc; break;
                    case "LanguageISO": info.LanguageISO = subReader.ReadElementContentAsString(); break;
                    case "Format": info.Format = subReader.ReadElementContentAsString(); break;
                    case "BlackAndWhite": if (Enum.TryParse<YesNo>(subReader.ReadElementContentAsString(), out var bw)) info.BlackAndWhite = bw; break;
                    case "Manga": if (Enum.TryParse<YesNo>(subReader.ReadElementContentAsString(), out var m)) info.Manga = m; break;
                    case "Characters": info.Characters = subReader.ReadElementContentAsString(); break;
                    case "Teams": info.Teams = subReader.ReadElementContentAsString(); break;
                    case "Locations": info.Locations = subReader.ReadElementContentAsString(); break;
                    case "StoryArc": info.StoryArc = subReader.ReadElementContentAsString(); break;
                    case "StoryArcNumber": info.StoryArcNumber = subReader.ReadElementContentAsString(); break;
                    case "SeriesGroup": info.SeriesGroup = subReader.ReadElementContentAsString(); break;
                    case "AgeRating": if (Enum.TryParse<AgeRating>(subReader.ReadElementContentAsString().Replace(" ", ""), out var ar)) info.AgeRating = ar; break;
                    case "CommunityRating": if (decimal.TryParse(subReader.ReadElementContentAsString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cr)) info.CommunityRating = cr; break;
                    case "ScanInformation": info.ScanInformation = subReader.ReadElementContentAsString(); break;
                    case "Pages": info.Pages = ReadPages(subReader); break;
                }
            }

            return info;
        }
        catch
        {
            return null;
        }
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

    private static string GetAgeRatingString(AgeRating rating) => rating switch
    {
        AgeRating.AdultsOnly18Plus => "Adults Only 18+",
        AgeRating.EarlyChildhood => "Early Childhood",
        AgeRating.Everyone10Plus => "Everyone 10+",
        AgeRating.KidsToAdults => "Kids to Adults",
        AgeRating.Mature17Plus => "Mature 17+",
        AgeRating.RatingPending => "Rating Pending",
        _ => rating.ToString()
    };
}

