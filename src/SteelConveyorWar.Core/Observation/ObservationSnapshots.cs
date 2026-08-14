using System.Collections.Immutable;
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
/// R04/M10: Extended, owner-only detail (economy/orders). Nested collections are deep-frozen so
/// cast-mutation cannot alter a retained snapshot.
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
    IReadOnlyDictionary<EntityKind, int> BastionTemplate,
    bool InserterLongReach)
{
    public IReadOnlyDictionary<ItemId, int> Inventory
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(Inventory);

    public IReadOnlyDictionary<ItemId, int> InputBuffer
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(InputBuffer);

    public IReadOnlyDictionary<ItemId, int> OutputBuffer
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(OutputBuffer);

    public IReadOnlyDictionary<EntityKind, int> BastionTemplate
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(BastionTemplate);
}

/// <summary>
/// R19/M10: Immutable, tick-stamped snapshot of the observer's own player-global economy.
/// </summary>
public sealed record OwnEconomySnapshot(
    long ObservationTick,
    PlayerId OwnerId,
    IReadOnlyDictionary<ItemId, int> Inventory,
    int PowerProduced,
    int PowerDemand,
    bool IsDefeated)
{
    public IReadOnlyDictionary<ItemId, int> Inventory
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(Inventory);
}

/// <summary>
/// R19/M10: Immutable, tick-stamped snapshot of one research track as the observer sees its own progress.
/// </summary>
public sealed record TrackObservation(
    string TrackId,
    int AllocationBasisPoints,
    TechnologyId? ActiveSerialTarget,
    IReadOnlyDictionary<TechnologyId, int> ProjectWeights)
{
    public IReadOnlyDictionary<TechnologyId, int> ProjectWeights
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(ProjectWeights);
}

/// <summary>
/// R19/M10: Immutable, tick-stamped snapshot of the observer's own research state. Nested collections
/// are deep-frozen against cast-mutation.
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
    IReadOnlySet<string> Milestones)
{
    public IReadOnlySet<TechnologyId> CompletedTechnologies
    {
        get => field!;
        init => field = ContentFreeze.Set(value);
    } = ContentFreeze.Set(CompletedTechnologies);

    public IReadOnlyDictionary<TechnologyId, int> ProgressWorkUnits
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(ProgressWorkUnits);

    public IReadOnlyList<TrackObservation> Tracks
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(Tracks);

    public IReadOnlySet<string> AppliedCapabilities
    {
        get => field!;
        init => field = ContentFreeze.Set(value, StringComparer.Ordinal);
    } = ContentFreeze.Set(AppliedCapabilities, StringComparer.Ordinal);

    public IReadOnlySet<string> UnlockedEntityKinds
    {
        get => field!;
        init => field = ContentFreeze.Set(value, StringComparer.Ordinal);
    } = ContentFreeze.Set(UnlockedEntityKinds, StringComparer.Ordinal);

    public IReadOnlySet<string> UnlockedRecipes
    {
        get => field!;
        init => field = ContentFreeze.Set(value, StringComparer.Ordinal);
    } = ContentFreeze.Set(UnlockedRecipes, StringComparer.Ordinal);

    public IReadOnlySet<string> UnlockedItemRecipes
    {
        get => field!;
        init => field = ContentFreeze.Set(value, StringComparer.Ordinal);
    } = ContentFreeze.Set(UnlockedItemRecipes, StringComparer.Ordinal);

    public IReadOnlySet<string> Milestones
    {
        get => field!;
        init => field = ContentFreeze.Set(value, StringComparer.Ordinal);
    } = ContentFreeze.Set(Milestones, StringComparer.Ordinal);
}

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
/// R04/R19/M10: A whole-observation frame bound to a single tick — entities, economy/research,
/// tech signatures, command vocabulary, H06-safe events, and a frozen fog/terrain board. Safe to
/// retain: nested collections are hard-immutable and the fog board never tracks later simulation.
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
    IReadOnlyList<ObservedCombatEvent> EventsThisTick,
    FogBoardView FogBoard)
{
    public IReadOnlyList<VisibleEntitySnapshot> VisibleEntities
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(VisibleEntities);

    public IReadOnlyList<TechSignatureObservation> TechSignatures
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(TechSignatures);

    public IReadOnlyList<SimulationCommandKind> AvailableCommandKinds
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(AvailableCommandKinds);

    public IReadOnlyList<ObservedCombatEvent> EventsThisTick
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(EventsThisTick);
}
