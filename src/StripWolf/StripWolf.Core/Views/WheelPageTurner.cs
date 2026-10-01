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

namespace StripWolf.Core.Views;

/// <summary>
/// Turns mouse wheel / touchpad scroll events into page turns.
/// </summary>
/// <remarks>
/// A mouse wheel reports whole notches (|delta| = 1 per notch): every notch turns a page, as before.
/// A touchpad (precise scrolling) reports a stream of small fractional deltas for a single gesture; turning a page for
/// every event flipped several pages per swipe. Fractional deltas are accumulated until a threshold is reached, and
/// after a page turn the rest of the gesture is ignored for a short cool-down.
/// </remarks>
internal sealed class WheelPageTurner
{
    private const double TouchpadThreshold = 1.5;
    private static readonly TimeSpan GestureGap = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CoolDown = TimeSpan.FromMilliseconds(400);

    private double _accumulated;
    private DateTime _lastEvent = DateTime.MinValue;
    private DateTime _coolDownUntil = DateTime.MinValue;

    /// <summary>
    /// Feed a wheel delta (Y).
    /// </summary>
    /// <returns>+1 to go back (wheel up), -1 to go forward (wheel down), 0 for no page turn</returns>
    public int Process(double deltaY)
    {
        if (deltaY == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var isWholeNotch = Math.Abs(deltaY - Math.Round(deltaY)) < 0.001 && Math.Abs(deltaY) >= 1;
        if (isWholeNotch)
        {
            // Classic mouse wheel
            _accumulated = 0;
            _lastEvent = now;
            return Math.Sign(deltaY);
        }

        if (now - _lastEvent > GestureGap || Math.Sign(deltaY) != Math.Sign(_accumulated))
        {
            // A new gesture (or a change of direction) starts accumulating from zero
            _accumulated = 0;
        }
        _lastEvent = now;

        if (now < _coolDownUntil)
        {
            // Swallow the tail of the gesture which already turned a page; it keeps extending the cool-down
            _coolDownUntil = now + CoolDown;
            return 0;
        }

        _accumulated += deltaY;
        if (Math.Abs(_accumulated) < TouchpadThreshold)
        {
            return 0;
        }

        var direction = Math.Sign(_accumulated);
        _accumulated = 0;
        _coolDownUntil = now + CoolDown;
        return direction;
    }
}
