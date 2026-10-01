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

namespace StripWolf.Core.Tests;

/// <summary>
/// A unique temporary directory per test, deleted again on dispose. Tests never touch the real user data.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string prefix = "test")
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "StripWolf.Core.Tests", $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string Combine(params string[] parts)
    {
        return Path.Combine([DirectoryPath, .. parts]);
    }

    /// <summary>
    /// Creates (and returns) a sub directory
    /// </summary>
    public string CreateDirectory(params string[] parts)
    {
        var path = Combine(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Copies a file from TestData into this directory, so a test can modify, convert or delete it.
    /// </summary>
    /// <param name="fixtureName">File name in TestData</param>
    /// <param name="relativeTargetPath">Target path relative to this directory, defaults to the fixture name</param>
    public string CopyFixture(string fixtureName, string? relativeTargetPath = null)
    {
        var target = Combine(relativeTargetPath ?? fixtureName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(TestFiles.Get(fixtureName), target, overwrite: true);
        return target;
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                // e.g. a SQLite file still locked on Windows, retry shortly
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
