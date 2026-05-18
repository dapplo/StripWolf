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

using SQLite;
using System.Diagnostics.CodeAnalysis;

namespace StripWolf.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields)]
public class EpubConversionState
{
    [PrimaryKey]
    public int ComicId { get; set; }

    [Indexed]
    public string SourceEpubPath { get; set; } = string.Empty;

    public string ShadowPath { get; set; } = string.Empty;

    public EpubConversionStatus Status { get; set; }

    public int ProducedPageCount { get; set; }

    public int? FinalPageCount { get; set; }

    public int NextChapterIndex { get; set; }

    public int NextPageIndexInChapter { get; set; }

    public string PaginationSignature { get; set; } = string.Empty;

    public string? LastError { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

