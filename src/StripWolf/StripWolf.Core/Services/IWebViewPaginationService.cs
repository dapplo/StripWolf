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
/// Extended WebView snapshot contract used by EPUB pagination to query paged layout information.
/// </summary>
public interface IWebViewPaginationService : IWebViewSnapshotService
{
    /// <summary>
    /// Creates an empty reusable pagination session for the supplied viewport.
    /// </summary>
    Task<IWebViewPaginationSession> CreatePaginationSessionAsync(int viewportWidth, int viewportHeight, double renderScale = 1);

    /// <summary>
    /// Renders the supplied HTML and returns the total number of paged columns.
    /// </summary>
    Task<int> GetPageCountAsync(string htmlContent, int viewportWidth, int viewportHeight);

    /// <summary>
    /// Loads the supplied HTML once and returns a session that can paginate and capture multiple pages.
    /// </summary>
    Task<IWebViewPaginationSession> CreatePaginationSessionAsync(string htmlContent, int viewportWidth, int viewportHeight, double renderScale = 1);
}
