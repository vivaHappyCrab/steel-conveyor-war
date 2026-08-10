namespace SteelConveyorWar.Core;

/// <summary>
/// Per-player observation surface for bots and future net clients.
/// Use <see cref="PlayerObservationMode.Fair"/> to avoid reading live <see cref="GameWorld.Entities"/> as cheat vision.
/// </summary>
public interface IPlayerView
{
    PlayerId ObserverId { get; }

    PlayerObservationMode Mode { get; }

    WorldSize WorldSize { get; }

    VisibilityState GetVisibility(TilePosition position);

    /// <summary>
    /// Fair: returns false for <see cref="VisibilityState.Unknown"/> tiles.
    /// Cheat: returns terrain for any in-bounds tile.
    /// </summary>
    bool TryGetTerrain(TilePosition position, out TerrainType terrain);

    /// <summary>
    /// Fair: alive, non-garrisoned entities that pass the same visibility gate as SFML playfield/minimap.
    /// Cheat: the full live entity list.
    /// </summary>
    IEnumerable<WorldEntity> GetVisibleEntities();

    /// <summary>
    /// Fair: null when the entity is missing or not currently observable.
    /// Cheat: same as <see cref="GameWorld.GetEntity"/>.
    /// </summary>
    WorldEntity? GetVisibleEntity(int entityId);

    /// <summary>
    /// True when <see cref="GetVisibleEntities"/> would include this entity in the current mode.
    /// </summary>
    bool IsEntityVisible(WorldEntity entity);
}
