namespace SteelConveyorWar.Core;

/// <summary>
/// FoW-aware (or cheat) read model over a live <see cref="GameSimulation"/>.
/// Entity gating matches SFML: owned entities always observe; others require <see cref="VisibilityState.Visible"/>.
/// </summary>
public sealed class PlayerView : IPlayerView
{
    private readonly GameSimulation _simulation;

    public PlayerView(GameSimulation simulation, PlayerId observerId, PlayerObservationMode mode)
    {
        _simulation = simulation;
        // Validate observer exists up front.
        _ = simulation.GetPlayer(observerId);
        ObserverId = observerId;
        Mode = mode;
    }

    public PlayerId ObserverId { get; }

    public PlayerObservationMode Mode { get; }

    public WorldSize WorldSize => _simulation.World.Size;

    public VisibilityState GetVisibility(TilePosition position)
    {
        if (!_simulation.World.IsInside(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Tile position is outside the world.");
        }

        return _simulation.GetVisibility(ObserverId, position);
    }

    public bool TryGetTerrain(TilePosition position, out TerrainType terrain)
    {
        if (!_simulation.World.IsInside(position))
        {
            terrain = default;
            return false;
        }

        if (Mode == PlayerObservationMode.Fair
            && _simulation.GetVisibility(ObserverId, position) == VisibilityState.Unknown)
        {
            terrain = default;
            return false;
        }

        terrain = _simulation.World.GetTerrain(position);
        return true;
    }

    public IEnumerable<WorldEntity> GetVisibleEntities()
    {
        if (Mode == PlayerObservationMode.Cheat)
        {
            return _simulation.World.Entities;
        }

        return _simulation.World.Entities.Where(IsFairEntityVisible);
    }

    public WorldEntity? GetVisibleEntity(int entityId)
    {
        var entity = _simulation.World.GetEntity(entityId);
        if (entity is null)
        {
            return null;
        }

        return IsEntityVisible(entity) ? entity : null;
    }

    public bool IsEntityVisible(WorldEntity entity)
    {
        if (Mode == PlayerObservationMode.Cheat)
        {
            return true;
        }

        return IsFairEntityVisible(entity);
    }

    private bool IsFairEntityVisible(WorldEntity entity)
    {
        // Match SFML playfield/minimap: garrisoned units are off-map; own units always show;
        // Explored must not leak current enemy/ally positions.
        if (!entity.IsAlive || entity.IsGarrisoned)
        {
            return false;
        }

        if (entity.OwnerId == ObserverId)
        {
            return true;
        }

        return _simulation.GetVisibility(ObserverId, entity.Position) == VisibilityState.Visible;
    }
}
