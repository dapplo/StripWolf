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

namespace StripWolf.Core.Services;

/// <summary>
/// Represents a reusable PDF render session for a single open document.
/// </summary>
public interface IPdfRenderSession : IDisposable
{
    /// <summary>
    /// Gets the number of pages in the open PDF document.
    /// </summary>
    int GetPageCount();

    /// <summary>
    /// Gets the PDF metadata from the open document, if available.
    /// </summary>
    PdfMetadata? GetMetadata();

    /// <summary>
    /// Renders the specified zero-based page index directly into the supplied JPEG output stream.
    /// </summary>
    Task RenderPageToJpegAsync(int pageIndex, Stream outputStream);
}
