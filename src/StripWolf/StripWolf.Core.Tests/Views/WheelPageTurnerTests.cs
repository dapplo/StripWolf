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

using StripWolf.Core.Views;
using Xunit;

namespace StripWolf.Core.Tests;

/// <summary>
/// WheelPageTurner uses DateTime.UtcNow: the tests feed the events back to back (well within the 200 ms gesture gap),
/// only the cool-down test waits.
/// </summary>
public sealed class WheelPageTurnerTests
{
    [Theory]
    [InlineData(1d, 1)]
    [InlineData(-1d, -1)]
    [InlineData(2d, 1)]
    [InlineData(-3d, -1)]
    [InlineData(0d, 0)]
    public void WholeNotch_TurnsAPage(double delta, int expected)
    {
        Assert.Equal(expected, new WheelPageTurner().Process(delta));
    }

    [Fact]
    public void WholeNotches_TurnEveryTime()
    {
        var turner = new WheelPageTurner();

        // A mouse wheel has no cool-down: every notch is a page
        Assert.Equal(new[] { -1, -1, -1, 1, 1 }, new[] { -1d, -1d, -1d, 1d, 1d }.Select(turner.Process));
    }

    [Fact]
    public void FractionalDeltas_AccumulateToTheThreshold()
    {
        var turner = new WheelPageTurner();

        Assert.Equal(0, turner.Process(-0.5));
        Assert.Equal(0, turner.Process(-0.5));
        // -1.5 reached: one page forward
        Assert.Equal(-1, turner.Process(-0.5));
    }

    [Fact]
    public void AfterATurn_TheRestOfTheGestureIsSwallowed()
    {
        var turner = new WheelPageTurner();
        Assert.Equal(1, turner.Process(0.8) + turner.Process(0.8));

        // The tail of the same swipe (a touchpad sends many more events) doesn't turn more pages
        for (var index = 0; index < 20; index++)
        {
            Assert.Equal(0, turner.Process(0.8));
        }
    }

    [Fact]
    public void DirectionChange_ResetsTheAccumulation()
    {
        var turner = new WheelPageTurner();

        Assert.Equal(0, turner.Process(0.6));
        Assert.Equal(0, turner.Process(0.6));
        // Without the reset this would only be 0.6 in total, now it starts at -0.6
        Assert.Equal(0, turner.Process(-0.6));
        Assert.Equal(0, turner.Process(-0.6));
        Assert.Equal(-1, turner.Process(-0.6));
    }

    [Fact]
    public void WholeNotch_DuringTheTouchpadCoolDown_StillTurns()
    {
        var turner = new WheelPageTurner();
        turner.Process(1.0 - 0.25);
        Assert.Equal(1, turner.Process(0.75));

        Assert.Equal(-1, turner.Process(-1));
    }

    [Fact]
    public void AfterTheCoolDown_ANewGestureTurnsAgain()
    {
        var turner = new WheelPageTurner();
        Assert.Equal(1, turner.Process(1.6));
        Assert.Equal(0, turner.Process(1.6));

        // Gesture gap (200 ms) and cool-down (400 ms) have passed
        Thread.Sleep(700);

        Assert.Equal(1, turner.Process(1.6));
    }
}
