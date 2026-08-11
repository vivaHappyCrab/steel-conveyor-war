using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core;

/// <summary>
/// Per-player observation surface for bots and future net clients.
/// Use <see cref="PlayerObservationMode.Fair"/> to avoid reading live <see cref="GameWorld.Entities"/> as cheat vision.
/// R04: entity observation returns immutable, tick-stamped snapshots (never live <see cref="WorldEntity"/>),
/// so a retained reference cannot be used to keep reading state after an entity re-enters fog.
/// </summary>
public interface IPlayerView
{
    PlayerId ObserverId { get; }

    PlayerObservationMode Mode { get; }

    WorldSize WorldSize { get; }

    /// <summary>
    /// R19: The simulation tick this view currently reflects. Every snapshot returned by this view is
    /// stamped with the same value, so a bot can correlate observations across a single decision cycle.
    /// </summary>
    long ObservationTick { get; }

    VisibilityState GetVisibility(TilePosition position);

    /// <summary>
    /// Fair: returns false for <see cref="VisibilityState.Unknown"/> tiles.
    /// Cheat: returns terrain for any in-bounds tile.
    /// </summary>
    bool TryGetTerrain(TilePosition position, out TerrainType terrain);

    /// <summary>
    /// Fair: alive, non-garrisoned entities that pass the same visibility gate as SFML playfield/minimap.
    /// Cheat: the full live entity list. Returned as immutable snapshots frozen at the current tick.
    /// </summary>
    IReadOnlyList<VisibleEntitySnapshot> GetVisibleEntities();

    /// <summary>
    /// Fair: null when the entity is missing or not currently observable.
    /// Cheat: snapshot of any existing entity. Snapshot is frozen at the current tick.
    /// </summary>
    VisibleEntitySnapshot? GetVisibleEntity(int entityId);

    /// <summary>
    /// True when <see cref="GetVisibleEntities"/> would include the entity with this id in the current mode.
    /// </summary>
    bool IsEntityVisible(int entityId);

    /// <summary>
    /// R19: The observer's own player-global economy (aggregate inventory, power, defeat flag) as an
    /// immutable snapshot. Never exposes another player's aggregate stock.
    /// </summary>
    OwnEconomySnapshot GetOwnEconomy();

    /// <summary>
    /// R19: The observer's own research state (completed tech, tier, progress, tracks, unlocked
    /// capabilities/recipes) as an immutable snapshot. Never exposes another player's tech tree.
    /// </summary>
    OwnResearchSnapshot GetOwnResearch();

    /// <summary>
    /// R19: Non-allied tech-signature hotspots visible to the observer (8×8 zone aggregates). Carries no
    /// exact enemy positions — only the same coarse signal the SFML fog overlay shows.
    /// </summary>
    IReadOnlyList<TechSignatureObservation> GetTechSignatures();

    /// <summary>
    /// R19: The command vocabulary the actor may submit through an <see cref="IPlayerCommandSink"/>.
    /// Ownership/authority is still enforced per-command at apply time; this only advertises the protocol
    /// surface so a bot need not hard-code the enum.
    /// </summary>
    IReadOnlyList<SimulationCommandKind> GetAvailableCommandKinds();

    /// <summary>
    /// R19/R27: This tick's combat events the observer is allowed to see, filtered by the same fair
    /// visibility gate SFML uses for tracers. Empty in the common no-combat case.
    /// </summary>
    IReadOnlyList<ObservedCombatEvent> GetEventsThisTick();

    /// <summary>
    /// Captures a whole-observation snapshot bound to the current tick. Safe to retain: values are
    /// copied and never track later simulation changes.
    /// </summary>
    PlayerObservationSnapshot CaptureSnapshot();
}
