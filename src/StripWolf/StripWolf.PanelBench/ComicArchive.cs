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

using System.IO.Compression;
using SharpCompress.Archives.Rar;
using StripWolf.Core.Services;

namespace StripWolf.PanelBench;

/// <summary>
/// A page of a comic: the entry name in the archive (used as key in the label files) and the image data.
/// </summary>
public sealed record ComicPage(string Name, byte[] Data);

/// <summary>
/// Reads all pages of a CBZ / CBR (or a folder of images) in the same order as the reader does.
/// </summary>
public static class ComicArchive
{
    public static readonly string[] SupportedExtensions = [".cbz", ".zip", ".cbr", ".rar"];

    public static List<ComicPage> ReadPages(string path)
    {
        var pages = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cbz" or ".zip" => ReadZip(path),
            ".cbr" or ".rar" => ReadRar(path),
            _ => throw new NotSupportedException($"Unsupported comic format: {path}")
        };

        return pages
            .OrderBy(page => page.Name, ComicPageComparer.Instance)
            .ToList();
    }

    private static IEnumerable<ComicPage> ReadZip(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            if (!IsPage(entry.FullName))
            {
                continue;
            }

            using var entryStream = entry.Open();
            yield return new ComicPage(entry.FullName, ReadAll(entryStream));
        }
    }

    private static IEnumerable<ComicPage> ReadRar(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = RarArchive.OpenArchive(stream);
        if (!archive.IsSolid)
        {
            // ExtractAllEntries only works on solid archives, non solid archives are read entry by entry
            foreach (var entry in archive.Entries)
            {
                var entryKey = entry.Key ?? string.Empty;
                if (entry.IsDirectory || !IsPage(entryKey))
                {
                    continue;
                }

                using var entryStream = entry.OpenEntryStream();
                yield return new ComicPage(entryKey, ReadAll(entryStream));
            }

            yield break;
        }

        // Solid archives can only be read sequentially
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            var key = reader.Entry.Key ?? string.Empty;
            if (reader.Entry.IsDirectory || !IsPage(key))
            {
                continue;
            }

            using var entryStream = reader.OpenEntryStream();
            yield return new ComicPage(key, ReadAll(entryStream));
        }
    }

    private static bool IsPage(string name) =>
        !ComicConstants.IsIgnoredImportPath(name) && ComicConstants.IsImageFile(name);

    private static byte[] ReadAll(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}
