using CommunityToolkit.Mvvm.ComponentModel;

namespace Kom2go.Models;

/// <summary>
/// Reading modes for the comic reader
/// </summary>
public enum ReadingMode
{
    /// <summary>
    /// Normal single or double page view
    /// </summary>
    Normal,

    /// <summary>
    /// Split view with overview on one side and zoomed area on the other
    /// </summary>
    Zoomed,

    /// <summary>
    /// Guided view that follows detected panels
    /// </summary>
    Guided
}

/// <summary>
/// Information about a single comic panel on a page
/// </summary>
public class ComicPanel
{
    /// <summary>
    /// The page index this panel belongs to
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// The index of this panel on the page
    /// </summary>
    public int PanelIndex { get; set; }

    /// <summary>
    /// X coordinate of the panel (normalized 0-1)
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y coordinate of the panel (normalized 0-1)
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
    /// Confidence of the detection (0-1)
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
    /// Width of the zoom region (normalized 0-1)
    /// </summary>
    public double Width { get; set; } = 0.4;

    /// <summary>
    /// Height of the zoom region (normalized 0-1)
    /// </summary>
    public double Height { get; set; } = 0.4;

    /// <summary>
    /// Backward compatibility Size property (uses the larger of Width/Height)
    /// </summary>
    public double Size
    {
        get => Math.Max(Width, Height);
        set
        {
            Width = value;
            Height = value;
        }
    }

    /// <summary>
    /// Minimum size for the zoom region
    /// </summary>
    public const double MinSize = 0.05;

    /// <summary>
    /// Maximum size for the zoom region
    /// </summary>
    public const double MaxSize = 1.0;

    /// <summary>
    /// Calculate the bounds of the zoom region
    /// </summary>
    public (double Left, double Top, double Right, double Bottom) GetBounds()
    {
        return (
            Math.Max(0, CenterX - Width / 2),
            Math.Max(0, CenterY - Height / 2),
            Math.Min(1, CenterX + Width / 2),
            Math.Min(1, CenterY + Height / 2)
        );
    }

    /// <summary>
    /// Move the zoom region by delta amounts (normalized)
    /// </summary>
    public void Move(double deltaX, double deltaY)
    {
        CenterX = Math.Max(Width / 2, Math.Min(1 - Width / 2, CenterX + deltaX));
        CenterY = Math.Max(Height / 2, Math.Min(1 - Height / 2, CenterY + deltaY));
    }

    /// <summary>
    /// Resize the zoom region maintaining aspect ratio
    /// </summary>
    public void Resize(double delta)
    {
        if (Width <= 0 || Height <= 0) return;
        
        double aspectRatio = Width / Height;
        double newWidth = Math.Max(MinSize, Math.Min(MaxSize, Width + delta));
        double newHeight = newWidth / aspectRatio;

        if (newHeight > MaxSize)
        {
            newHeight = MaxSize;
            newWidth = newHeight * aspectRatio;
        }
        else if (newHeight < MinSize)
        {
            newHeight = MinSize;
            newWidth = newHeight * aspectRatio;
        }

        Width = newWidth;
        Height = newHeight;
        
        // Clamp position with new size
        Move(0, 0);
    }
}
