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

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string filePath && !string.IsNullOrEmpty(filePath))
        {
            if (File.Exists(filePath))
            {
                try
                {
                    return new Bitmap(filePath);
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
    /// Converts a boolean (IsDescending) to a sort direction icon (↑/↓)
    /// </summary>
    public static readonly IValueConverter SortDirectionIconConverter = 
        new FuncValueConverter<bool, string>(isDescending => isDescending ? "↓" : "↑");
}
