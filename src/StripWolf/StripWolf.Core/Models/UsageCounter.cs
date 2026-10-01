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

namespace StripWolf.Core.Models;

/// <summary>
/// Aggregated usage counter for the personal reading statistics ("PagesRead", "ComicOpen", "LocalImport", "KomgaDownload").
/// This replaces the former UsageStats table, which stored one row per event (one per page turn!) and grew forever,
/// while only the number of rows per metric was ever used. Now there is exactly one row per metric.
/// </summary>
[Table(TableName)]
public class UsageCounter
{
    public const string TableName = "UsageCounter";

    [PrimaryKey]
    public string Metric { get; set; } = string.Empty;

    public long Amount { get; set; }
}
