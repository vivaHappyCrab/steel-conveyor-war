namespace SteelConveyorWar.Core;

/// <summary>
/// R06: the narrow contract that extracted simulation systems (Power, Combat, …) depend on instead of
/// the whole <see cref="GameSimulation"/> god-object. It exposes exactly the world/roster access and the
/// handful of shared helpers those systems need, so each system can be constructed and unit-tested
/// against an in-memory fake without spinning up a full simulation. <see cref="GameSimulation"/> is the
/// production implementation (mostly via its existing public surface, plus explicit implementations for
/// members that stay private on the class).
/// </summary>
internal interface ISimulationSystemContext
{
    /// <summary>Authoritative world state (entities, tiles, spatial queries).</summary>
    GameWorld World { get; }

    /// <summary>Current simulation tick.</summary>
    long Tick { get; }

    /// <summary>
    /// Live player roster. Unlike <see cref="GameSimulation.Players"/> this returns the backing list
    /// directly (no per-access wrapper allocation) because it is only visible to trusted internal systems.
    /// </summary>
    IReadOnlyList<PlayerState> Players { get; }

    /// <summary>Presentation side-channel for combat tracers etc. (not hashed).</summary>
    SimulationPresentationSink Presentation { get; }

    /// <summary>
    /// H01: match-scoped gameplay tables (power/recipes/stats/footprints/collision/stacks/resistances).
    /// Prefer this over <see cref="MvpDefinitions"/> Embedded accessors on production paths.
    /// </summary>
    GameplayTablesCatalog GameplayTables { get; }

    /// <summary>Looks up a player by id (throws if unknown).</summary>
    PlayerState GetPlayer(PlayerId playerId);

    /// <summary>True when both ids are present and share a team (static map-config alliances).</summary>
    bool AreAllied(PlayerId? a, PlayerId? b);

    /// <summary>Resolves a research-modified stat for a player.</summary>
    int ResolveStat(PlayerId playerId, string statId, int baseValue, string? selector = null, int? minValue = 1);

    /// <summary>Recomputes an entity's research-scaled max health, adjusting current health accordingly.</summary>
    void SyncResolvedMaxHealth(WorldEntity entity);

    /// <summary>Kills garrisoned units when their bastion dies (cascade), keeping the roster consistent.</summary>
    void CascadeBastionDeaths();

    /// <summary>
    /// Drains a building's power demand from its energy buffer, accumulating the drain into this tick's
    /// energy-stats sample. Returns false when the buffer is too low (work must pause).
    /// </summary>
    bool TryConsumeBuildingEnergy(WorldEntity building);

    /// <summary>
    /// Fills <paramref name="into"/> with alive entities matching <paramref name="predicate"/>, sorted by
    /// id for deterministic iteration. Reuses the caller's buffer to avoid per-tick allocations.
    /// </summary>
    void CollectSortedAliveEntities(List<WorldEntity> into, Func<WorldEntity, bool> predicate);

    /// <summary>
    /// M06: shared spatial index rebuilt at most twice per tick (post-commands, post-factory).
    /// Combat/movement share this instance; mid-pass movers use Relocate, spawns use InsertAlive.
    /// </summary>
    SpatialQueryIndex SharedSpatialIndex { get; }
}
