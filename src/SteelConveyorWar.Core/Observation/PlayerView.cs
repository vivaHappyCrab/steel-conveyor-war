using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core;

/// <summary>
/// FoW-aware (or cheat) read model over a live <see cref="GameSimulation"/>.
/// Entity gating matches SFML: owned entities always observe; others require <see cref="VisibilityState.Visible"/>.
/// </summary>
public sealed class PlayerView : IPlayerView
{
    // R19: the full command vocabulary a bot may submit. Ownership/authority is still enforced per
    // command at apply time; this only advertises the protocol surface so the bot need not hard-code it.
    private static readonly IReadOnlyList<SimulationCommandKind> AllCommandKinds =
        Enum.GetValues<SimulationCommandKind>();

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

    public long ObservationTick => _simulation.Tick;

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

    public IReadOnlyList<VisibleEntitySnapshot> GetVisibleEntities()
    {
        var source = Mode == PlayerObservationMode.Cheat
            ? _simulation.World.Entities.AsEnumerable()
            : _simulation.World.Entities.Where(IsFairEntityVisible);

        var tick = _simulation.Tick;
        return source.Select(entity => CaptureEntity(entity, tick)).ToList();
    }

    public VisibleEntitySnapshot? GetVisibleEntity(int entityId)
    {
        var entity = _simulation.World.GetEntity(entityId);
        if (entity is null || !IsEntityObservable(entity))
        {
            return null;
        }

        return CaptureEntity(entity, _simulation.Tick);
    }

    public bool IsEntityVisible(int entityId)
    {
        var entity = _simulation.World.GetEntity(entityId);
        return entity is not null && IsEntityObservable(entity);
    }

    public OwnEconomySnapshot GetOwnEconomy()
    {
        var player = _simulation.GetPlayer(ObserverId);
        return new OwnEconomySnapshot(
            _simulation.Tick,
            player.Id,
            new Dictionary<ItemId, int>(player.Inventory.Items),
            player.PowerProduced,
            player.PowerDemand,
            player.IsDefeated);
    }

    public OwnResearchSnapshot GetOwnResearch()
    {
        var tick = _simulation.Tick;
        var research = _simulation.GetPlayer(ObserverId).Research;
        var tracks = research.Tracks.Values
            .OrderBy(track => track.TrackId, StringComparer.Ordinal)
            .Select(track => new TrackObservation(
                track.TrackId,
                track.AllocationBasisPoints,
                track.ActiveSerialTarget,
                new Dictionary<TechnologyId, int>(track.ProjectWeights)))
            .ToList();

        return new OwnResearchSnapshot(
            tick,
            research.CurrentTierId,
            new HashSet<TechnologyId>(research.CompletedTechnologies),
            new Dictionary<TechnologyId, int>(research.ProgressWorkUnits),
            tracks,
            new HashSet<string>(research.AppliedCapabilities, StringComparer.Ordinal),
            new HashSet<string>(research.UnlockedEntityKinds, StringComparer.Ordinal),
            new HashSet<string>(research.UnlockedRecipes, StringComparer.Ordinal),
            new HashSet<string>(research.UnlockedItemRecipes, StringComparer.Ordinal),
            new HashSet<string>(research.Milestones, StringComparer.Ordinal));
    }

    public IReadOnlyList<TechSignatureObservation> GetTechSignatures()
    {
        return _simulation.GetTechSignatureHotspots(ObserverId)
            .Select(hotspot => new TechSignatureObservation(hotspot.ZoneX, hotspot.ZoneY, hotspot.Intensity))
            .ToList();
    }

    public IReadOnlyList<SimulationCommandKind> GetAvailableCommandKinds() => AllCommandKinds;

    public IReadOnlyList<ObservedCombatEvent> GetEventsThisTick()
    {
        var tick = _simulation.Tick;
        var events = new List<ObservedCombatEvent>();
        foreach (var shot in _simulation.CombatShotsThisTick)
        {
            if (!IsShotObservable(shot))
            {
                continue;
            }

            events.Add(new ObservedCombatEvent(
                tick,
                shot.AttackerId,
                shot.TargetId,
                shot.From,
                shot.To,
                shot.ProjectileKind));
        }

        return events;
    }

    public PlayerObservationSnapshot CaptureSnapshot()
    {
        return new PlayerObservationSnapshot(
            _simulation.Tick,
            ObserverId,
            Mode,
            WorldSize,
            GetVisibleEntities(),
            GetOwnEconomy(),
            GetOwnResearch(),
            GetTechSignatures(),
            GetAvailableCommandKinds(),
            GetEventsThisTick());
    }

    // R19/R27: mirror SFML's tracer gate so the event stream cannot leak hidden movement/combat.
    // Cheat mode sees everything; Fair mode requires either endpoint entity observable or an endpoint
    // tile currently Visible.
    private bool IsShotObservable(CombatShotEvent shot)
    {
        if (Mode == PlayerObservationMode.Cheat)
        {
            return true;
        }

        var attacker = _simulation.World.GetEntity(shot.AttackerId);
        if (attacker is not null && IsShotEndpointVisible(attacker))
        {
            return true;
        }

        var target = _simulation.World.GetEntity(shot.TargetId);
        if (target is not null && IsShotEndpointVisible(target))
        {
            return true;
        }

        return _simulation.GetVisibility(ObserverId, shot.From.ToTilePosition()) == VisibilityState.Visible
            || _simulation.GetVisibility(ObserverId, shot.To.ToTilePosition()) == VisibilityState.Visible;
    }

    // Matches WorldRenderer.IsVisibleToLocalPlayer: owned entities always observe; others require Visible.
    private bool IsShotEndpointVisible(WorldEntity entity)
    {
        return entity.OwnerId == ObserverId
            || _simulation.GetVisibility(ObserverId, entity.Position) == VisibilityState.Visible;
    }

    private bool IsEntityObservable(WorldEntity entity)
    {
        return Mode == PlayerObservationMode.Cheat || IsFairEntityVisible(entity);
    }

    private VisibleEntitySnapshot CaptureEntity(WorldEntity entity, long tick)
    {
        var isOwn = entity.OwnerId == ObserverId;
        return new VisibleEntitySnapshot(
            tick,
            entity.Id,
            entity.Kind,
            entity.OwnerId,
            entity.Position,
            entity.WorldPosition,
            entity.Direction,
            entity.Health,
            entity.MaxHealth,
            isOwn,
            isOwn ? CaptureOwnDetail(entity) : null);
    }

    private static OwnEntityDetail CaptureOwnDetail(WorldEntity entity)
    {
        return new OwnEntityDetail(
            new Dictionary<ItemId, int>(entity.Inventory.Items),
            new Dictionary<ItemId, int>(entity.InputBuffer.Items),
            new Dictionary<ItemId, int>(entity.OutputBuffer.Items),
            entity.EnergyBuffer,
            entity.EnergyBufferCapacity,
            entity.WorkTicksRemaining,
            entity.WorkTicksTotal,
            entity.SelectedItemRecipe,
            entity.ProductionTargetKind,
            entity.IsManualProductionTarget,
            entity.QueuedBuildOrder,
            entity.QueuedDemolishOrder,
            entity.MoveTarget,
            entity.Order,
            new Dictionary<EntityKind, int>(entity.BastionTemplate));
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
