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
/// Represents a reusable paginated HTML session that can load documents and capture multiple pages.
/// </summary>
public interface IWebViewPaginationSession : IAsyncDisposable
{
    /// <summary>
    /// Loads the supplied HTML into the existing native WebView session.
    /// </summary>
    Task LoadHtmlAsync(string htmlContent);

    /// <summary>
    /// Returns the number of paginated pages in the loaded document.
    /// </summary>
    Task<int> GetPageCountAsync();

    /// <summary>
    /// Captures the specified zero-based page index from the loaded document directly into the supplied stream.
    /// </summary>
    Task CapturePageToStreamAsync(int pageIndex, Stream outputStream);

    /// <summary>
    /// Captures the specified zero-based page index from the loaded document.
    /// </summary>
    Task<Stream> CapturePageAsync(int pageIndex);
}
