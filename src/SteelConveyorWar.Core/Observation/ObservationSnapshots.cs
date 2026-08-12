using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core;

/// <summary>
/// R04: Immutable, tick-stamped view of one entity as observed by a player. Values are copied at
/// capture time, so a retained reference never reflects later simulation changes (e.g. an enemy that
/// walked back into fog). Enemy/ally entities expose only game-design-visible fields; the private
/// economy (<see cref="Own"/>) is populated only when the observer owns the entity.
/// </summary>
public sealed record VisibleEntitySnapshot(
    long ObservationTick,
    int Id,
    EntityKind Kind,
    PlayerId? OwnerId,
    TilePosition Position,
    WorldPosition WorldPosition,
    Direction Direction,
    int Health,
    int MaxHealth,
    bool IsOwn,
    OwnEntityDetail? Own);

/// <summary>
/// R04: Extended, owner-only detail (economy/orders). Never attached to a foreign entity's snapshot,
/// so hidden buffers/orders cannot leak through observation. All collections are defensive copies.
/// </summary>
public sealed record OwnEntityDetail(
    IReadOnlyDictionary<ItemId, int> Inventory,
    IReadOnlyDictionary<ItemId, int> InputBuffer,
    IReadOnlyDictionary<ItemId, int> OutputBuffer,
    int EnergyBuffer,
    int EnergyBufferCapacity,
    int WorkTicksRemaining,
    int WorkTicksTotal,
    ItemRecipeId? SelectedItemRecipe,
    EntityKind? ProductionTargetKind,
    bool IsManualProductionTarget,
    CommanderBuildOrder? QueuedBuildOrder,
    CommanderDemolishOrder? QueuedDemolishOrder,
    TilePosition? MoveTarget,
    BastionOrder Order,
    IReadOnlyDictionary<EntityKind, int> BastionTemplate);

/// <summary>
/// R19: Immutable, tick-stamped snapshot of the observer's own player-global economy (not per-entity).
/// Only ever populated for the observing player, so it cannot leak an opponent's aggregate stock/power.
/// <see cref="Inventory"/> is a defensive copy of the player hub-wide inventory; power figures are the
/// authoritative per-tick production/installed demand recorded on <see cref="PlayerState"/>.
/// </summary>
public sealed record OwnEconomySnapshot(
    long ObservationTick,
    PlayerId OwnerId,
    IReadOnlyDictionary<ItemId, int> Inventory,
    int PowerProduced,
    int PowerDemand,
    bool IsDefeated);

/// <summary>
/// R19: Immutable, tick-stamped snapshot of one research track as the observer sees its own progress.
/// </summary>
public sealed record TrackObservation(
    string TrackId,
    int AllocationBasisPoints,
    TechnologyId? ActiveSerialTarget,
    IReadOnlyDictionary<TechnologyId, int> ProjectWeights);

/// <summary>
/// R19: Immutable, tick-stamped snapshot of the observer's own research state — completed tech, tier,
/// per-project progress, tracks, and unlocked capabilities/recipes/entity kinds. Defensive copies only;
/// only populated for the observing player so an opponent's tech tree cannot leak through this channel.
/// </summary>
public sealed record OwnResearchSnapshot(
    long ObservationTick,
    string CurrentTierId,
    IReadOnlySet<TechnologyId> CompletedTechnologies,
    IReadOnlyDictionary<TechnologyId, int> ProgressWorkUnits,
    IReadOnlyList<TrackObservation> Tracks,
    IReadOnlySet<string> AppliedCapabilities,
    IReadOnlySet<string> UnlockedEntityKinds,
    IReadOnlySet<string> UnlockedRecipes,
    IReadOnlySet<string> UnlockedItemRecipes,
    IReadOnlySet<string> Milestones);

/// <summary>
/// R19: Tech-signature hotspot as exposed to a bot — an 8×8 zone coordinate plus aggregate intensity of
/// non-allied activity. Mirrors the SFML fog overlay signal; carries no exact enemy positions.
/// </summary>
public sealed record TechSignatureObservation(int ZoneX, int ZoneY, int Intensity);

/// <summary>
/// R19/R27/H06: Immutable, tick-stamped combat event the observer is allowed to see this tick.
/// Hidden endpoints are omitted or stubbed (<see cref="RevealMode"/>) so exact fog coordinates cannot leak.
/// Presentation-only (not part of the authoritative hash).
/// </summary>
public sealed record ObservedCombatEvent(
    long ObservationTick,
    int AttackerId,
    int TargetId,
    WorldPosition? From,
    WorldPosition? To,
    ProjectileKind ProjectileKind,
    CombatShotRevealMode RevealMode);

/// <summary>
/// R04/R19: A whole-observation snapshot bound to a single tick. Everything a bot may read is frozen here
/// so it can be retained without leaking live state: visible entities, own economy/research, tech
/// signatures, the actor's available command vocabulary, and this tick's visible events.
/// </summary>
public sealed record PlayerObservationSnapshot(
    long ObservationTick,
    PlayerId ObserverId,
    PlayerObservationMode Mode,
    WorldSize WorldSize,
    IReadOnlyList<VisibleEntitySnapshot> VisibleEntities,
    OwnEconomySnapshot OwnEconomy,
    OwnResearchSnapshot OwnResearch,
    IReadOnlyList<TechSignatureObservation> TechSignatures,
    IReadOnlyList<SimulationCommandKind> AvailableCommandKinds,
    IReadOnlyList<ObservedCombatEvent> EventsThisTick);
