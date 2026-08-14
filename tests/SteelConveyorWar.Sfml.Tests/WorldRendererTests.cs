using SFML.System;
using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

public sealed class WorldRendererTests
{
    [Theory]
    [InlineData(Direction.North)]
    [InlineData(Direction.East)]
    [InlineData(Direction.South)]
    [InlineData(Direction.West)]
    public void DirectionTriangle_HasObtuseTipOf120Degrees(Direction direction)
    {
        var points = WorldRenderer.DirectionTriangleOffsets(direction, SfmlUiLayout.TileSize);
        Assert.Equal(3, points.Length);

        var tip = points[0];
        var left = points[2];
        var right = points[1];
        Assert.InRange(AngleDegrees(right - tip, left - tip), 119.0, 121.0);
        Assert.InRange(AngleDegrees(tip - left, right - left), 29.0, 31.0);
        Assert.InRange(AngleDegrees(tip - right, left - right), 29.0, 31.0);
    }

    [Fact]
    public void DemolishHold_CommitsAfterHalfSecond()
    {
        Assert.Equal(0.5f, SfmlUiLayout.DemolishHoldSeconds);
    }

    private static double AngleDegrees(Vector2f a, Vector2f b)
    {
        var dot = a.X * b.X + a.Y * b.Y;
        var mag = Math.Sqrt((a.X * a.X + a.Y * a.Y) * (b.X * b.X + b.Y * b.Y));
        return Math.Acos(Math.Clamp(dot / mag, -1.0, 1.0)) * 180.0 / Math.PI;
    }
}
