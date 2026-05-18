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
/// Captures a rendered snapshot of HTML content using a platform-native off-screen WebView.
/// </summary>
public interface IWebViewSnapshotService
{
    /// <summary>
    /// Renders the supplied HTML and captures the current viewport as a PNG stream.
    /// </summary>
    Task<Stream> CapturePageAsync(string htmlContent, int viewportWidth, int viewportHeight);
}

