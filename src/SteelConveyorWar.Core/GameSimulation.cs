namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation : ISimulationSystemContext
{
    /// <summary>
    /// Fallback fixed tick rate used when a match does not specify one via
    /// <see cref="GameCreationOptions.TicksPerSecond"/>. The per-match rate is
    /// exposed as the instance <see cref="TicksPerSecond"/> property, which drives
    /// all Core duration-in-ticks conversions (build timing fallbacks, energy
    /// history windows). Host loops derive wall-clock pacing from the same
    /// configured value (see <see cref="GameSettings.TicksPerSecond"/>, loaded
    /// from <c>game.json</c>).
    /// </summary>
    public const int DefaultTicksPerSecond = 30;

    /// <summary>
    /// R32: the effective tick rate for THIS match, sourced from
    /// <see cref="GameCreationOptions.TicksPerSecond"/>. All time-dependent Core
    /// quantities derive from this value, so changing TPS retimes the match
    /// consistently. Equals <see cref="DefaultTicksPerSecond"/> unless overridden.
    /// </summary>
    public int TicksPerSecond { get; }

    private readonly List<PlayerState> _players;
    private readonly ResearchSystem _researchSystem;
    private readonly SimulationPresentationSink _presentation = new();
    private readonly List<PlayerState> _fogAlliedScratch = new();
    // R06: Power/Combat are standalone systems now; each owns its own per-tick state and scratch buffers.
    private readonly PowerSystem _powerSystem;
    private readonly CombatSystem _combatSystem;
    // Reused across ticks to avoid LINQ/ToList allocations on hot simulation paths.
    private readonly List<WorldEntity> _scratchEntities = new();
    private readonly List<WorldEntity> _scratchDeadBastions = new();
    private readonly List<WorldEntity> _scratchCascadeUnits = new();
    private readonly List<ConveyorItem> _scratchConveyorItems = new();
    private readonly List<(int SourceId, WorldEntity Inserter, WorldEntity Source)> _scratchInserterExtracts = new();
    // R17: factory/bastion accounting scratch (sorted by Id; updated live on mid-tick spawn).
    private readonly List<WorldEntity> _scratchFactories = new();
    private readonly List<WorldEntity> _scratchBastions = new();
    private readonly List<WorldEntity> _scratchUnits = new();
    private readonly List<WorldEntity> _scratchBastionUnits = new();
    private readonly Dictionary<int, int> _scratchRemainingDeficit = new();
    private readonly List<EntityKind> _scratchTemplateKinds = new();
    private readonly List<TilePosition> _scratchSpawnCandidates = new();
    private int _armyAccountingEpoch;
    private int _armyAccountingAliveUnits = -1;
    // R07: shared spatial queries (movement collision / defend threat) + pooled A* scratch.
    private readonly SpatialQueryIndex _spatialQueryIndex = new();
    private readonly PathfindingWorkspace _pathfindingWorkspace = new();
    private int _nextEntityId = 1;

    private GameSimulation(
        GameWorld world,
        IReadOnlyList<PlayerState> players,
        int randomSeed,
        ResearchCatalog catalog,
        ResearchProfileDefinition profile,
        TileCatalog tiles,
        EntityCatalog entities,
        BuildCostCatalog buildCosts,
        GameplayTablesCatalog gameplayTables,
        int ticksPerSecond)
    {
        World = world;
        _players = players.ToList();
        RandomSeed = randomSeed;
        TicksPerSecond = ticksPerSecond;
        ResearchCatalog = catalog;
        ResearchProfile = profile;
        TileCatalog = tiles;
        EntityCatalog = entities;
        BuildCostCatalog = buildCosts;
        GameplayTables = gameplayTables;
        _researchSystem = new ResearchSystem(catalog, profile);
        _powerSystem = new PowerSystem(this);
        _combatSystem = new CombatSystem(this);
        foreach (var player in _players)
        {
            _researchSystem.InitializePlayer(player.Research);
        }
    }

    public GameWorld World { get; }

    public int RandomSeed { get; }

    public ResearchCatalog ResearchCatalog { get; }

    public ResearchProfileDefinition ResearchProfile { get; }

    public TileCatalog TileCatalog { get; }

    public EntityCatalog EntityCatalog { get; }

    public BuildCostCatalog BuildCostCatalog { get; }

    /// <summary>
    /// R18: match gameplay tables (power/stacks/footprints/recipes/combat). Prefer this over
    /// <see cref="MvpDefinitions"/> static accessors when a loaded catalog is available.
    /// </summary>
    public GameplayTablesCatalog GameplayTables { get; }

    public long Tick { get; private set; }

    // R05: ReadOnlyCollection wrapper prevents external downcast-and-mutate of the roster.
    public IReadOnlyList<PlayerState> Players => _players.AsReadOnly();

    /// <summary>
    /// Presentation side-channels (combat tracers, etc.). Not hashed; see
    /// <c>docs/MVP_IMPLEMENTATION_DECISIONS.md</c> § Presentation state in Core.
    /// </summary>
    public SimulationPresentationSink Presentation => _presentation;

    /// <summary>
    /// Convenience alias for <see cref="SimulationPresentationSink.CombatShotsThisTick"/>.
    /// Presentation-only; not included in determinism hashing.
    /// </summary>
    public IReadOnlyList<CombatShotEvent> CombatShotsThisTick => _presentation.CombatShotsThisTick;

    public GameStatus Status { get; private set; } = GameStatus.InProgress;

    public PlayerId? WinnerId { get; private set; }

    /// <summary>
    /// R31: winning team/side. Victory is decided per team, not per surviving player. For a 1v1
    /// (two single-player teams) this equals the sole survivor's team and <see cref="WinnerId"/>
    /// remains that player, so existing consumers and the deterministic state hash are unchanged.
    /// </summary>
    public int? WinnerTeamId { get; private set; }

    internal int NextEntityId => _nextEntityId;

    public string ComputeStateHash() => SimulationStateHasher.Compute(this);

    /// <summary>
    /// R13: fingerprint of the pending (not-yet-applied) command queue, ordered canonically by
    /// <c>(Tick, Actor, Sequence)</c>. Compared alongside <see cref="ComputeStateHash"/> so peers with
    /// identical present state but divergent future commands are detected before the divergence is applied.
    /// </summary>
    public string ComputePendingCommandsHash() => SimulationStateHasher.ComputePendingCommandsHash(this);

    public static GameSimulation CreateNewGame(int randomSeed = 1)
    {
        return CreateNewGame(GameCreationOptions.Default with { RandomSeed = randomSeed });
    }

    public static GameSimulation CreateNewGame(GameCreationOptions options)
    {
        if (!options.Catalog.Profiles.TryGetValue(options.ProfileId, out var profile))
        {
            throw new InvalidOperationException($"Unknown research profile '{options.ProfileId}'.");
        }

        var map = options.ResolvedMap;
        if (map.Players.Count < 2)
        {
            throw new InvalidOperationException("Map config must declare at least two players for MVP.");
        }

        var size = new WorldSize(MapPlayerDefinition.DefaultWorldWidth, MapPlayerDefinition.DefaultWorldHeight);
        var terrain = CreateStartingTerrain(size, options.RandomSeed);
        var players = map.Players
            .OrderBy(player => player.Id)
            .Select(player => new PlayerState(
                new PlayerId(player.Id),
                player.Name,
                size,
                player.TeamId,
                player.Color ?? MapPlayerDefinition.DefaultColorForPlayer(player.Id),
                options.TicksPerSecond))
            .ToArray();

        foreach (var player in players)
        {
            player.Inventory.Add(ItemId.IronPlate, 250);
            player.Inventory.Add(ItemId.CopperPlate, 120);
            player.Inventory.Add(ItemId.Coal, 40);
        }

        var simulation = new GameSimulation(
            new GameWorld(size, terrain, Array.Empty<WorldEntity>(), options.ResolvedGameplayTables),
            players,
            options.RandomSeed,
            options.Catalog,
            profile,
            options.Tiles,
            options.Entities,
            options.ResolvedBuildCosts,
            options.ResolvedGameplayTables,
            options.TicksPerSecond);
        simulation.CreateStartingEntities(map);
        simulation._powerSystem.Tick();
        simulation._powerSystem.RecordStats();
        simulation.UpdateFogOfWar();
        simulation.UpdateTechSignatures();
        return simulation;
    }

    public bool AreAllied(PlayerId a, PlayerId b)
    {
        if (a == b)
        {
            return true;
        }

        return GetPlayer(a).TeamId == GetPlayer(b).TeamId;
    }

    public bool AreAllied(PlayerId? a, PlayerId? b)
    {
        return a is not null && b is not null && AreAllied(a.Value, b.Value);
    }

    public PlayerState GetPlayer(PlayerId playerId)
    {
        return _players.Single(player => player.Id == playerId);
    }

    /// <summary>
    /// R09: roster-safe player lookup. Untrusted command paths must not throw on an unknown
    /// actor id (a DoS vector inside the tick loop); callers reject instead.
    /// </summary>
    public bool TryGetPlayer(PlayerId playerId, out PlayerState player)
    {
        player = _players.FirstOrDefault(candidate => candidate.Id == playerId)!;
        return player is not null;
    }

    // R06/R14: extracted PowerSystem owns the energy buffers; keep a forwarding member so Production/
    // FactoryBastion/Research (which call this bare on the partial class) still compile unchanged.
    // R14: internal (not public) — draining a live entity's energy is an authoritative mutation that
    // must not be reachable from Sfml/Headless/plugin hosts; only in-assembly tick code may call it.
    internal bool TryConsumeBuildingEnergy(WorldEntity building) => _powerSystem.TryConsumeBuildingEnergy(building);

    /// <summary>
    /// Fills <paramref name="into"/> with alive entities matching <paramref name="predicate"/>, sorted by
    /// id for deterministic iteration. Reuses the caller's buffer to avoid per-tick allocations. Shared by
    /// Combat/Logistics/Movement; lives here (not in a single extracted system) because it is common.
    /// </summary>
    internal void CollectSortedAliveEntities(List<WorldEntity> into, Func<WorldEntity, bool> predicate)
    {
        into.Clear();
        foreach (var entity in World.Entities)
        {
            if (entity.IsAlive && predicate(entity))
            {
                into.Add(entity);
            }
        }

        into.Sort(static (left, right) => left.Id.CompareTo(right.Id));
    }

    /// <summary>R17: invalidate idle-factory skip caches when army supply / templates / assignments change.</summary>
    private void BumpArmyAccountingEpoch() => _armyAccountingEpoch++;

    /// <summary>
    /// R17: insert <paramref name="entity"/> into a sorted-by-Id scratch list (mid-tick spawn).
    /// </summary>
    private static void InsertSortedById(List<WorldEntity> into, WorldEntity entity)
    {
        var index = into.BinarySearch(entity, WorldEntityIdComparer.Instance);
        if (index < 0)
        {
            index = ~index;
        }

        into.Insert(index, entity);
    }

    private sealed class WorldEntityIdComparer : IComparer<WorldEntity>
    {
        public static readonly WorldEntityIdComparer Instance = new();

        public int Compare(WorldEntity? x, WorldEntity? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return x.Id.CompareTo(y.Id);
        }
    }

    // R06: explicit ISimulationSystemContext members forwarding to existing (differently-visible) surface.
    IReadOnlyList<PlayerState> ISimulationSystemContext.Players => _players;

    GameplayTablesCatalog ISimulationSystemContext.GameplayTables => GameplayTables;

    void ISimulationSystemContext.SyncResolvedMaxHealth(WorldEntity entity) => SyncResolvedMaxHealth(entity);

    void ISimulationSystemContext.CascadeBastionDeaths() => CascadeBastionDeaths();

    void ISimulationSystemContext.CollectSortedAliveEntities(List<WorldEntity> into, Func<WorldEntity, bool> predicate) =>
        CollectSortedAliveEntities(into, predicate);

    // R14: explicit implementation so the internal instance method above is not forced back to public.
    bool ISimulationSystemContext.TryConsumeBuildingEnergy(WorldEntity building) => TryConsumeBuildingEnergy(building);

    public bool TryPlaceGhostBuild(PlayerId playerId, EntityKind targetKind, TilePosition position, out int ghostId)
    {
        ghostId = 0;
        var commander = World.Entities.FirstOrDefault(entity => entity.OwnerId == playerId && entity.IsAlive && entity.Kind == EntityKind.Commander);
        return commander is not null && TryPlaceGhostBuildFromCommander(commander.Id, targetKind, position, out ghostId);
    }

    /// <summary>
    /// R01 trust-boundary guard. When <paramref name="actor"/> is supplied (untrusted command
    /// path via <see cref="ApplyCommand"/>), the operated commander must belong to that actor —
    /// knowing another player's <c>CommanderId</c> must not let you act on their behalf.
    /// Trusted local/internal callers (SFML input, queued-order execution, tests that already
    /// resolve an owned commander) pass <c>null</c> to opt out of the actor check.
    /// </summary>
    private static bool IsAuthorizedCommander(WorldEntity? commander, PlayerId? actor)
    {
        if (actor is null)
        {
            return true;
        }

        return commander is not null && commander.OwnerId == actor.Value;
    }

    public bool TryPlaceGhostBuildFromCommander(
        int commanderId,
        EntityKind targetKind,
        TilePosition position,
        out int ghostId,
        Direction direction = Direction.East,
        ItemRecipeId? selectedItemRecipe = null,
        PlayerId? actor = null)
    {
        ghostId = 0;
        var commander = World.GetEntity(commanderId);
        if (commander is null || commander.Kind != EntityKind.Commander || commander.OwnerId is null || !commander.IsAlive)
        {
            return false;
        }

        if (!IsAuthorizedCommander(commander, actor))
        {
            return false;
        }

        if (!World.IsInside(position) || !BuildCostCatalog.Costs.TryGetValue(targetKind, out var cost))
        {
            return false;
        }

        if (!IsBuildUnlocked(commander.OwnerId.Value, targetKind) || !CanPlaceBuilding(targetKind, position))
        {
            return false;
        }

        if (!IsWithinBuildRadius(commander, targetKind, position))
        {
            return false;
        }

        if (!TryPayBuildCostFromCommanderOrNearbyHubs(commander, cost))
        {
            return false;
        }

        var ghost = CreateEntity(EntityKind.GhostBuild, position, commander.OwnerId);
        ghost.BuildTargetKind = targetKind;
        if (IsDirectedBuildKind(targetKind))
        {
            ghost.Direction = direction;
        }

        if (targetKind == EntityKind.Assembler && selectedItemRecipe is not null)
        {
            ghost.SelectedItemRecipe = selectedItemRecipe;
        }

        var buildTicks = BuildCostCatalog.BuildTicks.GetValueOrDefault(targetKind, TicksPerSecond);
        if (commander.OwnerId is not null)
        {
            buildTicks = ResolveStat(commander.OwnerId.Value, ResearchStatIds.ConstructionTicks, buildTicks);
        }

        ghost.ConstructionTicksRemaining = buildTicks;
        ghostId = ghost.Id;
        World.AddEntity(ghost);
        commander.QueuedBuildOrder = null;
        commander.QueuedDemolishOrder = null;
        return true;
    }

    public bool TryQueueCommanderBuild(
        int commanderId,
        EntityKind targetKind,
        TilePosition position,
        Direction direction = Direction.East,
        ItemRecipeId? selectedItemRecipe = null,
        PlayerId? actor = null)
    {
        var commander = World.GetEntity(commanderId);
        if (commander is null || commander.Kind != EntityKind.Commander || commander.OwnerId is null || !commander.IsAlive)
        {
            return false;
        }

        if (!IsAuthorizedCommander(commander, actor))
        {
            return false;
        }

        if (!BuildCostCatalog.Costs.ContainsKey(targetKind) || !IsBuildUnlocked(commander.OwnerId.Value, targetKind) || !CanPlaceBuilding(targetKind, position))
        {
            return false;
        }

        if (IsWithinBuildRadius(commander, targetKind, position))
        {
            return TryPlaceGhostBuildFromCommander(commanderId, targetKind, position, out _, direction, selectedItemRecipe, actor);
        }

        commander.QueuedBuildOrder = new CommanderBuildOrder(targetKind, position, direction, selectedItemRecipe);
        commander.QueuedDemolishOrder = null;
        commander.IsGarrisoned = false;
        commander.MoveTarget = null;
        ResetMovementPath(commander);
        return true;
    }

    private static bool IsDirectedBuildKind(EntityKind kind)
    {
        return kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter;
    }

    public bool TryRotateEntity(int entityId, PlayerId actorPlayerId, bool clockwise)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null
            || entity.OwnerId != actorPlayerId
            || entity.Kind is not (EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter))
        {
            return false;
        }

        entity.Direction = Rotate(entity.Direction, clockwise);
        return true;
    }

    public bool TryIssueMoveCommand(int entityId, PlayerId actorPlayerId, TilePosition target)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null
            || entity.OwnerId != actorPlayerId
            || entity.Kind != EntityKind.Commander
            || !entity.IsAlive
            || !World.IsInside(target))
        {
            return false;
        }

        entity.MoveTarget = target;
        entity.QueuedBuildOrder = null;
        entity.QueuedDemolishOrder = null;
        entity.IsGarrisoned = false;
        ResetMovementPath(entity);
        return true;
    }

    public bool TryStopCommander(int commanderId, PlayerId actorPlayerId)
    {
        var commander = World.GetEntity(commanderId);
        if (commander is null
            || commander.OwnerId != actorPlayerId
            || commander.Kind != EntityKind.Commander
            || !commander.IsAlive)
        {
            return false;
        }

        commander.MoveTarget = null;
        commander.QueuedBuildOrder = null;
        commander.QueuedDemolishOrder = null;
        ResetMovementPath(commander);
        return true;
    }

    /// <summary>
    /// True when the commander can demolish/queue-demolish the target (ownership, kind, alive).
    /// </summary>
    public bool IsDemolishableTarget(int commanderId, int targetEntityId)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        return TryResolveDemolishCostKind(commander, target, out _);
    }

    public bool TryQueueCommanderDemolish(int commanderId, int targetEntityId, PlayerId? actor = null)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryResolveDemolishCostKind(commander, target, out var costKind))
        {
            return false;
        }

        if (!IsAuthorizedCommander(commander, actor))
        {
            return false;
        }

        if (IsWithinBuildRadius(commander!, costKind, target!.Position))
        {
            return TryDemolishBuilding(commanderId, targetEntityId, actor);
        }

        commander!.QueuedDemolishOrder = new CommanderDemolishOrder(targetEntityId);
        commander.QueuedBuildOrder = null;
        commander.IsGarrisoned = false;
        commander.MoveTarget = null;
        ResetMovementPath(commander);
        return true;
    }

    public bool TryDemolishBuilding(int commanderId, int targetEntityId, PlayerId? actor = null)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryResolveDemolishCostKind(commander, target, out var costKind))
        {
            return false;
        }

        if (!IsAuthorizedCommander(commander, actor))
        {
            return false;
        }

        if (!IsWithinBuildRadius(commander!, costKind, target!.Position))
        {
            return false;
        }

        if (!BuildCostCatalog.Costs.TryGetValue(costKind, out var fullCost))
        {
            return false;
        }

        var transfer = new Dictionary<ItemId, int>();
        CollectInventoryInto(transfer, target.Inventory);
        CollectInventoryInto(transfer, target.InputBuffer);
        CollectInventoryInto(transfer, target.OutputBuffer);
        foreach (var slot in target.ConveyorItems)
        {
            AddTransferAmount(transfer, slot.Item, 1);
        }

        if (target.HeldItem is { } held)
        {
            AddTransferAmount(transfer, held, 1);
        }

        if (target.PendingOutputItem is { } pending)
        {
            AddTransferAmount(transfer, pending, Math.Max(1, target.PendingOutputAmount));
        }

        foreach (var pair in fullCost.OrderBy(entry => entry.Key))
        {
            var refund = pair.Value / 2;
            if (refund > 0)
            {
                AddTransferAmount(transfer, pair.Key, refund);
            }
        }

        target.Inventory.Clear();
        target.InputBuffer.Clear();
        target.OutputBuffer.Clear();
        target.ConveyorItemsMutable.Clear();
        target.HeldItem = null;
        target.HeldTransferTicksRemaining = 0;
        target.PendingOutputItem = null;
        target.PendingOutputAmount = 1;
        target.WorkTicksRemaining = 0;
        target.WorkTicksTotal = 0;

        // Exclude the demolish target so hub scrap/overflow cannot re-deposit into the dying hub.
        DepositItemsToCommanderOrNearbyHubs(commander!, transfer, excludeEntityId: target.Id);

        target.Health = 0;
        if (commander!.QueuedDemolishOrder?.TargetEntityId == targetEntityId)
        {
            commander.QueuedDemolishOrder = null;
        }

        return true;
    }

    public int GetResolvedConveyorMoveTicks(WorldEntity conveyor)
    {
        var moveTicks = MvpDefinitions.ConveyorMoveTicks;
        if (conveyor.OwnerId is not null)
        {
            moveTicks = ResolveStat(conveyor.OwnerId.Value, ResearchStatIds.ConveyorMoveTicks, moveTicks);
        }

        return Math.Max(1, moveTicks);
    }

    private bool TryResolveDemolishCostKind(WorldEntity? commander, WorldEntity? target, out EntityKind costKind)
    {
        costKind = default;
        if (commander is null
            || commander.Kind != EntityKind.Commander
            || commander.OwnerId is null
            || !commander.IsAlive
            || target is null
            || !target.IsAlive
            || target.OwnerId != commander.OwnerId)
        {
            return false;
        }

        if (target.Kind == EntityKind.GhostBuild)
        {
            if (target.BuildTargetKind is not { } ghostTarget
                || ghostTarget == EntityKind.Bastion
                || !BuildCostCatalog.Costs.ContainsKey(ghostTarget))
            {
                return false;
            }

            costKind = ghostTarget;
            return true;
        }

        if (target.Kind == EntityKind.Bastion
            || target.Kind == EntityKind.Commander
            || MvpDefinitions.UnitKinds.Contains(target.Kind)
            || !BuildCostCatalog.Costs.ContainsKey(target.Kind))
        {
            return false;
        }

        costKind = target.Kind;
        return true;
    }

    private static void CollectInventoryInto(Dictionary<ItemId, int> transfer, Inventory inventory)
    {
        foreach (var pair in inventory.Items)
        {
            AddTransferAmount(transfer, pair.Key, pair.Value);
        }
    }

    private static void AddTransferAmount(Dictionary<ItemId, int> transfer, ItemId item, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        transfer[item] = transfer.GetValueOrDefault(item) + amount;
    }

    /// <summary>
    /// Deposits into commander first (per-item stack cap), then owned hubs in interact radius by id.
    /// Remaining amounts are discarded.
    /// </summary>
    private void DepositItemsToCommanderOrNearbyHubs(
        WorldEntity commander,
        IReadOnlyDictionary<ItemId, int> items,
        int? excludeEntityId = null)
    {
        if (items.Count == 0 || commander.OwnerId is null)
        {
            return;
        }

        var hubs = World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.Kind == EntityKind.Hub
                && entity.OwnerId == commander.OwnerId
                && (excludeEntityId is null || entity.Id != excludeEntityId.Value)
                && DistanceSquaredToFootprint(commander.WorldPosition, entity.Kind, entity.Position)
                    <= Square(MvpDefinitions.CommanderInteractRadius * WorldUnits.MilliPerTile))
            .OrderBy(entity => entity.Id)
            .ToList();

        foreach (var pair in items.OrderBy(entry => entry.Key))
        {
            var left = pair.Value;
            if (left <= 0)
            {
                continue;
            }

            var commanderRoom = GameplayTables.GetMaxStackSize(pair.Key) - commander.Inventory.Count(pair.Key);
            if (commanderRoom > 0)
            {
                var toCommander = Math.Min(commanderRoom, left);
                commander.Inventory.Add(pair.Key, toCommander);
                left -= toCommander;
            }

            if (left <= 0)
            {
                continue;
            }

            var hubStacks = GetHubStorageStacks(commander.OwnerId);
            foreach (var hub in hubs)
            {
                if (left <= 0)
                {
                    break;
                }

                while (left > 0 && hub.Inventory.TryAddWithinTotalStackLimit(pair.Key, 1, hubStacks, GameplayTables))
                {
                    left--;
                }
            }
        }
    }

    public bool TryStartResearch(PlayerId playerId, TechnologyId technology, PlayerId? actor = null)
    {
        return TrySelectResearch(playerId, technology, actor: actor) == ResearchCommandResult.Ok;
    }

    public bool TryCancelResearch(PlayerId playerId, TechnologyId technology)
    {
        // R09: unknown actor must reject, not throw inside the (possibly untrusted) command path.
        if (!TryGetPlayer(playerId, out var player))
        {
            return false;
        }

        return _researchSystem.TryCancelResearch(player.Research, technology) == ResearchCommandResult.Ok;
    }

    public ResearchCommandResult TrySelectResearch(
        PlayerId playerId,
        TechnologyId technology,
        bool confirmExclusive = false,
        string? preferredTrackId = null,
        PlayerId? actor = null)
    {
        // R26: defense-in-depth — research is keyed to its owning player, so an actor may only steer
        // their own research. Reject cross-player attempts (e.g. selecting a visible enemy laboratory).
        if (actor.HasValue && actor.Value != playerId)
        {
            return ResearchCommandResult.NotAvailable;
        }

        if (!TryGetPlayer(playerId, out var player))
        {
            return ResearchCommandResult.NotAvailable;
        }

        return _researchSystem.TrySelectResearch(player.Research, technology, confirmExclusive, preferredTrackId);
    }

    public ResearchCommandResult TryConfirmExclusive(PlayerId playerId, string exclusiveGroupId)
    {
        if (!TryGetPlayer(playerId, out var player))
        {
            return ResearchCommandResult.NotAvailable;
        }

        return _researchSystem.TryConfirmExclusive(player.Research, exclusiveGroupId);
    }

    public ResearchCommandResult TrySetTrackAllocation(PlayerId playerId, IReadOnlyDictionary<string, int> allocations)
    {
        if (!TryGetPlayer(playerId, out var player))
        {
            return ResearchCommandResult.InvalidAllocation;
        }

        return _researchSystem.TrySetTrackAllocation(player.Research, allocations);
    }

    public ResearchCommandResult TrySetProjectWeight(
        PlayerId playerId,
        string trackId,
        TechnologyId technologyId,
        int weight)
    {
        if (!TryGetPlayer(playerId, out var player))
        {
            return ResearchCommandResult.InvalidTrack;
        }

        return _researchSystem.TrySetProjectWeight(player.Research, trackId, technologyId, weight);
    }

    public ResearchSnapshot GetResearchSnapshot(PlayerId playerId)
    {
        return _researchSystem.GetSnapshot(GetPlayer(playerId).Research);
    }

    public int ResolveStat(PlayerId playerId, string statId, int baseValue, string? selector = null, int? minValue = 1)
    {
        return ModifierResolver.Resolve(baseValue, GetPlayer(playerId).Research.AppliedModifiers, statId, selector, minValue);
    }

    public bool TrySetFactoryProduction(int factoryId, PlayerId actorPlayerId, EntityKind? outputKind, int? bastionId = null)
    {
        // bastionId is ignored: factories no longer store bastion assignment.
        _ = bastionId;
        var factory = World.GetEntity(factoryId);
        if (factory is null
            || factory.OwnerId != actorPlayerId
            || !MvpDefinitions.FactoryKinds.Contains(factory.Kind))
        {
            return false;
        }

        if (factory.WorkTicksRemaining > 0 && outputKind != factory.ProductionTargetKind)
        {
            return false;
        }

        if (outputKind is null)
        {
            factory.ProductionTargetKind = null;
            factory.IsManualProductionTarget = false;
            BumpArmyAccountingEpoch();
            return true;
        }

        if (!GameplayTables.ProductionRecipes.TryGetValue(outputKind.Value, out var recipe)
            || !CanFactoryProduce(factory.Kind, outputKind.Value)
            || !IsRecipeUnlockedForOwner(actorPlayerId, recipe))
        {
            return false;
        }

        factory.ProductionTargetKind = outputKind;
        factory.IsManualProductionTarget = true;
        BumpArmyAccountingEpoch();
        return true;
    }

    /// <summary>
    /// Obsolete: factories no longer assign to bastions. Always returns false.
    /// Spawned units pick a deficit bastion automatically.
    /// </summary>
    public bool TryAssignFactoryBastion(int factoryId, int bastionId)
    {
        _ = factoryId;
        _ = bastionId;
        return false;
    }

    /// <summary>
    /// Test/debug helper: instantly completes a technology. Not part of the production command surface.
    /// </summary>
    internal bool TryForceCompleteResearch(PlayerId playerId, TechnologyId technologyId, bool confirmExclusive = true)
    {
        var research = GetPlayer(playerId).Research;
        if (research.CompletedTechnologies.Contains(technologyId))
        {
            return true;
        }

        if (TrySelectResearch(playerId, technologyId, confirmExclusive) != ResearchCommandResult.Ok)
        {
            return false;
        }

        if (!ResearchCatalog.Technologies.TryGetValue(technologyId, out var definition))
        {
            return false;
        }

        research.ProgressWorkUnitsMutable[technologyId] = definition.Cost.EffortUnits;
        _researchSystem.EvaluatePendingCompletions(research);
        SyncResolvedMaxHealthForPlayer(playerId);
        return research.CompletedTechnologies.Contains(technologyId);
    }

    /// <summary>
    /// Test helper: snaps an entity to a tile and clears movement/garrison/cooldown so combat setups stay deterministic.
    /// </summary>
    internal bool TryTeleportEntityForTests(int entityId, TilePosition position)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null || !entity.IsAlive)
        {
            return false;
        }

        World.RelocateEntity(entity, position);
        entity.WorldPosition = WorldPosition.FromTileCenter(position);
        entity.MoveTarget = null;
        ResetMovementPath(entity);
        entity.IsGarrisoned = false;
        entity.AttackCooldownRemaining = 0;
        return true;
    }

    /// <summary>Test helper: whether <paramref name="entityId"/> may occupy <paramref name="position"/>.</summary>
    internal bool CanOccupyWorldPositionForTests(int entityId, WorldPosition position)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null)
        {
            return false;
        }

        _spatialQueryIndex.Rebuild(World.Entities, GameplayTables);
        return CanOccupyWorldPosition(entity, position, _spatialQueryIndex);
    }

    /// <summary>
    /// Test helper: sets entity health within [0, MaxHealth] for combat/victory scenarios.
    /// </summary>
    internal bool TrySetEntityHealthForTests(int entityId, int health)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null || health < 0 || health > entity.MaxHealth)
        {
            return false;
        }

        entity.Health = health;
        return true;
    }

    /// <summary>Test helper: clears an entity's primary inventory (not input/output buffers).</summary>
    internal bool ClearEntityInventoryForTests(int entityId)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null)
        {
            return false;
        }

        entity.Inventory.Clear();
        return true;
    }

    /// <summary>Test helper: clears an entity's input buffer.</summary>
    internal bool ClearEntityInputBufferForTests(int entityId)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null)
        {
            return false;
        }

        entity.InputBuffer.Clear();
        return true;
    }

    /// <summary>
    /// Test helper: fills or sets a building energy buffer within capacity.
    /// </summary>
    internal bool TrySetEnergyBufferForTests(int entityId, int energy)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null || !entity.IsAlive || entity.EnergyBufferCapacity <= 0)
        {
            return false;
        }

        entity.EnergyBuffer = Math.Clamp(energy, 0, entity.EnergyBufferCapacity);
        return true;
    }

    /// <summary>
    /// Test helper: spawns a completed entity (skips ghost construction) for combat setups.
    /// </summary>
    internal bool TrySpawnEntityForTests(EntityKind kind, TilePosition position, PlayerId ownerId, out int entityId)
    {
        entityId = -1;
        if (!World.IsInside(position) || kind == EntityKind.GhostBuild)
        {
            return false;
        }

        var entity = AddCompletedEntity(kind, position, ownerId);
        entityId = entity.Id;
        return true;
    }

    /// <summary>
    /// Test helper: injects a research modifier without completing a technology.
    /// </summary>
    internal void ApplyResearchModifierForTests(PlayerId playerId, AddModifierEffect effect)
    {
        GetPlayer(playerId).Research.AppliedModifiersMutable.Add(effect);
        SyncResolvedMaxHealthForPlayer(playerId);
    }

    /// <summary>
    /// Test/helper: formula-C damage for the current research-scaled stats of attacker and target.
    /// </summary>
    internal int ComputeCombatDamageForTests(int attackerId, int targetId)
    {
        var attacker = World.GetEntity(attackerId);
        var target = World.GetEntity(targetId);
        if (attacker is null || target is null)
        {
            return 0;
        }

        return _combatSystem.ComputeDamageForTests(attacker, GameplayTables.GetStats(attacker.Kind), target);
    }

    public bool TrySetBastionTemplate(int bastionId, PlayerId actorPlayerId, EntityKind unitKind, int count)
    {
        var bastion = World.GetEntity(bastionId);
        if (bastion is null
            || bastion.OwnerId != actorPlayerId
            || bastion.Kind != EntityKind.Bastion
            || count < 0
            || !MvpDefinitions.UnitKinds.Contains(unitKind))
        {
            return false;
        }

        var currentSum = bastion.BastionTemplate.Values.Sum();
        var previous = bastion.BastionTemplate.GetValueOrDefault(unitKind);
        var proposedSum = currentSum - previous + count;
        // Player-wide template budget (not per-bastion): other owned bastions count against the same cap.
        var otherBastionSum = World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.OwnerId == bastion.OwnerId
                && entity.Kind == EntityKind.Bastion
                && entity.Id != bastionId)
            .Sum(entity => entity.BastionTemplate.Values.Sum());
        if (otherBastionSum + proposedSum > GetBastionTemplateCapacity(bastion.OwnerId.Value))
        {
            return false;
        }

        if (count == 0)
        {
            bastion.BastionTemplateMutable.Remove(unitKind);
        }
        else
        {
            bastion.BastionTemplateMutable[unitKind] = count;
        }

        BumpArmyAccountingEpoch();
        return true;
    }

    /// <summary>
    /// Live bastion unit supply for a template slot: assigned living units of <paramref name="unitKind"/>
    /// plus in-flight factory production targeting that kind for this bastion.
    /// </summary>
    public int GetBastionUnitSupply(int bastionId, EntityKind unitKind)
    {
        var bastion = World.GetEntity(bastionId);
        if (bastion is null || bastion.Kind != EntityKind.Bastion || !MvpDefinitions.UnitKinds.Contains(unitKind))
        {
            return 0;
        }

        // Public query may run outside FactoryBastionSystem.Tick — refresh scratch from live world.
        CollectSortedAliveEntities(_scratchFactories, static entity => MvpDefinitions.FactoryKinds.Contains(entity.Kind));
        CollectSortedAliveEntities(_scratchBastions, static entity => entity.Kind == EntityKind.Bastion);
        CollectSortedAliveEntities(_scratchUnits, static entity => MvpDefinitions.UnitKinds.Contains(entity.Kind));
        return CountBastionUnitSupply(bastionId, unitKind);
    }

    /// <summary>
    /// Same unlock gates factories use when picking unit recipes / bastion autofill deficits.
    /// </summary>
    public bool IsUnitProductionUnlocked(PlayerId playerId, EntityKind unitKind)
    {
        if (!GameplayTables.ProductionRecipes.TryGetValue(unitKind, out var recipe))
        {
            return false;
        }

        return IsRecipeUnlockedForOwner(playerId, recipe);
    }

    public int GetBastionTemplateCapacity(PlayerId playerId)
    {
        return ResolveStat(
            playerId,
            ResearchStatIds.BastionTemplateCapacity,
            MvpDefinitions.BaseBastionTemplateCapacity,
            minValue: 1);
    }

    public int GetMaxBastionCount(PlayerId playerId)
    {
        var player = GetPlayer(playerId);
        var baseline = CapabilityResolver.HasCapability(player.Research, ResearchCapabilityIds.AdditionalBastions)
            ? MvpDefinitions.MaxBastionsAfterUnlock
            : MvpDefinitions.BaseMaxBastions;
        return ResolveStat(playerId, ResearchStatIds.MaxBastions, baseline, minValue: 1);
    }

    public int CountOwnedBastions(PlayerId playerId)
    {
        return World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == playerId
            && (entity.Kind == EntityKind.Bastion
                || (entity.Kind == EntityKind.GhostBuild && entity.BuildTargetKind == EntityKind.Bastion)));
    }

    public bool TrySetAssemblerRecipe(int assemblerId, PlayerId actorPlayerId, ItemRecipeId recipeId)
    {
        var assembler = World.GetEntity(assemblerId);
        if (assembler is null
            || assembler.OwnerId != actorPlayerId
            || assembler.Kind != EntityKind.Assembler
            || !GameplayTables.ItemRecipes.ContainsKey(recipeId))
        {
            return false;
        }

        assembler.SelectedItemRecipe = recipeId;
        assembler.PendingOutputItem = null;
        assembler.PendingOutputAmount = 1;
        assembler.WorkTicksRemaining = 0;
        assembler.WorkTicksTotal = 0;
        return true;
    }

    public bool TryIssueBastionOrder(int bastionId, PlayerId actorPlayerId, BastionOrder order)
    {
        var bastion = World.GetEntity(bastionId);
        if (bastion is null || bastion.OwnerId != actorPlayerId || bastion.Kind != EntityKind.Bastion)
        {
            return false;
        }

        if (!IsValidBastionOrder(order))
        {
            return false;
        }

        var normalized = order.WithWaypointIndex(0);
        bastion.Order = normalized;
        foreach (var unit in World.Entities.Where(entity =>
                     entity.IsAlive
                     && entity.AssignedBastionId == bastionId
                     && MvpDefinitions.UnitKinds.Contains(entity.Kind)))
        {
            ApplyBastionOrderToUnit(unit, normalized);
        }

        return true;
    }

    private static bool IsValidBastionOrder(BastionOrder order)
    {
        return order.Kind switch
        {
            BastionOrderKind.Defend => true,
            BastionOrderKind.AttackArea or BastionOrderKind.Scout => order.Target is not null,
            BastionOrderKind.Patrol => order.WaypointList.Count is >= 2 and <= 4,
            _ => false
        };
    }

    private void ApplyBastionOrderToUnit(WorldEntity unit, BastionOrder bastionOrder)
    {
        unit.Order = ResolveOrderForUnit(unit.Kind, bastionOrder);
        // Drop stale Attack/Scout waypoints so Defend/home (or a new target) can repath immediately.
        ResetMovementPath(unit);
        if (unit.Order.Kind == BastionOrderKind.Defend)
        {
            // Stay home / garrison unless Defend active defense ungarrisons them.
            return;
        }

        TryEjectFromGarrison(unit);
    }

    private static BastionOrder ResolveOrderForUnit(EntityKind unitKind, BastionOrder bastionOrder)
    {
        return bastionOrder.Kind switch
        {
            BastionOrderKind.Scout when unitKind != EntityKind.Scout => new BastionOrder(BastionOrderKind.Defend),
            _ => bastionOrder
        };
    }

    /// <summary>
    /// Test/debug helper: grants items to player inventory without a gameplay source. Not part of the production command surface.
    /// </summary>
    internal void AddPlayerItems(PlayerId playerId, ItemId item, int amount)
    {
        GetPlayer(playerId).Inventory.Add(item, amount);
    }

    /// <summary>
    /// Test/debug helper: injects items into an entity buffer/inventory. Not part of the production command surface.
    /// </summary>
    internal bool AddItemToEntity(int entityId, ItemId item, int amount)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null)
        {
            return false;
        }

        if (entity.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor)
        {
            for (var i = 0; i < amount; i++)
            {
                if (!TryAddToConveyor(entity, item, progressTicks: 0))
                {
                    return false;
                }
            }

            return true;
        }

        if (entity.Kind == EntityKind.Hub)
        {
            return entity.Inventory.TryAddWithinTotalStackLimit(item, amount, GetHubStorageStacks(entity.OwnerId), GameplayTables);
        }

        if (IsBuildingWithBuffers(entity.Kind))
        {
            return TryAddToBuffer(entity.InputBuffer, item, amount);
        }

        entity.Inventory.Add(item, amount);
        return true;
    }

    // R14: internal (not public) — this is a test/cheat seam that injects items into a live entity with
    // no game source or actor. Kept accessible to Core.Tests via InternalsVisibleTo, closed to hosts.
    internal bool TryAddOutputItemToEntity(int entityId, ItemId item, int amount)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null)
        {
            return false;
        }

        return entity.Kind == EntityKind.Hub
            ? entity.Inventory.TryAddWithinTotalStackLimit(item, amount, GetHubStorageStacks(entity.OwnerId), GameplayTables)
            : TryAddToBuffer(entity.OutputBuffer, item, amount);
    }

    public bool TryCollectOutputBuffer(int commanderId, int targetEntityId, PlayerId? actor = null)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!IsAuthorizedCommander(commander, actor) || !TryValidateCommanderInteract(commander, target))
        {
            return false;
        }

        foreach (var item in target!.OutputBuffer.Items.ToList())
        {
            commander!.Inventory.Add(item.Key, item.Value);
        }

        target.OutputBuffer.Clear();
        return true;
    }

    /// <summary>
    /// Withdraws from hub inventory or any entity output buffer within commander interact radius.
    /// </summary>
    public bool TryWithdrawFromHubOrOutput(int commanderId, int targetEntityId, PlayerId? actor = null)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!IsAuthorizedCommander(commander, actor) || !TryValidateCommanderInteract(commander, target))
        {
            return false;
        }

        if (target!.Kind == EntityKind.Hub)
        {
            foreach (var item in target.Inventory.Items.ToList())
            {
                commander!.Inventory.Add(item.Key, item.Value);
            }

            target.Inventory.Clear();
            return true;
        }

        foreach (var item in target.OutputBuffer.Items.ToList())
        {
            commander!.Inventory.Add(item.Key, item.Value);
        }

        target.OutputBuffer.Clear();
        return true;
    }

    /// <summary>
    /// Deposits commander inventory into hub storage or a building input buffer within interact radius.
    /// Production buildings only accept items matching the current recipe; no recipe → false (move fallback).
    /// Transfers as many accepted items as fit; returns true when the target is a valid deposit destination
    /// with a non-empty accepted set (hub always) even if nothing moved due to full buffers.
    /// </summary>
    public bool TryDepositToHubOrInput(int commanderId, int targetEntityId, PlayerId? actor = null)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!IsAuthorizedCommander(commander, actor) || !TryValidateCommanderInteract(commander, target))
        {
            return false;
        }

        if (target!.Kind == EntityKind.Hub)
        {
            DepositAllMatching(commander!, target.Inventory, item => true, GetHubStorageStacks(target.OwnerId), hubMode: true);
            return true;
        }

        if (!IsBuildingWithBuffers(target.Kind))
        {
            return false;
        }

        var accepted = GetAcceptedInputItems(target);
        if (accepted is null || accepted.Count == 0)
        {
            return false;
        }

        DepositAllMatching(commander!, target.InputBuffer, accepted.Contains, hubStackLimit: null, hubMode: false);
        return true;
    }

    /// <summary>
    /// Moves all of <paramref name="item"/> from commander inventory into hub storage or building input.
    /// </summary>
    public bool TryDepositItemTypeToHubOrInput(int commanderId, int targetEntityId, ItemId item, PlayerId? actor = null)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!IsAuthorizedCommander(commander, actor) || !TryValidateCommanderInteract(commander, target))
        {
            return false;
        }

        if (target!.Kind == EntityKind.Hub)
        {
            DepositAllMatching(commander!, target.Inventory, candidate => candidate == item, GetHubStorageStacks(target.OwnerId), hubMode: true);
            return true;
        }

        if (!IsBuildingWithBuffers(target.Kind))
        {
            return false;
        }

        var accepted = GetAcceptedInputItems(target);
        if (accepted is null || !accepted.Contains(item))
        {
            return false;
        }

        DepositAllMatching(commander!, target.InputBuffer, candidate => candidate == item, hubStackLimit: null, hubMode: false);
        return true;
    }

    /// <summary>
    /// Moves all of <paramref name="item"/> from hub inventory or building output into commander inventory.
    /// </summary>
    public bool TryWithdrawItemTypeFromHubOrOutput(int commanderId, int targetEntityId, ItemId item, PlayerId? actor = null)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!IsAuthorizedCommander(commander, actor) || !TryValidateCommanderInteract(commander, target))
        {
            return false;
        }

        if (target!.Kind == EntityKind.Hub)
        {
            var amount = target.Inventory.Count(item);
            if (amount <= 0)
            {
                return true;
            }

            if (!target.Inventory.TryRemove(item, amount))
            {
                return false;
            }

            commander!.Inventory.Add(item, amount);
            return true;
        }

        var outputAmount = target.OutputBuffer.Count(item);
        if (outputAmount <= 0)
        {
            return true;
        }

        if (!target.OutputBuffer.TryRemove(item, outputAmount))
        {
            return false;
        }

        commander!.Inventory.Add(item, outputAmount);
        return true;
    }

    /// <summary>
    /// Items currently accepted into this building's input (empty = no recipe / refuse Ctrl+deposit).
    /// Hub is not handled here.
    /// </summary>
    public IReadOnlySet<ItemId> GetAcceptedInputItems(WorldEntity entity)
    {
        switch (entity.Kind)
        {
            case EntityKind.Assembler:
                if (entity.SelectedItemRecipe is null
                    || !GameplayTables.ItemRecipes.TryGetValue(entity.SelectedItemRecipe.Value, out var itemRecipe))
                {
                    return EmptyItemSet;
                }

                return itemRecipe.Inputs.Keys.ToHashSet();

            case EntityKind.Smelter:
                return entity.ActiveSmeltRecipe switch
                {
                    SmeltRecipeId.IronPlate => IronOreOnly,
                    SmeltRecipeId.CopperPlate => CopperOreOnly,
                    SmeltRecipeId.Steel => SteelSmeltInputs,
                    _ => EmptyItemSet
                };

            case EntityKind.Refinery:
                return CrudeOilOnly;

            case EntityKind.TankFactory:
            case EntityKind.DroneCenter:
                if (entity.ProductionTargetKind is null
                    || !GameplayTables.ProductionRecipes.TryGetValue(entity.ProductionTargetKind.Value, out var unitRecipe))
                {
                    return EmptyItemSet;
                }

                return unitRecipe.Inputs.Keys.ToHashSet();

            case EntityKind.Laboratory:
                return SciencePackInputs;

            case EntityKind.CoalPlant:
                return CoalOnly;

            case EntityKind.MachineGunTurret:
                return AmmoOnly;

            case EntityKind.CannonTurret:
                return ShellOnly;

            case EntityKind.AntiAirTurret:
                return AntiAirShellOnly;

            default:
                return EmptyItemSet;
        }
    }

    private static readonly HashSet<ItemId> EmptyItemSet = new();
    private static readonly HashSet<ItemId> IronOreOnly = new() { ItemId.IronOre };
    private static readonly HashSet<ItemId> CopperOreOnly = new() { ItemId.CopperOre };
    private static readonly HashSet<ItemId> SteelSmeltInputs = new() { ItemId.IronPlate, ItemId.Coal };
    private static readonly HashSet<ItemId> CrudeOilOnly = new() { ItemId.CrudeOil };
    private static readonly HashSet<ItemId> SciencePackInputs = new() { ItemId.SciencePackT1, ItemId.SciencePackT2 };
    private static readonly HashSet<ItemId> CoalOnly = new() { ItemId.Coal };
    private static readonly HashSet<ItemId> AmmoOnly = new() { ItemId.Ammo };
    private static readonly HashSet<ItemId> ShellOnly = new() { ItemId.Shell };
    private static readonly HashSet<ItemId> AntiAirShellOnly = new() { ItemId.AntiAirShell };

    private void DepositAllMatching(
        WorldEntity commander,
        Inventory destination,
        Func<ItemId, bool> accept,
        int? hubStackLimit,
        bool hubMode)
    {
        foreach (var item in commander.Inventory.Items.OrderBy(pair => pair.Key).ToList())
        {
            if (!accept(item.Key))
            {
                continue;
            }

            var remaining = item.Value;
            while (remaining > 0)
            {
                var chunk = Math.Min(remaining, GameplayTables.GetMaxStackSize(item.Key));
                while (chunk > 0)
                {
                    var ok = hubMode
                        ? destination.TryAddWithinTotalStackLimit(item.Key, chunk, hubStackLimit!.Value, GameplayTables)
                        : TryAddToBuffer(destination, item.Key, chunk);
                    if (ok)
                    {
                        break;
                    }

                    chunk--;
                }

                if (chunk <= 0)
                {
                    break;
                }

                commander.Inventory.TryRemove(item.Key, chunk);
                remaining -= chunk;
            }
        }
    }

    private bool TryValidateCommanderInteract(WorldEntity? commander, WorldEntity? target)
    {
        if (commander is null
            || target is null
            || commander.Kind != EntityKind.Commander
            || !commander.IsAlive
            || commander.OwnerId is null
            || target.OwnerId != commander.OwnerId)
        {
            return false;
        }

        return DistanceSquaredToFootprint(commander.WorldPosition, target.Kind, target.Position)
            <= Square(MvpDefinitions.CommanderInteractRadius * WorldUnits.MilliPerTile);
    }

    /// <summary>
    /// Test/debug helper: applies raw damage and runs death/victory cascades. Not part of the production command surface.
    /// </summary>
    internal void DamageEntity(int entityId, int damage)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null || !entity.IsAlive || damage <= 0)
        {
            return;
        }

        entity.Health = Math.Max(0, entity.Health - damage);
        CascadeBastionDeaths();
        CheckVictory();
    }

    public VisibilityState GetVisibility(PlayerId playerId, TilePosition position)
    {
        return GetPlayer(playerId).GetVisibility(position);
    }

    /// <summary>
    /// R16/R33: tiles whose FoW visibility changed on the most recent fog update for this player.
    /// Empty when vision sources were unchanged and repaint was skipped. Presentation-oriented
    /// (minimap dirty regions); not part of the determinism hash.
    /// </summary>
    public IReadOnlyList<TilePosition> GetFogDirtyTiles(PlayerId playerId)
    {
        return GetPlayer(playerId).FogDirtyTiles;
    }

    public IReadOnlyList<TechSignatureHotspot> GetTechSignatureHotspots(PlayerId playerId)
    {
        return GetPlayer(playerId).TechSignatures;
    }

    /// <summary>
    /// Creates a per-player observation surface. Prefer <see cref="PlayerObservationMode.Fair"/> for bots/net clients;
    /// <see cref="PlayerObservationMode.Cheat"/> keeps unfiltered <see cref="World"/> access for tests/tools.
    /// </summary>
    public IPlayerView CreatePlayerView(PlayerId playerId, PlayerObservationMode mode = PlayerObservationMode.Fair)
    {
        return new PlayerView(this, playerId, mode);
    }

    public void AdvanceTick()
    {
        if (Status != GameStatus.InProgress)
        {
            // DamageEntity / similar APIs may kill a commander and end the match without
            // reaching RemoveDead; still purge corpses if a host advances after game-over.
            World.RemoveDead();
            return;
        }

        Tick++;
        ApplyQueuedCommandsForCurrentTick();
        CommanderOrdersSystem.Tick(this);
        _powerSystem.Tick();
        ProductionSystem.Tick(this);
        LogisticsSystem.Tick(this);
        ResearchTickSystem.Tick(this);
        FactoryBastionSystem.Tick(this);
        MovementSystem.Tick(this);
        _combatSystem.Tick();
        _powerSystem.RecordStats();
        VictorySystem.Tick(this);
        World.RemoveDead();
        FogOfWarSystem.Tick(this);
    }

    private void CreateStartingEntities(MapSettings map)
    {
        TilePosition? referenceSolar = null;
        foreach (var definition in map.Players.OrderBy(player => player.Id))
        {
            // Roster-only seats (e.g. ally id 3 in tests) may omit start geometry — skip spawn.
            if (definition.StartCommander is null
                && definition.StartBastion is null
                && definition.StartHub is null
                && definition.Id is not (1 or 2))
            {
                continue;
            }

            var playerId = new PlayerId(definition.Id);
            var (commanderTile, bastionTile, hubTile) = definition.ResolveStartPositions(World.Size);

            var commander = AddCompletedEntity(EntityKind.Commander, commanderTile, playerId);
            AddStartingCommanderInventory(commander);
            var bastion = AddCompletedEntity(EntityKind.Bastion, bastionTile, playerId);
            // Hub is 2x2; keep within CommanderInteractRadius of the commander and clear of bastion 3x3.
            AddCompletedEntity(EntityKind.Hub, hubTile, playerId);

            TilePosition solarTile;
            if (referenceSolar is null)
            {
                solarTile = ChooseStartingSolarTile(bastion);
                referenceSolar = solarTile;
            }
            else
            {
                // Mirror fairness vs the first seat's solar when possible; otherwise pick adjacent grass.
                var mirroredSolar = new TilePosition(World.Size.Width - 1 - referenceSolar.Value.X, referenceSolar.Value.Y);
                solarTile = IsValidStartingSolarTile(bastion, mirroredSolar)
                    ? mirroredSolar
                    : ChooseStartingSolarTile(bastion);
            }

            AddCompletedEntity(EntityKind.SolarPanel, solarTile, playerId);
        }
    }

    private TilePosition ChooseStartingSolarTile(WorldEntity bastion)
    {
        var bastionTiles = GameWorld.GetFootprintTiles(bastion.Kind, bastion.Position, GameplayTables).ToHashSet();
        var candidates = new List<TilePosition>();
        foreach (var tile in bastionTiles)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    var candidate = new TilePosition(tile.X + dx, tile.Y + dy);
                    if (!IsValidStartingSolarTile(bastion, candidate))
                    {
                        continue;
                    }

                    candidates.Add(candidate);
                }
            }
        }

        var ordered = candidates
            .Distinct()
            .OrderBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .ToList();
        if (ordered.Count == 0)
        {
            throw new InvalidOperationException($"No grass tile adjacent to bastion for starting solar (owner {bastion.OwnerId?.Value}).");
        }

        return ordered[0];
    }

    private bool IsValidStartingSolarTile(WorldEntity bastion, TilePosition candidate)
    {
        if (!World.IsInside(candidate))
        {
            return false;
        }

        var bastionTiles = GameWorld.GetFootprintTiles(bastion.Kind, bastion.Position, GameplayTables).ToHashSet();
        if (bastionTiles.Contains(candidate))
        {
            return false;
        }

        // Chebyshev distance 1 from bastion footprint (adjacent including diagonals).
        var adjacent = bastionTiles.Any(tile =>
            Math.Max(Math.Abs(tile.X - candidate.X), Math.Abs(tile.Y - candidate.Y)) == 1);
        if (!adjacent)
        {
            return false;
        }

        if (World.GetTerrain(candidate) != TerrainType.Grass)
        {
            return false;
        }

        return !World.GetEntitiesAt(candidate).Any(entity => entity.IsAlive);
    }

    private static void AddStartingCommanderInventory(WorldEntity commander)
    {
        commander.Inventory.Add(ItemId.IronPlate, 250);
        commander.Inventory.Add(ItemId.CopperPlate, 120);
        commander.Inventory.Add(ItemId.Coal, 40);
        commander.Inventory.Add(ItemId.Steel, 20);
    }

    private WorldEntity AddCompletedEntity(EntityKind kind, TilePosition position, PlayerId ownerId)
    {
        var entity = CreateEntity(kind, position, ownerId);
        World.AddEntity(entity);
        return entity;
    }

    private WorldEntity CreateEntity(EntityKind kind, TilePosition position, PlayerId? ownerId)
    {
        var entity = new WorldEntity(_nextEntityId++, kind, position, ownerId);
        ConfigureEntityDefaults(entity);
        SyncResolvedMaxHealth(entity);
        return entity;
    }

    private void ConfigureEntityDefaults(WorldEntity entity)
    {
        entity.EnergyBufferCapacity = GameplayTables.GetEnergyBufferCapacity(entity.Kind);
        entity.EnergyBuffer = 0;
        // Assembler / factory default recipe is none until the player (or autofill) selects one.
    }

    /// <summary>
    /// Applies research-scaled MaxHealth (and clamps/extends current HP when the cap changes).
    /// </summary>
    private void SyncResolvedMaxHealth(WorldEntity entity)
    {
        if (entity.OwnerId is null)
        {
            return;
        }

        var baseline = GameplayTables.GetStats(entity.Kind).MaxHealth;
        var resolved = ResolveStat(
            entity.OwnerId.Value,
            ResearchStatIds.MaxHealth,
            baseline,
            entity.Kind.ToString(),
            minValue: 1);
        if (resolved == entity.MaxHealth)
        {
            return;
        }

        var delta = resolved - entity.MaxHealth;
        entity.MaxHealth = resolved;
        if (delta > 0)
        {
            entity.Health += delta;
        }
        else
        {
            entity.Health = Math.Min(entity.Health, resolved);
        }
    }

    private void SyncResolvedMaxHealthForPlayer(PlayerId playerId)
    {
        foreach (var entity in World.Entities.Where(candidate => candidate.OwnerId == playerId && candidate.IsAlive))
        {
            SyncResolvedMaxHealth(entity);
        }
    }

    private void SyncAllResolvedMaxHealth()
    {
        foreach (var player in _players)
        {
            SyncResolvedMaxHealthForPlayer(player.Id);
        }
    }


    private static TerrainType[,] CreateStartingTerrain(WorldSize size, int randomSeed)
    {
        // Local RNG only — not retained for later ticks (determinism stays seed → layout).
        var rng = new Random(randomSeed);
        var terrain = new TerrainType[size.Width, size.Height];
        var halfWidth = size.Width / 2;

        // Left-half start ores near Blue; right half is mirrored for PvP fairness.
        // Keep Fe/Cu within CommanderBuildRadius of the start (commander ~ (4, midY)).
        // Prefer vertical separation; baseX near commander so Manhattan distance stays ≤ radius after jitter.
        var midY = size.Height / 2;
        var ironCenter = JitterTile(rng, baseX: 4, baseY: midY - 10, maxOffset: 1, minX: 3, maxX: 7, minY: midY - 12, maxY: midY - 8);
        var copperCenter = JitterTile(rng, baseX: 4, baseY: midY + 10, maxOffset: 1, minX: 3, maxX: 7, minY: midY + 8, maxY: midY + 12);
        // Chebyshev radius ≥ 2 → bounding box at least 5×5 (≥ 4×4 requirement).
        FillOrePatchLeftHalf(terrain, ironCenter, TerrainType.IronOre, maxDistance: 2 + rng.Next(0, 2), halfWidth);
        FillOrePatchLeftHalf(terrain, copperCenter, TerrainType.CopperOre, maxDistance: 2 + rng.Next(0, 2), halfWidth);

        // Coal/oil farther from the start, near the center of the left half, then mirrored.
        var coalCenter = JitterTile(
            rng,
            baseX: halfWidth - 16,
            baseY: midY - 20,
            maxOffset: 2,
            minX: halfWidth - 28,
            maxX: halfWidth - 1,
            minY: 16,
            maxY: midY - 8);
        var oilCenter = JitterTile(
            rng,
            baseX: halfWidth - 16,
            baseY: midY + 20,
            maxOffset: 2,
            minX: halfWidth - 28,
            maxX: halfWidth - 1,
            minY: midY + 8,
            maxY: size.Height - 16);
        FillOrePatchLeftHalf(terrain, coalCenter, TerrainType.Coal, maxDistance: 2 + rng.Next(0, 2), halfWidth);
        FillOrePatchLeftHalf(terrain, oilCenter, TerrainType.Oil, maxDistance: 2 + rng.Next(0, 2), halfWidth);

        MirrorResourceTilesLeftToRight(terrain, halfWidth);
        return terrain;
    }

    private static TilePosition JitterTile(
        Random rng,
        int baseX,
        int baseY,
        int maxOffset,
        int minX,
        int maxX,
        int minY,
        int maxY)
    {
        var x = Math.Clamp(baseX + rng.Next(-maxOffset, maxOffset + 1), minX, maxX);
        var y = Math.Clamp(baseY + rng.Next(-maxOffset, maxOffset + 1), minY, maxY);
        return new TilePosition(x, y);
    }

    private static void FillOrePatchLeftHalf(TerrainType[,] terrain, TilePosition center, TerrainType type, int maxDistance, int halfWidth)
    {
        // Chebyshev (square) fill so min radius 2 yields at least a 5×5 AABB (≥ 4×4).
        for (var y = center.Y - maxDistance; y <= center.Y + maxDistance; y++)
        {
            for (var x = center.X - maxDistance; x <= center.X + maxDistance; x++)
            {
                var distance = Math.Max(Math.Abs(center.X - x), Math.Abs(center.Y - y));
                if (distance <= maxDistance
                    && x >= 0
                    && y >= 0
                    && x < halfWidth
                    && y < terrain.GetLength(1))
                {
                    terrain[x, y] = type;
                }
            }
        }
    }

    private static void MirrorResourceTilesLeftToRight(TerrainType[,] terrain, int halfWidth)
    {
        var width = terrain.GetLength(0);
        var height = terrain.GetLength(1);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < halfWidth; x++)
            {
                var tile = terrain[x, y];
                if (tile != TerrainType.Grass)
                {
                    terrain[width - 1 - x, y] = tile;
                }
            }
        }
    }

    private bool CanPlaceBuilding(EntityKind targetKind, TilePosition anchor)
    {
        var tiles = GameWorld.GetFootprintTiles(targetKind, anchor, GameplayTables).ToList();
        if (tiles.Any(tile => !World.IsInside(tile)))
        {
            return false;
        }

        if (RequiresResourceTerrain(targetKind) && !IsValidResourceAnchor(targetKind, anchor))
        {
            return false;
        }

        if (!BlocksPlacement(targetKind))
        {
            return true;
        }

        return tiles.All(tile => !World.GetEntitiesAt(tile).Any(entity => entity.IsAlive));
    }

    private bool IsValidResourceAnchor(EntityKind kind, TilePosition anchor)
    {
        var terrain = World.GetTerrain(anchor);
        return kind switch
        {
            EntityKind.Mine => terrain is TerrainType.IronOre or TerrainType.CopperOre,
            EntityKind.CoalMine => terrain == TerrainType.Coal,
            EntityKind.OilWell => terrain == TerrainType.Oil,
            _ => true
        };
    }

    private static bool RequiresResourceTerrain(EntityKind kind)
    {
        return kind is EntityKind.Mine or EntityKind.CoalMine or EntityKind.OilWell;
    }

    private bool IsWithinBuildRadius(WorldEntity commander, EntityKind targetKind, TilePosition anchor)
    {
        return DistanceSquaredToFootprint(commander.WorldPosition, targetKind, anchor)
            <= Square(MvpDefinitions.CommanderBuildRadius * WorldUnits.MilliPerTile);
    }

    /// <summary>
    /// Pays ghost-build cost from commander inventory first, then remaining from owned hubs within
    /// <see cref="MvpDefinitions.CommanderInteractRadius"/> (Euclidean distance to hub footprint,
    /// same check as Ctrl withdraw/deposit), ordered by entity id. All-or-nothing: no partial spend
    /// when the combined stock cannot cover cost. Construction-drone research only changes build
    /// ticks; drones never pull hub stock.
    /// </summary>
    private bool TryPayBuildCostFromCommanderOrNearbyHubs(WorldEntity commander, IReadOnlyDictionary<ItemId, int> cost)
    {
        if (cost.Count == 0)
        {
            return true;
        }

        if (commander.Inventory.HasAll(cost))
        {
            return commander.Inventory.TryRemoveAll(cost);
        }

        if (commander.OwnerId is null)
        {
            return false;
        }

        var hubs = World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.Kind == EntityKind.Hub
                && entity.OwnerId == commander.OwnerId
                && DistanceSquaredToFootprint(commander.WorldPosition, entity.Kind, entity.Position)
                    <= Square(MvpDefinitions.CommanderInteractRadius * WorldUnits.MilliPerTile))
            .OrderBy(entity => entity.Id)
            .ToList();

        var remainingAfterCommander = new Dictionary<ItemId, int>();
        foreach (var pair in cost.OrderBy(entry => entry.Key))
        {
            var fromCommander = Math.Min(commander.Inventory.Count(pair.Key), pair.Value);
            var needFromHubs = pair.Value - fromCommander;
            if (needFromHubs > 0)
            {
                remainingAfterCommander[pair.Key] = needFromHubs;
            }
        }

        var plannedHubTake = hubs.ToDictionary(hub => hub.Id, _ => new Dictionary<ItemId, int>());
        foreach (var pair in remainingAfterCommander.OrderBy(entry => entry.Key))
        {
            var left = pair.Value;
            foreach (var hub in hubs)
            {
                if (left <= 0)
                {
                    break;
                }

                var available = hub.Inventory.Count(pair.Key) - plannedHubTake[hub.Id].GetValueOrDefault(pair.Key);
                var take = Math.Min(available, left);
                if (take <= 0)
                {
                    continue;
                }

                plannedHubTake[hub.Id][pair.Key] = plannedHubTake[hub.Id].GetValueOrDefault(pair.Key) + take;
                left -= take;
            }

            if (left > 0)
            {
                return false;
            }
        }

        foreach (var pair in cost.OrderBy(entry => entry.Key))
        {
            var fromCommander = Math.Min(commander.Inventory.Count(pair.Key), pair.Value);
            if (fromCommander > 0)
            {
                commander.Inventory.TryRemove(pair.Key, fromCommander);
            }

            var left = pair.Value - fromCommander;
            foreach (var hub in hubs)
            {
                if (left <= 0)
                {
                    break;
                }

                var take = Math.Min(plannedHubTake[hub.Id].GetValueOrDefault(pair.Key), left);
                if (take <= 0)
                {
                    continue;
                }

                hub.Inventory.TryRemove(pair.Key, take);
                left -= take;
            }
        }

        return true;
    }

    /// <summary>
    /// R30: Non-mutating affordability query used by the build UI so the menu count matches what an
    /// actual ghost-build would pay for. Mirrors <see cref="TryPayBuildCostFromCommanderOrNearbyHubs"/>
    /// payment sources (commander inventory + owned hubs within
    /// <see cref="MvpDefinitions.CommanderInteractRadius"/>). Returns how many copies of
    /// <paramref name="kind"/> the combined stock can afford. Does not mutate state.
    /// </summary>
    public int GetAffordableBuildCount(WorldEntity commander, EntityKind kind)
    {
        ArgumentNullException.ThrowIfNull(commander);
        if (!BuildCostCatalog.Costs.TryGetValue(kind, out var cost))
        {
            return 0;
        }

        if (cost.Count == 0)
        {
            return int.MaxValue;
        }

        var available = new Dictionary<ItemId, int>();
        foreach (var pair in cost)
        {
            available[pair.Key] = commander.Inventory.Count(pair.Key);
        }

        if (commander.OwnerId is not null)
        {
            var hubs = World.Entities
                .Where(entity =>
                    entity.IsAlive
                    && entity.Kind == EntityKind.Hub
                    && entity.OwnerId == commander.OwnerId
                    && DistanceSquaredToFootprint(commander.WorldPosition, entity.Kind, entity.Position)
                        <= Square(MvpDefinitions.CommanderInteractRadius * WorldUnits.MilliPerTile));
            foreach (var hub in hubs)
            {
                foreach (var pair in cost)
                {
                    available[pair.Key] += hub.Inventory.Count(pair.Key);
                }
            }
        }

        var affordable = int.MaxValue;
        foreach (var pair in cost)
        {
            if (pair.Value <= 0)
            {
                continue;
            }

            affordable = Math.Min(affordable, available[pair.Key] / pair.Value);
        }

        return affordable == int.MaxValue ? 0 : affordable;
    }

    /// <summary>
    /// R30: Public capability gate for the build menu so the UI can compose its buildable set from the
    /// authoritative catalog without re-implementing research/tier gating.
    /// </summary>
    public bool IsBuildKindAvailable(PlayerId playerId, EntityKind kind)
    {
        return BuildCostCatalog.Costs.ContainsKey(kind) && IsBuildUnlocked(playerId, kind);
    }

    private int DistanceToFootprint(TilePosition from, EntityKind targetKind, TilePosition anchor)
    {
        return GameWorld.GetFootprintTiles(targetKind, anchor, GameplayTables)
            .Min(tile => from.ManhattanDistance(tile));
    }

    private long DistanceSquaredToFootprint(WorldPosition from, EntityKind targetKind, TilePosition anchor)
    {
        var footprint = GameplayTables.GetFootprint(targetKind);
        var minX = WorldUnits.TileToMilli(anchor.X);
        var maxX = WorldUnits.TileToMilli(anchor.X + footprint.Width);
        var minY = WorldUnits.TileToMilli(anchor.Y);
        var maxY = WorldUnits.TileToMilli(anchor.Y + footprint.Height);
        var closestX = Math.Clamp(from.X, minX, maxX);
        var closestY = Math.Clamp(from.Y, minY, maxY);
        var dx = from.X - closestX;
        var dy = from.Y - closestY;
        return dx * dx + dy * dy;
    }

    private static long Square(long value) => value * value;

    private static Direction Rotate(Direction direction, bool clockwise)
    {
        return direction switch
        {
            Direction.North => clockwise ? Direction.East : Direction.West,
            Direction.East => clockwise ? Direction.South : Direction.North,
            Direction.South => clockwise ? Direction.West : Direction.East,
            Direction.West => clockwise ? Direction.North : Direction.South,
            _ => direction
        };
    }

    private bool IsBuildUnlocked(PlayerId playerId, EntityKind kind)
    {
        var player = GetPlayer(playerId);
        if (kind == EntityKind.Bastion)
        {
            return CountOwnedBastions(playerId) < GetMaxBastionCount(playerId);
        }

        if (CapabilityResolver.IsEntityUnlocked(player.Research, kind, ResearchCatalog, ResearchProfile))
        {
            return true;
        }

        if (BuildCostCatalog.Requirements.TryGetValue(kind, out var requiredTechnology))
        {
            return player.ResearchedTechnologies.Contains(requiredTechnology);
        }

        // T2-gated entities require tier unlock / capability.
        if (IsTier2Gated(kind))
        {
            return CapabilityResolver.HasCapability(player.Research, ResearchCapabilityIds.Tier2Content)
                || player.Research.UnlockedEntityKinds.Contains(kind.ToString());
        }

        return player.Research.UnlockedEntityKinds.Contains(kind.ToString());
    }

    private static bool IsTier2Gated(EntityKind kind)
    {
        return kind is EntityKind.CoalMine
            or EntityKind.OilWell
            or EntityKind.Refinery
            or EntityKind.CoalPlant
            or EntityKind.SteelWall
            or EntityKind.MediumBot
            or EntityKind.MediumTank
            or EntityKind.AntiAirBot
            or EntityKind.RocketLauncher
            or EntityKind.UndergroundConveyor
            or EntityKind.AntiAirTurret;
    }

    private static bool BlocksPlacement(EntityKind kind)
    {
        return kind != EntityKind.Conveyor && kind != EntityKind.UndergroundConveyor && kind != EntityKind.Inserter;
    }
}
