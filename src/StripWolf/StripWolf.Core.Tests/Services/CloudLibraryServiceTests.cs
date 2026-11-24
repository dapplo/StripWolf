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

using StripWolf.Core.Services;
using Xunit;

namespace StripWolf.Core.Tests;

public sealed class CloudLibraryServiceTests
{
    [Fact]
    public async Task HaveSameContentAsync_ComparesTheBytes()
    {
        using var temp = new TempDirectory("cloud");
        var a = temp.Combine("a.cbz");
        var b = temp.Combine("b.cbz");
        var c = temp.Combine("c.cbz");
        var d = temp.Combine("d.cbz");
        var content = new byte[200_000];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(a, content);
        await File.WriteAllBytesAsync(b, content);
        var changed = (byte[])content.Clone();
        changed[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(c, changed);
        await File.WriteAllBytesAsync(d, content[..1000]);

        Assert.True(await CloudLibraryService.HaveSameContentAsync(a, b));
        Assert.False(await CloudLibraryService.HaveSameContentAsync(a, c));
        Assert.False(await CloudLibraryService.HaveSameContentAsync(a, d));
    }
}
