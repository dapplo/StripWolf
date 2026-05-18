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

namespace StripWolf.Core.Models;

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

