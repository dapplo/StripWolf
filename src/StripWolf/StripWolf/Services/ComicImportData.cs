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

using StripWolf.Models;

namespace StripWolf.Services;

/// <summary>
/// Import-ready comic metadata captured during a single conversion or archive inspection pass.
/// </summary>
public sealed class ComicImportData : IDisposable
{
    public required string FilePath { get; init; }

    public required ComicFormat Format { get; init; }

    public ComicInfo? ComicInfo { get; init; }

    public int PageCount { get; init; }

    public long FileSize { get; init; }

    public Stream? CoverImageStream { get; init; }

    public void Dispose()
    {
        CoverImageStream?.Dispose();
    }
}

