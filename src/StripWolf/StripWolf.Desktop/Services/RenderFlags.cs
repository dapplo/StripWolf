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

namespace StripWolf.Services;

/// <summary>
/// PDFium render flags
/// </summary>
[Flags]
internal enum RenderFlags
{
    RenderAnnotations = 0x01,
    LcdText = 0x02,
    NoNativeText = 0x04,
    Grayscale = 0x08,
    LimitedImageCache = 0x200,
    ForceHalftone = 0x400,
    Printing = 0x800,
    NoSmoothText = 0x1000,
    NoSmoothImage = 0x2000,
    NoSmoothPath = 0x4000
}

