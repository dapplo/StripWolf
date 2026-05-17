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

namespace StripWolf.Services;

/// <summary>
/// Represents metadata extracted from a PDF file
/// </summary>
public class PdfMetadata
{
    /// <summary>
    /// The document's title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The name of the person who created the document
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// The subject of the document
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Keywords associated with the document
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// The name of the application that created the original document
    /// </summary>
    public string? Creator { get; set; }

    /// <summary>
    /// The name of the application that produced the PDF
    /// </summary>
    public string? Producer { get; set; }

    /// <summary>
    /// The date and time the document was created
    /// </summary>
    public DateTime? CreationDate { get; set; }

    /// <summary>
    /// The date and time the document was last modified
    /// </summary>
    public DateTime? ModificationDate { get; set; }

    /// <summary>
    /// Checks if any metadata fields are set
    /// </summary>
    public bool HasAnyMetadata =>
        !string.IsNullOrEmpty(Title) ||
        !string.IsNullOrEmpty(Author) ||
        !string.IsNullOrEmpty(Subject) ||
        !string.IsNullOrEmpty(Keywords) ||
        !string.IsNullOrEmpty(Creator) ||
        CreationDate.HasValue;
}
