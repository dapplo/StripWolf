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

using Microsoft.IO;

namespace StripWolf.Core.Services;

public static class RecyclableStreamManagerProvider
{
    // With the default options the pools never release anything: the largest pages ever read (plus every buffer
    // size in between) stay allocated for the lifetime of the process. Cap what is kept in the free pools.
    public static RecyclableMemoryStreamManager Manager { get; } = new(new RecyclableMemoryStreamManager.Options
    {
        MaximumSmallPoolFreeBytes = 16 * 1024 * 1024,
        MaximumLargePoolFreeBytes = 64 * 1024 * 1024
    });
}

