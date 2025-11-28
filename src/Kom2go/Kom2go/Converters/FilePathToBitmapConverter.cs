using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Kom2go.Converters;

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
        if (value is string filePath && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                return new Bitmap(filePath);
            }
            catch
            {
                // Failed to load image - return null
                return null;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
