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

public sealed class KomgaThumbnailCacheTests : IDisposable
{
    private readonly TempDirectory _temp = new("thumbnails");

    public void Dispose() => _temp.Dispose();

    private string CreateFile(string name, int size, DateTime lastUsedUtc)
    {
        var path = Path.Combine(_temp.CreateDirectory("1", "books"), name);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, lastUsedUtc);
        File.SetLastAccessTimeUtc(path, lastUsedUtc);
        return path;
    }

    [Fact]
    public void Prune_BelowTheLimit_KeepsEverything()
    {
        var file = CreateFile("a.jpg", 100, DateTime.UtcNow);

        KomgaThumbnailCache.Prune(_temp.DirectoryPath, maxBytes: 1000, targetBytes: 500);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Prune_AboveTheLimit_RemovesTheLeastRecentlyUsedFirst()
    {
        var oldest = CreateFile("oldest.jpg", 400, DateTime.UtcNow.AddDays(-3));
        var older = CreateFile("older.jpg", 400, DateTime.UtcNow.AddDays(-2));
        var newest = CreateFile("newest.jpg", 400, DateTime.UtcNow.AddDays(-1));

        KomgaThumbnailCache.Prune(_temp.DirectoryPath, maxBytes: 1000, targetBytes: 500);

        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(older));
        Assert.True(File.Exists(newest));
    }
}
