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

using Avalonia.Media.Imaging;

namespace StripWolf.Core.Controls;

/// <summary>
/// Decodes cover images for display at a bounded size.
/// A full size decode of a cover costs width * height * 4 bytes, for a typical 1200x1800 cover that is more than 8MB,
/// while it is displayed at ~150-200 DIP. Decoding to a capped width keeps that at a fraction.
/// Safe to call from a background thread.
/// </summary>
public static class ThumbnailDecoder
{
    /// <summary>
    /// Decodes the image, scaling it down to <paramref name="maxWidth"/> pixels when it is wider.
    /// Smaller images are decoded as-is, DecodeToWidth would scale them up and waste memory.
    /// </summary>
    public static Bitmap Decode(byte[] imageBytes, int maxWidth)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        var width = TryGetWidth(imageBytes);
        if (width is null || width.Value > maxWidth)
        {
            // Width unknown (format not supported by ImageSharp's identify): better to risk an upscale than a huge decode
            return Bitmap.DecodeToWidth(stream, maxWidth, BitmapInterpolationMode.HighQuality);
        }

        return new Bitmap(stream);
    }

    private static int? TryGetWidth(byte[] imageBytes)
    {
        try
        {
            // Only reads the header, no pixel decoding
            return SixLabors.ImageSharp.Image.Identify(imageBytes.AsSpan()).Width;
        }
        catch
        {
            return null;
        }
    }
}
