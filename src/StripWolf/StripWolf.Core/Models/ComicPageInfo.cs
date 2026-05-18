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
/// Information about a single page in the comic
/// </summary>
public class ComicPageInfo
{
    /// <summary>
    /// Page index (0-based)
    /// </summary>
    [XmlAttribute("Image")]
    public int Image { get; set; }

    /// <summary>
    /// Page type
    /// </summary>
    [XmlIgnore]
    public ComicPageType? Type { get; set; }

    /// <summary>
    /// Page type as string for XML serialization
    /// </summary>
    [XmlAttribute("Type")]
    public string? TypeString
    {
        get => Type?.ToString();
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                Type = null;
            }
            else
            {
                Type = Enum.TryParse<ComicPageType>(value, out var result) ? result : null;
            }
        }
    }

    /// <summary>
    /// Whether the page should be shown in double page spread
    /// </summary>
    [XmlAttribute("DoublePage")]
    public bool DoublePage { get; set; }

    /// <summary>
    /// Page width
    /// </summary>
    [XmlAttribute("ImageWidth")]
    public int ImageWidth { get; set; }

    /// <summary>
    /// Page height
    /// </summary>
    [XmlAttribute("ImageHeight")]
    public int ImageHeight { get; set; }

    /// <summary>
    /// File size of the page image
    /// </summary>
    [XmlAttribute("ImageSize")]
    public long ImageSize { get; set; }

    /// <summary>
    /// Bookmark name for this page
    /// </summary>
    [XmlAttribute("Bookmark")]
    public string? Bookmark { get; set; }
}

