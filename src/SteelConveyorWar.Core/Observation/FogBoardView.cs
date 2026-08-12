using System.Collections.Immutable;

namespace SteelConveyorWar.Core;

/// <summary>
/// M10: tick-frozen fog + known-terrain board. Visibility is a dense immutable grid; terrain is
/// retained only for Explored/Visible tiles (fair) or every in-bounds tile (cheat). Safe to retain
/// across <see cref="GameSimulation.AdvanceTick"/> — values never track live FoW.
/// </summary>
public sealed class FogBoardView
{
    private readonly VisibilityState[] _visibility;
    private readonly ImmutableDictionary<long, TerrainType> _knownTerrain;

    internal FogBoardView(
        long observationTick,
        WorldSize worldSize,
        VisibilityState[] visibility,
        ImmutableDictionary<long, TerrainType> knownTerrain)
    {
        ObservationTick = observationTick;
        WorldSize = worldSize;
        _visibility = visibility;
        _knownTerrain = knownTerrain;
    }

    public long ObservationTick { get; }

    public WorldSize WorldSize { get; }

    public VisibilityState GetVisibility(TilePosition position)
    {
        if (!IsInside(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Tile position is outside the world.");
        }

        return _visibility[IndexOf(position)];
    }

    public bool TryGetTerrain(TilePosition position, out TerrainType terrain)
    {
        if (!IsInside(position))
        {
            terrain = default;
            return false;
        }

        return _knownTerrain.TryGetValue(Pack(position), out terrain);
    }

    private bool IsInside(TilePosition position)
        => position.X >= 0
            && position.Y >= 0
            && position.X < WorldSize.Width
            && position.Y < WorldSize.Height;

    private int IndexOf(TilePosition position)
        => (position.Y * WorldSize.Width) + position.X;

    internal static long Pack(TilePosition position)
        => ((long)position.X << 32) | (uint)position.Y;
}
