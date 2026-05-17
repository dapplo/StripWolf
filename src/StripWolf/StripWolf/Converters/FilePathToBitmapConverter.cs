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

using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace StripWolf.Converters;

/// <summary>
/// Converts a file path string to a Bitmap for display in Image controls.
/// Returns null if the file doesn't exist or can't be loaded.
/// </summary>
public class FilePathToBitmapConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance for XAML usage
    /// </summary>
    public static readonly FilePathToBitmapConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string filePath && !string.IsNullOrEmpty(filePath))
        {
            if (_cache.TryGetValue(filePath, out var cached))
                return cached;

            if (File.Exists(filePath))
            {
                try
                {
                    var bitmap = new Bitmap(filePath);
                    _cache.TryAdd(filePath, bitmap);
                    return bitmap;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"FilePathToBitmapConverter: Failed to load '{filePath}': {ex.Message}");
                    return null;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"FilePathToBitmapConverter: File not found '{filePath}'");
            }
        }
        return null;
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
    /// Converts a boolean (IsDescending) to a sort direction icon (â†‘/â†“)
    /// </summary>
    public static readonly IValueConverter SortDirectionIconConverter = 
        new FuncValueConverter<bool, string>(isDescending => isDescending ? "â†“" : "â†‘");
}

