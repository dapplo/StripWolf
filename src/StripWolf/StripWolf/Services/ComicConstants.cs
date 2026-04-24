namespace StripWolf.Services;

/// <summary>
/// Constants for comic book file handling
/// </summary>
public static class ComicConstants
{
    /// <summary>
    /// Supported image file extensions for comic pages
    /// </summary>
    public static readonly string[] ImageExtensions = 
    [
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif", ".avif"
    ];

    /// <summary>
    /// Supported comic book file extensions
    /// </summary>
    public static readonly string[] ComicExtensions = 
    [
        ".cbz", ".cbr", ".cb7", ".cbt", ".pdf"
    ];

    /// <summary>
    /// Checks if a filename is an image file
    /// </summary>
    public static bool IsImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }

    /// <summary>
    /// Checks if a filename is a ComicInfo.xml file
    /// </summary>
    public static bool IsComicInfoFile(string fileName)
    {
        return Path.GetFileName(fileName).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase);
    }
}
