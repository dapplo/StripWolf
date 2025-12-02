namespace Kom2go.Models;

/// <summary>
/// Reading modes available in the comic reader
/// </summary>
public enum ReadingMode
{
    /// <summary>
    /// Standard reading mode - full page view
    /// </summary>
    Normal,
    
    /// <summary>
    /// Zoomed reading mode - page overview on one side, zoomed area on the other
    /// </summary>
    Zoomed,
    
    /// <summary>
    /// Guided reading mode - automatically detects comic panels and navigates through them
    /// </summary>
    Guided
}

/// <summary>
/// Represents a detected panel (scene) on a comic page
/// </summary>
public class ComicPanel
{
    /// <summary>
    /// The page index this panel belongs to
    /// </summary>
    public int PageIndex { get; set; }
    
    /// <summary>
    /// Zero-based index of this panel on the page (in reading order)
    /// </summary>
    public int PanelIndex { get; set; }
    
    /// <summary>
    /// X coordinate of the panel's top-left corner (normalized 0-1)
    /// </summary>
    public double X { get; set; }
    
    /// <summary>
    /// Y coordinate of the panel's top-left corner (normalized 0-1)
    /// </summary>
    public double Y { get; set; }
    
    /// <summary>
    /// Width of the panel (normalized 0-1)
    /// </summary>
    public double Width { get; set; }
    
    /// <summary>
    /// Height of the panel (normalized 0-1)
    /// </summary>
    public double Height { get; set; }
    
    /// <summary>
    /// Confidence score of the detection (0-1), where 1 is highest confidence
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Contains cached panel detection results for a comic page
/// </summary>
public class PagePanelInfo
{
    /// <summary>
    /// The page index
    /// </summary>
    public int PageIndex { get; set; }
    
    /// <summary>
    /// List of detected panels in reading order (top-left to bottom-right)
    /// </summary>
    public List<ComicPanel> Panels { get; set; } = [];
    
    /// <summary>
    /// Whether the detection was successful
    /// </summary>
    public bool DetectionSuccessful { get; set; }
    
    /// <summary>
    /// If detection failed or page is a splash page, this will be true
    /// </summary>
    public bool IsSplashPage { get; set; }
    
    /// <summary>
    /// Time when this was detected (for cache management)
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Handedness preference for reading mode layout
/// </summary>
public enum Handedness
{
    /// <summary>
    /// Right-handed - overview on left, zoomed area on right
    /// </summary>
    RightHanded,
    
    /// <summary>
    /// Left-handed - overview on right, zoomed area on left
    /// </summary>
    LeftHanded
}

/// <summary>
/// Zoom region for the zoomed reading mode
/// </summary>
public class ZoomRegion
{
    /// <summary>
    /// X coordinate of the zoom region's center (normalized 0-1)
    /// </summary>
    public double CenterX { get; set; } = 0.5;
    
    /// <summary>
    /// Y coordinate of the zoom region's center (normalized 0-1)
    /// </summary>
    public double CenterY { get; set; } = 0.5;
    
    /// <summary>
    /// Size of the zoom region (normalized 0-1, represents width and height)
    /// </summary>
    public double Size { get; set; } = 0.3;
    
    /// <summary>
    /// Minimum size for the zoom region
    /// </summary>
    public const double MinSize = 0.1;
    
    /// <summary>
    /// Maximum size for the zoom region
    /// </summary>
    public const double MaxSize = 0.8;
    
    /// <summary>
    /// Calculate the bounds of the zoom region
    /// </summary>
    public (double Left, double Top, double Right, double Bottom) GetBounds()
    {
        var halfSize = Size / 2;
        return (
            Math.Max(0, CenterX - halfSize),
            Math.Max(0, CenterY - halfSize),
            Math.Min(1, CenterX + halfSize),
            Math.Min(1, CenterY + halfSize)
        );
    }
    
    /// <summary>
    /// Move the zoom region by delta amounts (normalized)
    /// </summary>
    public void Move(double deltaX, double deltaY)
    {
        var halfSize = Size / 2;
        CenterX = Math.Max(halfSize, Math.Min(1 - halfSize, CenterX + deltaX));
        CenterY = Math.Max(halfSize, Math.Min(1 - halfSize, CenterY + deltaY));
    }
    
    /// <summary>
    /// Resize the zoom region
    /// </summary>
    public void Resize(double sizeDelta)
    {
        Size = Math.Max(MinSize, Math.Min(MaxSize, Size + sizeDelta));
        // Ensure center stays in valid bounds after resize
        var halfSize = Size / 2;
        CenterX = Math.Max(halfSize, Math.Min(1 - halfSize, CenterX));
        CenterY = Math.Max(halfSize, Math.Min(1 - halfSize, CenterY));
    }
}
