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

using StripWolf.Core.Models;
using Xunit;

namespace StripWolf.Core.Tests;

public sealed class ZoomRegionTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Defaults()
    {
        var region = new ZoomRegion();

        Assert.Equal(0.5, region.CenterX);
        Assert.Equal(0.5, region.CenterY);
        Assert.Equal(0.4, region.Width);
        Assert.Equal(0.4, region.Height);
        Assert.Equal(0.4, region.Size);
    }

    [Fact]
    public void Size_IsTheLargerSide_AndSetsBothSides()
    {
        var region = new ZoomRegion { Width = 0.2, Height = 0.6 };
        Assert.Equal(0.6, region.Size);

        region.Size = 0.3;

        Assert.Equal(0.3, region.Width);
        Assert.Equal(0.3, region.Height);
    }

    [Theory]
    [InlineData(0.6, 0.6)]
    [InlineData(1.0, 1.0)]
    [InlineData(5.0, ZoomRegion.MaxSize)]
    [InlineData(0.05, 0.05)]
    [InlineData(0.01, ZoomRegion.MinSize)]
    [InlineData(-1.0, ZoomRegion.MinSize)]
    public void SetSize_Square_IsClamped(double target, double expected)
    {
        var region = new ZoomRegion();

        region.SetSize(target);

        Assert.Equal(expected, region.Width, Tolerance);
        Assert.Equal(expected, region.Height, Tolerance);
    }

    [Fact]
    public void SetSize_KeepsTheAspectRatio()
    {
        var region = new ZoomRegion { Width = 0.2, Height = 0.4 };

        region.SetSize(0.8);

        Assert.Equal(0.8, region.Height, Tolerance);
        Assert.Equal(0.4, region.Width, Tolerance);
        Assert.Equal(0.8, region.Size, Tolerance);
    }

    [Fact]
    public void SetSize_TallRegion_SmallerSideStaysAboveTheMinimum()
    {
        var region = new ZoomRegion { Width = 0.1, Height = 0.5 };

        region.SetSize(0.02);

        // Limited by the width reaching MinSize, the ratio is kept
        Assert.Equal(ZoomRegion.MinSize, region.Width, Tolerance);
        Assert.Equal(0.25, region.Height, Tolerance);
    }

    [Fact]
    public void SetSize_TallRegion_LargerSideStaysBelowTheMaximum()
    {
        var region = new ZoomRegion { Width = 0.3, Height = 0.6 };

        region.SetSize(3);

        Assert.Equal(ZoomRegion.MaxSize, region.Height, Tolerance);
        Assert.Equal(0.5, region.Width, Tolerance);
    }

    [Fact]
    public void SetSize_ExtremeAspectRatio_PrefersTheMaximum()
    {
        var region = new ZoomRegion { Width = 0.01, Height = 0.9 };

        region.SetSize(0.5);

        // Shrinking is impossible without the width dropping below MinSize, growing is capped at MaxSize
        Assert.True(region.Height <= ZoomRegion.MaxSize + Tolerance);
        Assert.Equal(0.9 / 0.01, region.Height / region.Width, 1e-6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SetSize_InvalidTarget_IsIgnored(double target)
    {
        var region = new ZoomRegion { Width = 0.2, Height = 0.3 };

        region.SetSize(target);

        Assert.Equal(0.2, region.Width);
        Assert.Equal(0.3, region.Height);
    }

    [Fact]
    public void SetSize_DegenerateRegion_IsIgnored()
    {
        var region = new ZoomRegion { Width = 0, Height = 0.3 };

        region.SetSize(0.5);

        Assert.Equal(0, region.Width);
        Assert.Equal(0.3, region.Height);
    }

    /// <summary>
    /// Regression: Resize used to add the delta to the width only while the caller computed it from Size,
    /// pinch zoom on tall regions never reached the target.
    /// </summary>
    [Fact]
    public void Resize_TallRegion_ConvergesToTheTargetSize()
    {
        var region = new ZoomRegion { Width = 0.2, Height = 0.5 };
        const double target = 0.8;

        for (var step = 0; step < 3; step++)
        {
            region.Resize(target - region.Size);
        }

        Assert.Equal(target, region.Size, Tolerance);
        Assert.Equal(0.2 / 0.5, region.Width / region.Height, Tolerance);
    }

    [Fact]
    public void SetSize_KeepsTheRegionInsideThePage()
    {
        var region = new ZoomRegion { CenterX = 0.9, CenterY = 0.1, Width = 0.2, Height = 0.2 };

        region.SetSize(0.6);

        Assert.Equal(0.7, region.CenterX, Tolerance);
        Assert.Equal(0.3, region.CenterY, Tolerance);
        var (left, top, right, bottom) = region.GetBounds();
        Assert.Equal(0.4, left, Tolerance);
        Assert.Equal(0, top, Tolerance);
        Assert.Equal(1, right, Tolerance);
        Assert.Equal(0.6, bottom, Tolerance);
    }

    [Fact]
    public void Move_IsClampedToThePage()
    {
        var region = new ZoomRegion { Width = 0.4, Height = 0.2 };

        region.Move(10, -10);

        Assert.Equal(0.8, region.CenterX, Tolerance);
        Assert.Equal(0.1, region.CenterY, Tolerance);

        region.Move(-0.3, 0.2);

        Assert.Equal(0.5, region.CenterX, Tolerance);
        Assert.Equal(0.3, region.CenterY, Tolerance);
    }

    [Fact]
    public void GetBounds_ClampsToTheUnitSquare()
    {
        var region = new ZoomRegion { CenterX = 0.1, CenterY = 0.95, Width = 0.4, Height = 0.4 };

        var (left, top, right, bottom) = region.GetBounds();

        Assert.Equal(0, left, Tolerance);
        Assert.Equal(0.75, top, Tolerance);
        Assert.Equal(0.3, right, Tolerance);
        Assert.Equal(1, bottom, Tolerance);
    }
}
