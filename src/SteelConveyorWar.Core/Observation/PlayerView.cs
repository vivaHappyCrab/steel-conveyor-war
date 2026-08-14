using System.Collections.Immutable;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core;

/// <summary>
/// FoW-aware (or cheat) read model over a live <see cref="GameSimulation"/>.
/// Entity gating matches SFML: owned entities always observe; others require <see cref="VisibilityState.Visible"/>.
/// M10: nested snapshot collections are hard-immutable; <see cref="CaptureFrame"/> freezes fog/terrain
/// at the same tick as entities/economy.
/// </summary>
public sealed class PlayerView : IPlayerView
{
    // M02/M10: advertised vocabulary is a frozen ImmutableArray (cast-mutation throws / no shared mutate).
    private static readonly ImmutableArray<SimulationCommandKind> AllCommandKinds =
        SimulationCommandVocabulary.AdvertisedKinds.ToImmutableArray();

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
        return ContentFreeze.List(source.Select(entity => CaptureEntity(entity, tick)));
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
            player.Inventory.Items,
            player.PowerProduced,
            player.PowerDemand,
            player.IsDefeated);
    }

    public OwnResearchSnapshot GetOwnResearch()
    {
        var tick = _simulation.Tick;
        var research = _simulation.GetPlayer(ObserverId).Research;
        var tracks = ContentFreeze.List(
            research.Tracks.Values
                .OrderBy(track => track.TrackId, StringComparer.Ordinal)
                .Select(track => new TrackObservation(
                    track.TrackId,
                    track.AllocationBasisPoints,
                    track.ActiveSerialTarget,
                    track.ProjectWeights)));

        return new OwnResearchSnapshot(
            tick,
            research.CurrentTierId,
            research.CompletedTechnologies,
            research.ProgressWorkUnits,
            tracks,
            research.AppliedCapabilities,
            research.UnlockedEntityKinds,
            research.UnlockedRecipes,
            research.UnlockedItemRecipes,
            research.Milestones);
    }

    public IReadOnlyList<TechSignatureObservation> GetTechSignatures()
    {
        return ContentFreeze.List(
            _simulation.GetTechSignatureHotspots(ObserverId)
                .Select(hotspot => new TechSignatureObservation(hotspot.ZoneX, hotspot.ZoneY, hotspot.Intensity)));
    }

    public IReadOnlyList<SimulationCommandKind> GetAvailableCommandKinds() => AllCommandKinds;

    public IReadOnlyList<ObservedCombatEvent> GetEventsThisTick()
    {
        var tick = _simulation.Tick;
        var events = new List<ObservedCombatEvent>();
        foreach (var shot in _simulation.CombatShotsThisTick)
        {
            var reveal = CombatShotVisibility.Classify(
                _simulation,
                ObserverId,
                shot,
                cheatMode: Mode == PlayerObservationMode.Cheat);
            if (!CombatShotVisibility.IsVisible(reveal))
            {
                continue;
            }

            var (from, to) = CombatShotVisibility.SanitizeEndpoints(shot, reveal);
            events.Add(new ObservedCombatEvent(
                tick,
                shot.AttackerId,
                shot.TargetId,
                from,
                to,
                shot.ProjectileKind,
                reveal));
        }

        return ContentFreeze.List(events);
    }

    public PlayerObservationSnapshot CaptureFrame()
    {
        var tick = _simulation.Tick;
        return new PlayerObservationSnapshot(
            tick,
            ObserverId,
            Mode,
            WorldSize,
            GetVisibleEntities(),
            GetOwnEconomy(),
            GetOwnResearch(),
            GetTechSignatures(),
            GetAvailableCommandKinds(),
            GetEventsThisTick(),
            CaptureFogBoard(tick));
    }

    public PlayerObservationSnapshot CaptureSnapshot() => CaptureFrame();

    private FogBoardView CaptureFogBoard(long tick)
    {
        var size = WorldSize;
        var visibility = new VisibilityState[size.Width * size.Height];
        var terrainBuilder = ImmutableDictionary.CreateBuilder<long, TerrainType>();

        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var tile = new TilePosition(x, y);
                var state = Mode == PlayerObservationMode.Cheat
                    ? VisibilityState.Visible
                    : _simulation.GetVisibility(ObserverId, tile);
                visibility[(y * size.Width) + x] = state;

                if (Mode == PlayerObservationMode.Cheat || state != VisibilityState.Unknown)
                {
                    terrainBuilder[FogBoardView.Pack(tile)] = _simulation.World.GetTerrain(tile);
                }
            }
        }

        return new FogBoardView(tick, size, visibility, terrainBuilder.ToImmutable());
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
            entity.Inventory.Items,
            entity.InputBuffer.Items,
            entity.OutputBuffer.Items,
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
            entity.BastionTemplate,
            entity.InserterLongReach);
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
