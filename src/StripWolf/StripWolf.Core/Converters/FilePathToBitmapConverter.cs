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

using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using StripWolf.Core.Controls;

namespace StripWolf.Core.Converters;

/// <summary>
/// Converts a file path string to a Bitmap for display in Image controls.
/// Returns null if the file doesn't exist or can't be loaded.
/// The decoded bitmaps are kept in a small LRU cache, keyed by path and the file's last write time and length,
/// because covers (e.g. Komga covers stored as {BookId}.jpg) can be rewritten in place under the same path.
/// Note: a value converter can only answer synchronously, so the decode runs on the UI thread. It is capped to
/// <see cref="MaxDecodeWidth"/>, for bigger or many images prefer <see cref="AsyncImage"/> which decodes in the background.
/// </summary>
public class FilePathToBitmapConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance for XAML usage
    /// </summary>
    public static readonly FilePathToBitmapConverter Instance = new();

    // Covers are displayed at ~150-200 DIP, 400 pixels is enough for HiDPI screens
    private const int MaxDecodeWidth = 400;
    private const int MaxCacheEntries = 32;

    private sealed record CacheEntry(string FilePath, DateTime LastWriteTimeUtc, long Length, Bitmap Bitmap);

    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache = new(StringComparer.Ordinal);
    // Most recently used entry first
    private static readonly LinkedList<CacheEntry> _lru = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string filePath || string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        DateTime lastWriteTimeUtc;
        long length;
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                System.Diagnostics.Debug.WriteLine($"FilePathToBitmapConverter: File not found '{filePath}'");
                return null;
            }
            lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            length = fileInfo.Length;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FilePathToBitmapConverter: Failed to access '{filePath}': {ex.Message}");
            return null;
        }

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(filePath, out var node))
            {
                var entry = node.Value;
                if (entry.LastWriteTimeUtc == lastWriteTimeUtc && entry.Length == length)
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    return entry.Bitmap;
                }

                // The file changed on disk: drop the stale entry (not disposed, see AddToCache)
                _lru.Remove(node);
                _cache.Remove(filePath);
            }
        }

        try
        {
            var bitmap = ThumbnailDecoder.Decode(File.ReadAllBytes(filePath), MaxDecodeWidth);
            AddToCache(new CacheEntry(filePath, lastWriteTimeUtc, length, bitmap));
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FilePathToBitmapConverter: Failed to load '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static void AddToCache(CacheEntry entry)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(entry.FilePath, out var existing))
            {
                _lru.Remove(existing);
            }

            _cache[entry.FilePath] = _lru.AddFirst(entry);

            while (_lru.Count > MaxCacheEntries && _lru.Last is { } oldest)
            {
                // Evicted (and stale) bitmaps are intentionally NOT disposed: the converter can't know whether an
                // Image still shows it, and disposing a displayed bitmap throws during rendering.
                // Once nothing references it any more, the GC finalizes it and the native memory is released.
                _lru.RemoveLast();
                _cache.Remove(oldest.Value.FilePath);
            }
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Multi-value converter that returns the first non-null, non-empty string from its inputs.
    /// </summary>
    public static readonly IMultiValueConverter FirstNonNullStringConverter = 
        new FuncMultiValueConverter<object?, string?>(values => 
            values?.Select(v => v?.ToString()).FirstOrDefault(s => !string.IsNullOrEmpty(s)));

    /// <summary>
    /// Returns true if the numeric value is greater than zero.
    /// </summary>
    public static readonly IValueConverter GreaterThanZeroConverter =
        new FuncValueConverter<double, bool>(val => val > 0);

    /// <summary>
    /// Returns true if the numeric value is zero.
    /// </summary>
    public static readonly IValueConverter IsZeroConverter =
        new FuncValueConverter<double, bool>(val => Math.Abs(val) < 0.0001);

    /// <summary>
    /// Converts a boolean (IsDescending) to a sort direction icon (↑/↓)
    /// </summary>
    public static readonly IValueConverter SortDirectionIconConverter = 
        new FuncValueConverter<bool, string>(isDescending => isDescending ? "↓" : "↑");
}

