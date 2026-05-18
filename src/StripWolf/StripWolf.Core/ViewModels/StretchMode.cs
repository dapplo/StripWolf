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

namespace StripWolf.Core.ViewModels;

/// <summary>
/// Image stretch/fit modes for the reader
/// </summary>
public enum StretchMode
{
    FitPage,    // Fit entire page in view (maintain aspect ratio)
    FitWidth,   // Fit to width (may scroll vertically)
    FitHeight,  // Fit to height (may scroll horizontally)
    Original    // Original size (100%)
}
