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

namespace StripWolf.Core.Models;

/// <summary>
/// The result of the last read progress synchronization with Komga.
/// Stored as a number in the database, shown localized in the UI.
/// Never renumber existing values.
/// </summary>
public enum KomgaSyncState
{
    None = 0,
    Pending = 1,
    Synced = 2,
    UpdatedFromKomga = 3,
    InSync = 4,
    ServerNotFound = 5,
    NetworkError = 6,
    Failed = 7
}
