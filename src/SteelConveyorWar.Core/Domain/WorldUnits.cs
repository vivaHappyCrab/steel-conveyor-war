namespace SteelConveyorWar.Core;

/// <summary>
/// R20: authoritative continuous world units are integer millitiles (1 tile = 1000).
/// </summary>
public static class WorldUnits
{
    public const long MilliPerTile = 1000;
    public const long HalfTile = MilliPerTile / 2;

    /// <summary>Former <c>0.125</c> tile/tick mobile step.</summary>
    public const long MobileMoveMilliPerTick = 125;

    public static long TileToMilli(int tile) => tile * MilliPerTile;

    public static long TileCenterMilli(int tile) => tile * MilliPerTile + HalfTile;

    public static int MilliToTile(long milli)
    {
        // World coords are non-negative in MVP; floor-divide toward -∞ for safety.
        if (milli >= 0)
        {
            return (int)(milli / MilliPerTile);
        }

        return (int)((milli - (MilliPerTile - 1)) / MilliPerTile);
    }

    /// <summary>Integer square root (floor). Deterministic across runtimes.</summary>
    public static long IntegerSqrt(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value < 2)
        {
            return value;
        }

        var x0 = value / 2;
        var x1 = (x0 + value / x0) / 2;
        while (x1 < x0)
        {
            x0 = x1;
            x1 = (x0 + value / x0) / 2;
        }

        return x0;
    }
}
