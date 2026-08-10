namespace SteelConveyorWar.Core;

public sealed class GameSimulation
{
    /// <summary>
    /// Default fixed tick rate used by Core for duration-in-ticks conversions
    /// (build timing fallbacks, energy history windows, tests). Host loops must
    /// drive wall-clock pacing from <see cref="GameSettings.TicksPerSecond"/>
    /// (loaded from <c>game.json</c>); keep that value aligned with this default
    /// unless intentionally retiming the match.
    /// </summary>
    public const int TicksPerSecond = 30;

    private readonly List<PlayerState> _players;
    private readonly ResearchSystem _researchSystem;
    private readonly SimulationPresentationSink _presentation = new();
    private readonly Dictionary<PlayerId, int> _tickPowerProduced = new();
    private readonly Dictionary<PlayerId, int> _tickPowerConsumed = new();
    private readonly Dictionary<PlayerId, Dictionary<EntityKind, int>> _tickProducedByKind = new();
    private readonly Dictionary<PlayerId, Dictionary<EntityKind, int>> _tickConsumedByKind = new();
    private int _nextEntityId = 1;

    private GameSimulation(
        GameWorld world,
        IReadOnlyList<PlayerState> players,
        int randomSeed,
        ResearchCatalog catalog,
        ResearchProfileDefinition profile,
        TileCatalog tiles,
        EntityCatalog entities,
        BuildCostCatalog buildCosts)
    {
        World = world;
        _players = players.ToList();
        RandomSeed = randomSeed;
        ResearchCatalog = catalog;
        ResearchProfile = profile;
        TileCatalog = tiles;
        EntityCatalog = entities;
        BuildCostCatalog = buildCosts;
        _researchSystem = new ResearchSystem(catalog, profile);
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

    public long Tick { get; private set; }

    public IReadOnlyList<PlayerState> Players => _players;

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

    internal int NextEntityId => _nextEntityId;

    public string ComputeStateHash() => SimulationStateHasher.Compute(this);

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

        var size = new WorldSize(192, 112);
        var terrain = CreateStartingTerrain(size, options.RandomSeed);
        var players = map.Players
            .OrderBy(player => player.Id)
            .Select(player => new PlayerState(new PlayerId(player.Id), player.Name, size, player.TeamId))
            .ToArray();

        foreach (var player in players)
        {
            player.Inventory.Add(ItemId.IronPlate, 250);
            player.Inventory.Add(ItemId.CopperPlate, 120);
            player.Inventory.Add(ItemId.Coal, 40);
        }

        var simulation = new GameSimulation(
            new GameWorld(size, terrain, Array.Empty<WorldEntity>()),
            players,
            options.RandomSeed,
            options.Catalog,
            profile,
            options.Tiles,
            options.Entities,
            options.ResolvedBuildCosts);
        simulation.CreateStartingEntities();
        simulation.UpdatePower();
        simulation.RecordEnergyStatsSample();
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

    public bool TryPlaceGhostBuild(PlayerId playerId, EntityKind targetKind, TilePosition position, out int ghostId)
    {
        ghostId = 0;
        var commander = World.Entities.FirstOrDefault(entity => entity.OwnerId == playerId && entity.IsAlive && entity.Kind == EntityKind.Commander);
        return commander is not null && TryPlaceGhostBuildFromCommander(commander.Id, targetKind, position, out ghostId);
    }

    public bool TryPlaceGhostBuildFromCommander(
        int commanderId,
        EntityKind targetKind,
        TilePosition position,
        out int ghostId,
        Direction direction = Direction.East,
        ItemRecipeId? selectedItemRecipe = null)
    {
        ghostId = 0;
        var commander = World.GetEntity(commanderId);
        if (commander is null || commander.Kind != EntityKind.Commander || commander.OwnerId is null || !commander.IsAlive)
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
        ItemRecipeId? selectedItemRecipe = null)
    {
        var commander = World.GetEntity(commanderId);
        if (commander is null || commander.Kind != EntityKind.Commander || commander.OwnerId is null || !commander.IsAlive)
        {
            return false;
        }

        if (!BuildCostCatalog.Costs.ContainsKey(targetKind) || !IsBuildUnlocked(commander.OwnerId.Value, targetKind) || !CanPlaceBuilding(targetKind, position))
        {
            return false;
        }

        if (IsWithinBuildRadius(commander, targetKind, position))
        {
            return TryPlaceGhostBuildFromCommander(commanderId, targetKind, position, out _, direction, selectedItemRecipe);
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

    public bool TryIssueMoveCommand(int entityId, TilePosition target)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null || entity.Kind != EntityKind.Commander || !entity.IsAlive || !World.IsInside(target))
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

    public bool TryStopCommander(int commanderId)
    {
        var commander = World.GetEntity(commanderId);
        if (commander is null || commander.Kind != EntityKind.Commander || !commander.IsAlive)
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

    public bool TryQueueCommanderDemolish(int commanderId, int targetEntityId)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryResolveDemolishCostKind(commander, target, out var costKind))
        {
            return false;
        }

        if (IsWithinBuildRadius(commander!, costKind, target!.Position))
        {
            return TryDemolishBuilding(commanderId, targetEntityId);
        }

        commander!.QueuedDemolishOrder = new CommanderDemolishOrder(targetEntityId);
        commander.QueuedBuildOrder = null;
        commander.IsGarrisoned = false;
        commander.MoveTarget = null;
        ResetMovementPath(commander);
        return true;
    }

    public bool TryDemolishBuilding(int commanderId, int targetEntityId)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryResolveDemolishCostKind(commander, target, out var costKind))
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
                    <= Square(MvpDefinitions.CommanderInteractRadius))
            .OrderBy(entity => entity.Id)
            .ToList();

        foreach (var pair in items.OrderBy(entry => entry.Key))
        {
            var left = pair.Value;
            if (left <= 0)
            {
                continue;
            }

            var commanderRoom = MvpDefinitions.GetMaxStackSize(pair.Key) - commander.Inventory.Count(pair.Key);
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

                while (left > 0 && hub.Inventory.TryAddWithinTotalStackLimit(pair.Key, 1, hubStacks))
                {
                    left--;
                }
            }
        }
    }

    public bool TryStartResearch(PlayerId playerId, TechnologyId technology)
    {
        return TrySelectResearch(playerId, technology) == ResearchCommandResult.Ok;
    }

    public bool TryCancelResearch(PlayerId playerId, TechnologyId technology)
    {
        return _researchSystem.TryCancelResearch(GetPlayer(playerId).Research, technology) == ResearchCommandResult.Ok;
    }

    public ResearchCommandResult TrySelectResearch(
        PlayerId playerId,
        TechnologyId technology,
        bool confirmExclusive = false,
        string? preferredTrackId = null)
    {
        return _researchSystem.TrySelectResearch(GetPlayer(playerId).Research, technology, confirmExclusive, preferredTrackId);
    }

    public ResearchCommandResult TryConfirmExclusive(PlayerId playerId, string exclusiveGroupId)
    {
        return _researchSystem.TryConfirmExclusive(GetPlayer(playerId).Research, exclusiveGroupId);
    }

    public ResearchCommandResult TrySetTrackAllocation(PlayerId playerId, IReadOnlyDictionary<string, int> allocations)
    {
        return _researchSystem.TrySetTrackAllocation(GetPlayer(playerId).Research, allocations);
    }

    public ResearchCommandResult TrySetProjectWeight(
        PlayerId playerId,
        string trackId,
        TechnologyId technologyId,
        int weight)
    {
        return _researchSystem.TrySetProjectWeight(GetPlayer(playerId).Research, trackId, technologyId, weight);
    }

    public ResearchSnapshot GetResearchSnapshot(PlayerId playerId)
    {
        return _researchSystem.GetSnapshot(GetPlayer(playerId).Research);
    }

    public int ResolveStat(PlayerId playerId, string statId, int baseValue, string? selector = null, int? minValue = 1)
    {
        return ModifierResolver.Resolve(baseValue, GetPlayer(playerId).Research.AppliedModifiers, statId, selector, minValue);
    }

    public bool TrySetFactoryProduction(int factoryId, EntityKind? outputKind, int? bastionId = null)
    {
        // bastionId is ignored: factories no longer store bastion assignment.
        _ = bastionId;
        var factory = World.GetEntity(factoryId);
        if (factory is null || !MvpDefinitions.FactoryKinds.Contains(factory.Kind))
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
            return true;
        }

        if (!MvpDefinitions.ProductionRecipes.ContainsKey(outputKind.Value)
            || !CanFactoryProduce(factory.Kind, outputKind.Value))
        {
            return false;
        }

        factory.ProductionTargetKind = outputKind;
        factory.IsManualProductionTarget = true;
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
        return entity is not null && CanOccupyWorldPosition(entity, position);
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

        return ComputeDamageAgainst(attacker, MvpDefinitions.GetStats(attacker.Kind), target);
    }

    public bool TrySetBastionTemplate(int bastionId, EntityKind unitKind, int count)
    {
        var bastion = World.GetEntity(bastionId);
        if (bastion is null || bastion.Kind != EntityKind.Bastion || count < 0 || !MvpDefinitions.UnitKinds.Contains(unitKind))
        {
            return false;
        }

        if (bastion.OwnerId is null)
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

        return CountBastionUnitSupply(bastionId, unitKind);
    }

    /// <summary>
    /// Same unlock gates factories use when picking unit recipes / bastion autofill deficits.
    /// </summary>
    public bool IsUnitProductionUnlocked(PlayerId playerId, EntityKind unitKind)
    {
        if (!MvpDefinitions.ProductionRecipes.TryGetValue(unitKind, out var recipe))
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

    public bool TrySetAssemblerRecipe(int assemblerId, ItemRecipeId recipeId)
    {
        var assembler = World.GetEntity(assemblerId);
        if (assembler is null || assembler.Kind != EntityKind.Assembler || !MvpDefinitions.ItemRecipes.ContainsKey(recipeId))
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

    public bool TryIssueBastionOrder(int bastionId, BastionOrder order)
    {
        var bastion = World.GetEntity(bastionId);
        if (bastion is null || bastion.Kind != EntityKind.Bastion)
        {
            return false;
        }

        if (!IsValidBastionOrder(order))
        {
            return false;
        }

        var normalized = order with { WaypointIndex = 0 };
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

    private static void ApplyBastionOrderToUnit(WorldEntity unit, BastionOrder bastionOrder)
    {
        unit.Order = ResolveOrderForUnit(unit.Kind, bastionOrder);
        // Drop stale Attack/Scout waypoints so Defend/home (or a new target) can repath immediately.
        ResetMovementPath(unit);
        if (unit.Order.Kind == BastionOrderKind.Defend)
        {
            // Stay home / garrison unless Defend active defense ungarrisons them.
            return;
        }

        unit.IsGarrisoned = false;
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
            return entity.Inventory.TryAddWithinTotalStackLimit(item, amount, GetHubStorageStacks(entity.OwnerId));
        }

        if (IsBuildingWithBuffers(entity.Kind))
        {
            return TryAddToBuffer(entity.InputBuffer, item, amount);
        }

        entity.Inventory.Add(item, amount);
        return true;
    }

    public bool TryAddOutputItemToEntity(int entityId, ItemId item, int amount)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null)
        {
            return false;
        }

        return entity.Kind == EntityKind.Hub
            ? entity.Inventory.TryAddWithinTotalStackLimit(item, amount, GetHubStorageStacks(entity.OwnerId))
            : TryAddToBuffer(entity.OutputBuffer, item, amount);
    }

    public bool TryCollectOutputBuffer(int commanderId, int targetEntityId)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryValidateCommanderInteract(commander, target))
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
    public bool TryWithdrawFromHubOrOutput(int commanderId, int targetEntityId)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryValidateCommanderInteract(commander, target))
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
    public bool TryDepositToHubOrInput(int commanderId, int targetEntityId)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryValidateCommanderInteract(commander, target))
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
    public bool TryDepositItemTypeToHubOrInput(int commanderId, int targetEntityId, ItemId item)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryValidateCommanderInteract(commander, target))
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
    public bool TryWithdrawItemTypeFromHubOrOutput(int commanderId, int targetEntityId, ItemId item)
    {
        var commander = World.GetEntity(commanderId);
        var target = World.GetEntity(targetEntityId);
        if (!TryValidateCommanderInteract(commander, target))
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
    public static IReadOnlySet<ItemId> GetAcceptedInputItems(WorldEntity entity)
    {
        switch (entity.Kind)
        {
            case EntityKind.Assembler:
                if (entity.SelectedItemRecipe is null
                    || !MvpDefinitions.ItemRecipes.TryGetValue(entity.SelectedItemRecipe.Value, out var itemRecipe))
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
                    || !MvpDefinitions.ProductionRecipes.TryGetValue(entity.ProductionTargetKind.Value, out var unitRecipe))
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

    private static void DepositAllMatching(
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
                var chunk = Math.Min(remaining, MvpDefinitions.GetMaxStackSize(item.Key));
                while (chunk > 0)
                {
                    var ok = hubMode
                        ? destination.TryAddWithinTotalStackLimit(item.Key, chunk, hubStackLimit!.Value)
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

    /// <summary>
    /// Drains <see cref="MvpDefinitions.GetPowerDemand"/> from the building buffer when it can afford to run.
    /// Returns false when the buffer is too low (work must pause). No demand configured → success (unpowered-free).
    /// Successful drains accumulate into this tick's energy-stats consumption sample.
    /// </summary>
    public bool TryConsumeBuildingEnergy(WorldEntity building)
    {
        var demand = MvpDefinitions.GetPowerDemand(building.Kind);
        if (demand <= 0)
        {
            return true;
        }

        if (building.EnergyBuffer < demand)
        {
            return false;
        }

        building.EnergyBuffer -= demand;
        if (building.OwnerId is not null)
        {
            var ownerId = building.OwnerId.Value;
            _tickPowerConsumed[ownerId] = _tickPowerConsumed.GetValueOrDefault(ownerId) + demand;
            if (!_tickConsumedByKind.TryGetValue(ownerId, out var byKind))
            {
                byKind = new Dictionary<EntityKind, int>();
                _tickConsumedByKind[ownerId] = byKind;
            }

            byKind[building.Kind] = byKind.GetValueOrDefault(building.Kind) + demand;
        }

        return true;
    }

    private static bool TryValidateCommanderInteract(WorldEntity? commander, WorldEntity? target)
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
            <= Square(MvpDefinitions.CommanderInteractRadius);
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
        ProcessCommanderBuildOrders();
        ProcessCommanderDemolishOrders();
        ProcessCommanderMoveCommands();
        CompleteGhostBuilds();
        UpdatePower();
        ProduceRawResources();
        ProcessBuildingWork();
        ProcessLogistics();
        ProcessResearch();
        ProcessFactoryProduction();
        ProcessBastions();
        ProcessMovement();
        ProcessCombat();
        CascadeBastionDeaths();
        ProcessRepairOutOfCombat();
        RecordEnergyStatsSample();
        CheckVictory();
        World.RemoveDead();
        UpdateFogOfWar();
        UpdateTechSignatures();
    }

    private void CreateStartingEntities()
    {
        var playerOne = new PlayerId(1);
        var playerTwo = new PlayerId(2);
        var midY = World.Size.Height / 2;

        var commanderOne = AddCompletedEntity(EntityKind.Commander, new TilePosition(4, midY), playerOne);
        AddStartingCommanderInventory(commanderOne);
        var bastionOne = AddCompletedEntity(EntityKind.Bastion, new TilePosition(1, midY), playerOne);
        // Hub is 2x2; keep within CommanderInteractRadius of the БМК and clear of bastion 3x3 (x=1..3).
        AddCompletedEntity(EntityKind.Hub, new TilePosition(5, midY + 2), playerOne);
        var solarOne = ChooseStartingSolarTile(bastionOne);
        AddCompletedEntity(EntityKind.SolarPanel, solarOne, playerOne);

        var commanderTwo = AddCompletedEntity(EntityKind.Commander, new TilePosition(World.Size.Width - 5, midY), playerTwo);
        AddStartingCommanderInventory(commanderTwo);
        var bastionTwo = AddCompletedEntity(EntityKind.Bastion, new TilePosition(World.Size.Width - 4, midY), playerTwo);
        AddCompletedEntity(EntityKind.Hub, new TilePosition(World.Size.Width - 6, midY + 2), playerTwo);
        // Mirror Blue's solar across the map for PvP fairness (same relative placement).
        var mirroredSolar = new TilePosition(World.Size.Width - 1 - solarOne.X, solarOne.Y);
        var solarTwo = IsValidStartingSolarTile(bastionTwo, mirroredSolar)
            ? mirroredSolar
            : ChooseStartingSolarTile(bastionTwo);
        AddCompletedEntity(EntityKind.SolarPanel, solarTwo, playerTwo);
    }

    private TilePosition ChooseStartingSolarTile(WorldEntity bastion)
    {
        var bastionTiles = GameWorld.GetFootprintTiles(bastion.Kind, bastion.Position).ToHashSet();
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

        var bastionTiles = GameWorld.GetFootprintTiles(bastion.Kind, bastion.Position).ToHashSet();
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

    private static void ConfigureEntityDefaults(WorldEntity entity)
    {
        entity.EnergyBufferCapacity = MvpDefinitions.GetEnergyBufferCapacity(entity.Kind);
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

        var baseline = MvpDefinitions.GetStats(entity.Kind).MaxHealth;
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

    private int ResolveAttackDamage(WorldEntity attacker, EntityStats baseline)
    {
        if (attacker.OwnerId is null)
        {
            return baseline.AttackDamage;
        }

        return ResolveStat(
            attacker.OwnerId.Value,
            ResearchStatIds.AttackDamage,
            baseline.AttackDamage,
            attacker.Kind.ToString(),
            minValue: 0);
    }

    private int ResolveArmor(WorldEntity target, EntityStats baseline)
    {
        if (target.OwnerId is null)
        {
            return baseline.Armor;
        }

        return ResolveStat(
            target.OwnerId.Value,
            ResearchStatIds.Armor,
            baseline.Armor,
            target.Kind.ToString(),
            minValue: 0);
    }

    private int ResolveAttackCooldown(WorldEntity attacker, EntityStats baseline)
    {
        if (attacker.OwnerId is null)
        {
            return baseline.AttackCooldownTicks;
        }

        return ResolveStat(
            attacker.OwnerId.Value,
            ResearchStatIds.AttackCooldownTicks,
            baseline.AttackCooldownTicks,
            attacker.Kind.ToString(),
            minValue: 1);
    }

    private int ComputeDamageAgainst(WorldEntity attacker, EntityStats attackerStats, WorldEntity target)
    {
        SyncResolvedMaxHealth(target);
        var attackDamage = ResolveAttackDamage(attacker, attackerStats);
        var targetStats = MvpDefinitions.GetStats(target.Kind);
        var armor = ResolveArmor(target, targetStats);
        var resistance = CombatDamage.GetResistanceBasisPoints(
            attackerStats.ProjectileKind,
            MvpDefinitions.GetCombatTargetCategory(target.Kind));
        return CombatDamage.ComputeFinalDamage(attackDamage, armor, resistance);
    }

    /// <summary>
    /// True when GroundToGround fire at a ground unit is blocked by an allied Wall/SteelWall on the LoS ray.
    /// Ballistic and AirToGround ignore walls. Buildings/walls as targets are never covered.
    /// Allied means same TeamId (static map-config alliances).
    /// </summary>
    private bool IsGroundToGroundBlockedByAlliedWall(
        WorldEntity attacker,
        WorldEntity target,
        ProjectileKind projectileKind,
        CombatSpatialIndex spatial)
    {
        if (projectileKind != ProjectileKind.GroundToGround)
        {
            return false;
        }

        if (!MvpDefinitions.IsGroundUnitForWallCover(target.Kind) || target.OwnerId is null)
        {
            return false;
        }

        foreach (var tile in EnumerateLineExclusive(attacker.Position, target.Position))
        {
            if (!World.IsInside(tile))
            {
                continue;
            }

            if (spatial.HasAlliedWallAt(tile, target.OwnerId.Value, AreAllied))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bresenham tiles strictly between <paramref name="from"/> and <paramref name="to"/> (endpoints excluded).
    /// </summary>
    internal static IEnumerable<TilePosition> EnumerateLineExclusive(TilePosition from, TilePosition to)
    {
        var x0 = from.X;
        var y0 = from.Y;
        var x1 = to.X;
        var y1 = to.Y;
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }

            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }

            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }

            yield return new TilePosition(x0, y0);
        }
    }

    private void ApplyCombatDamage(WorldEntity target, int damage)
    {
        if (damage <= 0)
        {
            return;
        }

        target.Health = Math.Max(0, target.Health - damage);
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

    private void ProcessCommanderBuildOrders()
    {
        foreach (var commander in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Commander && entity.QueuedBuildOrder is not null).ToList())
        {
            var order = commander.QueuedBuildOrder!;
            if (!CanPlaceBuilding(order.TargetKind, order.TargetPosition))
            {
                commander.QueuedBuildOrder = null;
                continue;
            }

            if (IsWithinBuildRadius(commander, order.TargetKind, order.TargetPosition))
            {
                TryPlaceGhostBuildFromCommander(
                    commander.Id,
                    order.TargetKind,
                    order.TargetPosition,
                    out _,
                    order.Direction,
                    order.SelectedItemRecipe);
                continue;
            }

            MoveMobileEntityTowardTile(commander, order.TargetPosition);
        }
    }

    private void ProcessCommanderDemolishOrders()
    {
        foreach (var commander in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Commander && entity.QueuedDemolishOrder is not null).ToList())
        {
            var order = commander.QueuedDemolishOrder!;
            var target = World.GetEntity(order.TargetEntityId);
            if (!TryResolveDemolishCostKind(commander, target, out var costKind))
            {
                commander.QueuedDemolishOrder = null;
                continue;
            }

            if (IsWithinBuildRadius(commander, costKind, target!.Position))
            {
                TryDemolishBuilding(commander.Id, order.TargetEntityId);
                continue;
            }

            MoveMobileEntityTowardTile(commander, target.Position);
        }
    }

    private void ProcessCommanderMoveCommands()
    {
        foreach (var commander in World.Entities.Where(entity =>
                     entity.IsAlive
                     && entity.Kind == EntityKind.Commander
                     && entity.MoveTarget is not null
                     && entity.QueuedBuildOrder is null
                     && entity.QueuedDemolishOrder is null).ToList())
        {
            if (commander.Position == commander.MoveTarget && commander.CurrentWaypoint is null && commander.MovementPath.Count == 0)
            {
                commander.MoveTarget = null;
                continue;
            }

            if (MoveMobileEntityTowardTile(commander, commander.MoveTarget!.Value))
            {
                commander.MoveTarget = null;
            }
        }
    }

    private bool CanPlaceBuilding(EntityKind targetKind, TilePosition anchor)
    {
        var tiles = GameWorld.GetFootprintTiles(targetKind, anchor).ToList();
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

    private static bool IsWithinBuildRadius(WorldEntity commander, EntityKind targetKind, TilePosition anchor)
    {
        return DistanceSquaredToFootprint(commander.WorldPosition, targetKind, anchor)
            <= Square(MvpDefinitions.CommanderBuildRadius);
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
                    <= Square(MvpDefinitions.CommanderInteractRadius))
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

    private static int DistanceToFootprint(TilePosition from, EntityKind targetKind, TilePosition anchor)
    {
        return GameWorld.GetFootprintTiles(targetKind, anchor)
            .Min(tile => from.ManhattanDistance(tile));
    }

    private static double DistanceSquaredToFootprint(WorldPosition from, EntityKind targetKind, TilePosition anchor)
    {
        var footprint = MvpDefinitions.GetFootprint(targetKind);
        var closestX = Math.Clamp(from.X, anchor.X, anchor.X + footprint.Width);
        var closestY = Math.Clamp(from.Y, anchor.Y, anchor.Y + footprint.Height);
        var dx = from.X - closestX;
        var dy = from.Y - closestY;
        return dx * dx + dy * dy;
    }

    private static double Square(double value) => value * value;

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

    private void CompleteGhostBuilds()
    {
        foreach (var ghost in World.Entities.Where(entity => entity.Kind == EntityKind.GhostBuild && entity.IsAlive).ToList())
        {
            ghost.ConstructionTicksRemaining--;
            if (ghost.ConstructionTicksRemaining > 0 || ghost.BuildTargetKind is null || ghost.OwnerId is null)
            {
                continue;
            }

            ghost.Kind = ghost.BuildTargetKind.Value;
            ghost.BuildTargetKind = null;
            var stats = MvpDefinitions.GetStats(ghost.Kind);
            ghost.MaxHealth = stats.MaxHealth;
            ghost.Health = stats.MaxHealth;
            ConfigureEntityDefaults(ghost);
            SyncResolvedMaxHealth(ghost);
        }
    }

    private void UpdatePower()
    {
        ResetEnergyTickAccumulators();

        foreach (var player in _players)
        {
            player.PowerProduced = 0;
            player.PowerDemand = 0;
        }

        foreach (var entity in World.Entities.Where(entity => entity.IsAlive && entity.OwnerId is not null))
        {
            var player = GetPlayer(entity.OwnerId!.Value);
            var produced = entity.Kind switch
            {
                EntityKind.SolarPanel => MvpDefinitions.PowerProduction.GetValueOrDefault(EntityKind.SolarPanel),
                EntityKind.CoalPlant when entity.InputBuffer.TryRemove(ItemId.Coal, 1) => MvpDefinitions.PowerProduction.GetValueOrDefault(EntityKind.CoalPlant),
                _ => 0
            };
            if (produced > 0)
            {
                player.PowerProduced += produced;
                _tickPowerProduced[player.Id] = _tickPowerProduced.GetValueOrDefault(player.Id) + produced;
                var producedMap = _tickProducedByKind[player.Id];
                producedMap[entity.Kind] = producedMap.GetValueOrDefault(entity.Kind) + produced;
            }

            // HUD demand = installed consumer rating (not actual drain this tick).
            player.PowerDemand += MvpDefinitions.GetPowerDemand(entity.Kind);
        }

        foreach (var player in _players)
        {
            FillEnergyBuffersEmptiestFirst(player);
        }
    }

    private void ResetEnergyTickAccumulators()
    {
        _tickPowerProduced.Clear();
        _tickPowerConsumed.Clear();
        _tickProducedByKind.Clear();
        _tickConsumedByKind.Clear();
        foreach (var player in _players)
        {
            _tickProducedByKind[player.Id] = new Dictionary<EntityKind, int>();
            _tickConsumedByKind[player.Id] = new Dictionary<EntityKind, int>();
            _tickPowerProduced[player.Id] = 0;
            _tickPowerConsumed[player.Id] = 0;
        }
    }

    /// <summary>
    /// Records this tick's production and <b>actual</b> buffer drains into presentation-only energy history.
    /// Must run after all <see cref="TryConsumeBuildingEnergy"/> call sites for the tick.
    /// </summary>
    private void RecordEnergyStatsSample()
    {
        foreach (var player in _players)
        {
            player.EnergyStats.Record(
                Tick,
                _tickPowerProduced.GetValueOrDefault(player.Id),
                _tickPowerConsumed.GetValueOrDefault(player.Id),
                _tickProducedByKind.GetValueOrDefault(player.Id) ?? new Dictionary<EntityKind, int>(),
                _tickConsumedByKind.GetValueOrDefault(player.Id) ?? new Dictionary<EntityKind, int>());
        }
    }

    private void FillEnergyBuffersEmptiestFirst(PlayerState player)
    {
        var remaining = player.PowerProduced;
        if (remaining <= 0)
        {
            return;
        }

        var consumers = World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.OwnerId == player.Id
                && entity.EnergyBufferCapacity > 0)
            .ToList();

        if (consumers.Count == 0)
        {
            return;
        }

        while (remaining > 0)
        {
            var target = consumers
                .Where(entity => entity.EnergyBuffer < entity.EnergyBufferCapacity)
                .OrderBy(entity => entity, EnergyFillRatioComparer.Instance)
                .FirstOrDefault();
            if (target is null)
            {
                break;
            }

            target.EnergyBuffer++;
            remaining--;
        }
    }

    private void ProduceRawResources()
    {
        foreach (var entity in World.Entities.Where(entity => entity.IsAlive).OrderBy(entity => entity.Id))
        {
            var terrain = World.GetTerrain(entity.Position);
            ItemId? product = entity.Kind switch
            {
                EntityKind.Mine when terrain == TerrainType.IronOre => ItemId.IronOre,
                EntityKind.Mine when terrain == TerrainType.CopperOre => ItemId.CopperOre,
                EntityKind.CoalMine when terrain == TerrainType.Coal => ItemId.Coal,
                EntityKind.OilWell when terrain == TerrainType.Oil => ItemId.CrudeOil,
                _ => null
            };

            if (product is null)
            {
                continue;
            }

            // Full output = idle (clear / don't advance ticks).
            if (entity.OutputBuffer.Count(product.Value) >= MvpDefinitions.GetMaxStackSize(product.Value))
            {
                entity.WorkTicksRemaining = 0;
                entity.WorkTicksTotal = 0;
                continue;
            }

            if (entity.WorkTicksRemaining <= 0)
            {
                var cycleTicks = entity.Kind switch
                {
                    EntityKind.Mine => MvpDefinitions.OreMineWorkTicks,
                    EntityKind.CoalMine => MvpDefinitions.CoalMineWorkTicks,
                    _ => MvpDefinitions.MineWorkTicks
                };
                entity.WorkTicksTotal = cycleTicks;
                entity.WorkTicksRemaining = cycleTicks;
            }

            entity.WorkTicksRemaining--;
            if (entity.WorkTicksRemaining > 0)
            {
                continue;
            }

            if (!TryConsumeBuildingEnergy(entity))
            {
                // Stay at zero until energy is available; next tick restarts the cycle.
                entity.WorkTicksTotal = 0;
                continue;
            }

            TryAddToBuffer(entity.OutputBuffer, product.Value, 1);
            entity.WorkTicksTotal = 0;
        }
    }

    private void ProcessBuildingWork()
    {
        foreach (var building in World.Entities.Where(entity => entity.IsAlive))
        {
            if (building.WorkTicksRemaining > 0 && building.PendingOutputItem is not null && !MvpDefinitions.FactoryKinds.Contains(building.Kind))
            {
                if (!TryConsumeBuildingEnergy(building))
                {
                    continue;
                }

                building.WorkTicksRemaining--;
                if (building.WorkTicksRemaining == 0)
                {
                    TryCompletePendingOutput(building);
                    if (building.WorkTicksRemaining == 0 && building.PendingOutputItem is null)
                    {
                        building.WorkTicksTotal = 0;
                    }
                }

                continue;
            }

            if (building.WorkTicksRemaining == 0 && building.PendingOutputItem is not null && !MvpDefinitions.FactoryKinds.Contains(building.Kind))
            {
                TryCompletePendingOutput(building);
                if (building.PendingOutputItem is null)
                {
                    building.WorkTicksTotal = 0;
                }

                continue;
            }

            if (building.WorkTicksRemaining > 0)
            {
                continue;
            }

            switch (building.Kind)
            {
                case EntityKind.Smelter:
                    TryStartSmelterRecipe(building);
                    break;
                case EntityKind.Refinery:
                    StartItemRecipe(building, ItemId.CrudeOil, ItemId.Fuel, 30);
                    break;
                case EntityKind.Assembler:
                    StartAssemblerRecipe(building);
                    break;
            }
        }
    }

    private void TryStartSmelterRecipe(WorldEntity smelter)
    {
        if (smelter.PendingOutputItem is not null)
        {
            return;
        }

        if (smelter.ActiveSmeltRecipe is { } sticky && TryBeginSmeltRecipe(smelter, sticky))
        {
            return;
        }

        foreach (var candidate in new[] { SmeltRecipeId.IronPlate, SmeltRecipeId.CopperPlate, SmeltRecipeId.Steel })
        {
            if (smelter.ActiveSmeltRecipe == candidate)
            {
                continue;
            }

            if (TryBeginSmeltRecipe(smelter, candidate))
            {
                return;
            }
        }
    }

    private bool TryBeginSmeltRecipe(WorldEntity smelter, SmeltRecipeId recipe)
    {
        switch (recipe)
        {
            case SmeltRecipeId.IronPlate:
                if (!smelter.InputBuffer.Has(ItemId.IronOre, 1))
                {
                    return false;
                }

                smelter.ActiveSmeltRecipe = SmeltRecipeId.IronPlate;
                StartItemRecipe(smelter, ItemId.IronOre, ItemId.IronPlate, 40);
                return smelter.PendingOutputItem is not null;

            case SmeltRecipeId.CopperPlate:
                if (!smelter.InputBuffer.Has(ItemId.CopperOre, 1))
                {
                    return false;
                }

                smelter.ActiveSmeltRecipe = SmeltRecipeId.CopperPlate;
                StartItemRecipe(smelter, ItemId.CopperOre, ItemId.CopperPlate, 40);
                return smelter.PendingOutputItem is not null;

            case SmeltRecipeId.Steel:
                if (!smelter.InputBuffer.Has(ItemId.IronPlate, 2) || !smelter.InputBuffer.Has(ItemId.Coal, 1))
                {
                    return false;
                }

                smelter.InputBuffer.TryRemove(ItemId.Coal, 1);
                smelter.InputBuffer.TryRemove(ItemId.IronPlate, 2);
                smelter.ActiveSmeltRecipe = SmeltRecipeId.Steel;
                smelter.PendingOutputItem = ItemId.Steel;
                smelter.PendingOutputAmount = 1;
                var steelTicks = 60;
                if (smelter.OwnerId is not null)
                {
                    steelTicks = ResolveStat(smelter.OwnerId.Value, ResearchStatIds.SmelterWorkTicks, steelTicks);
                }

                smelter.WorkTicksTotal = steelTicks;
                smelter.WorkTicksRemaining = steelTicks;
                return true;

            default:
                return false;
        }
    }

    private void StartItemRecipe(WorldEntity building, ItemId input, ItemId output, int workTicks)
    {
        if (building.PendingOutputItem is not null || !building.InputBuffer.TryRemove(input, 1))
        {
            return;
        }

        if (building.OwnerId is not null)
        {
            var statId = building.Kind == EntityKind.Smelter || building.Kind == EntityKind.Refinery
                ? ResearchStatIds.SmelterWorkTicks
                : ResearchStatIds.FactoryWorkTicks;
            workTicks = ResolveStat(building.OwnerId.Value, statId, workTicks);
        }

        building.PendingOutputItem = output;
        building.PendingOutputAmount = 1;
        building.WorkTicksTotal = workTicks;
        building.WorkTicksRemaining = workTicks;
    }

    private void StartAssemblerRecipe(WorldEntity assembler)
    {
        if (assembler.SelectedItemRecipe is null || !MvpDefinitions.ItemRecipes.TryGetValue(assembler.SelectedItemRecipe.Value, out var recipe))
        {
            return;
        }

        if (assembler.OwnerId is not null
            && !CapabilityResolver.IsItemRecipeUnlocked(GetPlayer(assembler.OwnerId.Value).Research, recipe.Id))
        {
            return;
        }

        if (assembler.PendingOutputItem is not null || !assembler.InputBuffer.TryRemoveAll(recipe.Inputs))
        {
            return;
        }

        var workTicks = recipe.WorkTicks;
        if (assembler.OwnerId is not null)
        {
            workTicks = ResolveStat(assembler.OwnerId.Value, ResearchStatIds.FactoryWorkTicks, workTicks);
        }

        assembler.PendingOutputItem = recipe.OutputItem;
        assembler.PendingOutputAmount = recipe.OutputAmount;
        assembler.WorkTicksTotal = workTicks;
        assembler.WorkTicksRemaining = workTicks;
    }

    private static bool TryCompletePendingOutput(WorldEntity building)
    {
        if (building.PendingOutputItem is null || !TryAddToBuffer(building.OutputBuffer, building.PendingOutputItem.Value, building.PendingOutputAmount))
        {
            return false;
        }

        building.PendingOutputItem = null;
        building.PendingOutputAmount = 1;
        return true;
    }

    private void ProcessLogistics()
    {
        ProcessInserters();
        ProcessConveyors();
    }

    private void ProcessInserters()
    {
        var inserters = World.Entities
            .Where(entity => entity.IsAlive && entity.Kind == EntityKind.Inserter)
            .OrderBy(entity => entity.Id)
            .ToList();

        // Empty-handed extract: at most one pull per source entity per tick (fair share by tick + source id).
        var extractGroups = inserters
            .Where(inserter => inserter.HeldItem is null)
            .Select(inserter =>
            {
                var source = World.GetTopEntityAt(inserter.Position.Offset(Opposite(inserter.Direction)));
                return (Inserter: inserter, Source: source);
            })
            .Where(pair => pair.Source is not null)
            .GroupBy(pair => pair.Source!.Id)
            .OrderBy(group => group.Key);

        foreach (var group in extractGroups)
        {
            var candidates = group.OrderBy(pair => pair.Inserter.Id).ToList();
            var start = (int)((Tick + group.Key) % candidates.Count);
            for (var offset = 0; offset < candidates.Count; offset++)
            {
                var index = (start + offset) % candidates.Count;
                var (inserter, source) = candidates[index];
                if (TryExtractItem(source!, inserter.FilterItem, out var item))
                {
                    inserter.HeldItem = item;
                    inserter.HeldTransferTicksRemaining = MvpDefinitions.InserterTransferTicks;
                    break;
                }
            }
        }

        foreach (var inserter in inserters.Where(entity => entity.HeldItem is not null))
        {
            if (inserter.HeldTransferTicksRemaining > 0)
            {
                inserter.HeldTransferTicksRemaining--;
                continue;
            }

            var target = World.GetTopEntityAt(inserter.Position.Offset(inserter.Direction));
            if (target is not null && TryInsertItem(target, inserter.HeldItem!.Value))
            {
                inserter.HeldItem = null;
            }
        }
    }

    private void ProcessConveyors()
    {
        foreach (var conveyor in World.Entities.Where(entity => entity.IsAlive && (entity.Kind == EntityKind.Conveyor || entity.Kind == EntityKind.UndergroundConveyor)).OrderBy(entity => entity.Id).ToList())
        {
            foreach (var conveyorItem in conveyor.ConveyorItems.ToList())
            {
                conveyorItem.ProgressTicks++;
                var moveTicks = MvpDefinitions.ConveyorMoveTicks;
                if (conveyor.OwnerId is not null)
                {
                    moveTicks = ResolveStat(conveyor.OwnerId.Value, ResearchStatIds.ConveyorMoveTicks, moveTicks);
                }

                if (conveyorItem.ProgressTicks < moveTicks)
                {
                    continue;
                }

                var target = World.GetTopEntityAt(conveyor.Position.Offset(conveyor.Direction));
                if (target is not null && TryInsertItem(target, conveyorItem.Item))
                {
                    conveyor.ConveyorItemsMutable.Remove(conveyorItem);
                }
            }
        }
    }

    private static Direction Opposite(Direction direction)
    {
        return direction switch
        {
            Direction.North => Direction.South,
            Direction.East => Direction.West,
            Direction.South => Direction.North,
            Direction.West => Direction.East,
            _ => direction
        };
    }

    private static bool TryExtractItem(WorldEntity source, ItemId? filter, out ItemId item)
    {
        if (source.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor)
        {
            var conveyorItem = source.ConveyorItems
                .OrderBy(slot => slot.Item)
                .FirstOrDefault(slot => filter is null || slot.Item == filter.Value);
            if (conveyorItem is not null)
            {
                item = conveyorItem.Item;
                source.ConveyorItemsMutable.Remove(conveyorItem);
                return true;
            }
        }

        if (source.Kind == EntityKind.Hub && source.Inventory.TryTakeFirst(filter is null ? null : candidate => candidate == filter.Value, out item))
        {
            return true;
        }

        if (IsBuildingWithBuffers(source.Kind) && source.OutputBuffer.TryTakeFirst(filter is null ? null : candidate => candidate == filter.Value, out item))
        {
            return true;
        }

        return source.Inventory.TryTakeFirst(filter is null ? null : candidate => candidate == filter.Value, out item);
    }

    private bool TryInsertItem(WorldEntity target, ItemId item)
    {
        if (target.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor)
        {
            return TryAddToConveyor(target, item, progressTicks: 0);
        }

        if (target.Kind == EntityKind.Hub)
        {
            return target.Inventory.TryAddWithinTotalStackLimit(item, 1, GetHubStorageStacks(target.OwnerId));
        }

        return IsBuildingWithBuffers(target.Kind)
            ? TryAddToBuffer(target.InputBuffer, item, 1)
            : target.Inventory.TryAddWithinStackLimit(item, 1);
    }

    private static bool TryAddToBuffer(Inventory buffer, ItemId item, int amount)
    {
        return buffer.TryAddWithinStackLimit(item, amount);
    }

    private static bool TryAddToConveyor(WorldEntity conveyor, ItemId item, int progressTicks)
    {
        if (conveyor.ConveyorItems.Count >= MvpDefinitions.ConveyorMaxItemsPerTile)
        {
            return false;
        }

        conveyor.ConveyorItemsMutable.Add(new ConveyorItem(item, progressTicks));
        return true;
    }

    private static bool IsBuildingWithBuffers(EntityKind kind)
    {
        return kind is EntityKind.Mine
            or EntityKind.CoalMine
            or EntityKind.OilWell
            or EntityKind.Smelter
            or EntityKind.Refinery
            or EntityKind.Assembler
            or EntityKind.SolarPanel
            or EntityKind.CoalPlant
            or EntityKind.Hub
            or EntityKind.TankFactory
            or EntityKind.DroneCenter
            or EntityKind.Laboratory
            or EntityKind.MachineGunTurret
            or EntityKind.CannonTurret
            or EntityKind.AntiAirTurret;
    }

    private void ProcessResearch()
    {
        _researchSystem.ProcessResearch(this, Tick);
        // MaxHealth modifiers only land on completion; sync caps/HP for living entities.
        SyncAllResolvedMaxHealth();
    }

    private int GetHubStorageStacks(PlayerId? ownerId)
    {
        if (ownerId is null)
        {
            return MvpDefinitions.HubStorageStacks;
        }

        return ResolveStat(ownerId.Value, ResearchStatIds.HubStorageStacks, MvpDefinitions.HubStorageStacks, minValue: 1);
    }

    private void ProcessRepairOutOfCombat()
    {
        if (Tick % 30 != 0)
        {
            return;
        }

        foreach (var player in _players.OrderBy(player => player.Id.Value))
        {
            if (!CapabilityResolver.HasCapability(player.Research, ResearchCapabilityIds.RepairOutOfCombat))
            {
                continue;
            }

            foreach (var bastion in World.Entities
                .Where(entity => entity.IsAlive && entity.OwnerId == player.Id && entity.Kind == EntityKind.Bastion)
                .OrderBy(entity => entity.Id))
            {
                foreach (var unit in World.Entities
                    .Where(entity =>
                        entity.IsAlive
                        && entity.OwnerId == player.Id
                        && MvpDefinitions.UnitKinds.Contains(entity.Kind)
                        && entity.Health < entity.MaxHealth
                        && entity.AttackCooldownRemaining <= 0
                        && entity.Position.ManhattanDistance(bastion.Position) <= 4)
                    .OrderBy(entity => entity.Id))
                {
                    unit.Health = Math.Min(unit.MaxHealth, unit.Health + 1);
                }
            }
        }
    }

    private void ProcessFactoryProduction()
    {
        foreach (var factory in World.Entities.Where(entity => entity.IsAlive && MvpDefinitions.FactoryKinds.Contains(entity.Kind)).OrderBy(entity => entity.Id))
        {
            if (factory.WorkTicksRemaining > 0 && factory.ProductionTargetKind is not null)
            {
                if (!TryConsumeBuildingEnergy(factory))
                {
                    continue;
                }

                factory.WorkTicksRemaining--;
                if (factory.WorkTicksRemaining == 0)
                {
                    if (!TrySpawnProducedUnit(factory, factory.ProductionTargetKind.Value))
                    {
                        // Keep craft in-flight until a free collision tile appears; do not drop the unit.
                        factory.WorkTicksRemaining = 1;
                        continue;
                    }

                    factory.WorkTicksTotal = 0;
                    if (!factory.IsManualProductionTarget)
                    {
                        factory.ProductionTargetKind = null;
                    }
                }

                continue;
            }

            // Autofill re-resolves every idle tick across all owned bastion deficits.
            if (!factory.IsManualProductionTarget)
            {
                factory.ProductionTargetKind = ChooseBastionDeficit(factory);
            }

            if (factory.ProductionTargetKind is null || !MvpDefinitions.ProductionRecipes.TryGetValue(factory.ProductionTargetKind.Value, out var recipe))
            {
                continue;
            }

            if (!IsRecipeUnlockedForOwner(factory.OwnerId, recipe))
            {
                continue;
            }

            // Manual and autofill both wait when template demand or army capacity is full.
            if (factory.OwnerId is null || !CanStartUnitProduction(factory.OwnerId.Value, recipe.OutputKind))
            {
                continue;
            }

            if (!CanFactoryProduce(factory.Kind, recipe.OutputKind) || !factory.InputBuffer.TryRemoveAll(recipe.Inputs))
            {
                continue;
            }

            var workTicks = recipe.WorkTicks;
            if (factory.OwnerId is not null)
            {
                workTicks = ResolveStat(factory.OwnerId.Value, ResearchStatIds.FactoryWorkTicks, workTicks, recipe.OutputKind.ToString());
            }

            factory.WorkTicksTotal = workTicks;
            factory.WorkTicksRemaining = workTicks;
        }
    }

    private EntityKind? ChooseBastionDeficit(WorldEntity factory)
    {
        if (factory.OwnerId is null)
        {
            return null;
        }

        foreach (var bastion in World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.OwnerId == factory.OwnerId
                && entity.Kind == EntityKind.Bastion)
            .OrderBy(entity => entity.Id))
        {
            foreach (var desired in bastion.BastionTemplate.OrderBy(pair => pair.Key))
            {
                if (desired.Value <= 0 || !CanFactoryProduce(factory.Kind, desired.Key))
                {
                    continue;
                }

                if (!MvpDefinitions.ProductionRecipes.TryGetValue(desired.Key, out var recipe)
                    || !IsRecipeUnlockedForOwner(factory.OwnerId, recipe))
                {
                    continue;
                }

                var current = CountBastionUnitSupply(bastion.Id, desired.Key);
                if (current < desired.Value)
                {
                    return desired.Key;
                }
            }
        }

        return null;
    }

    private int CountBastionUnitSupply(int bastionId, EntityKind unitKind)
    {
        var living = World.Entities.Count(entity =>
            entity.IsAlive
            && entity.AssignedBastionId == bastionId
            && entity.Kind == unitKind);

        var bastion = World.GetEntity(bastionId);
        if (bastion?.OwnerId is null)
        {
            return living;
        }

        return living + CountInFlightAttributedToBastion(bastion.OwnerId.Value, bastionId, unitKind);
    }

    /// <summary>
    /// True when player-wide living+in-flight supply of <paramref name="unitKind"/> is below
    /// summed bastion templates for that kind, and total army supply is below template capacity.
    /// </summary>
    private bool CanStartUnitProduction(PlayerId ownerId, EntityKind unitKind)
    {
        var kindDemand = World.Entities
            .Where(entity => entity.IsAlive && entity.OwnerId == ownerId && entity.Kind == EntityKind.Bastion)
            .Sum(bastion => bastion.BastionTemplate.GetValueOrDefault(unitKind));
        if (kindDemand <= 0)
        {
            return false;
        }

        if (CountPlayerUnitKindSupply(ownerId, unitKind) >= kindDemand)
        {
            return false;
        }

        return CountPlayerArmySupply(ownerId) < GetBastionTemplateCapacity(ownerId);
    }

    private int CountPlayerUnitKindSupply(PlayerId ownerId, EntityKind unitKind)
    {
        var living = World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == ownerId
            && entity.Kind == unitKind);
        return living + CountInFlightOfKind(ownerId, unitKind);
    }

    private int CountPlayerArmySupply(PlayerId ownerId)
    {
        var living = World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == ownerId
            && MvpDefinitions.UnitKinds.Contains(entity.Kind));
        var inFlight = World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == ownerId
            && MvpDefinitions.FactoryKinds.Contains(entity.Kind)
            && entity.ProductionTargetKind is not null
            && entity.WorkTicksRemaining > 0
            && MvpDefinitions.UnitKinds.Contains(entity.ProductionTargetKind.Value));
        return living + inFlight;
    }

    private int CountInFlightOfKind(PlayerId ownerId, EntityKind unitKind) =>
        World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == ownerId
            && MvpDefinitions.FactoryKinds.Contains(entity.Kind)
            && entity.ProductionTargetKind == unitKind
            && entity.WorkTicksRemaining > 0);

    /// <summary>
    /// Attributes in-flight factory crafts of <paramref name="unitKind"/> to bastions greedily:
    /// lowest factory Id fills the lowest bastion Id that still has living-based deficit.
    /// </summary>
    private int CountInFlightAttributedToBastion(PlayerId ownerId, int bastionId, EntityKind unitKind)
    {
        var bastions = World.Entities
            .Where(entity => entity.IsAlive && entity.OwnerId == ownerId && entity.Kind == EntityKind.Bastion)
            .OrderBy(entity => entity.Id)
            .ToList();

        var remainingDeficit = new Dictionary<int, int>();
        foreach (var bastion in bastions)
        {
            var desired = bastion.BastionTemplate.GetValueOrDefault(unitKind);
            var living = World.Entities.Count(entity =>
                entity.IsAlive
                && entity.AssignedBastionId == bastion.Id
                && entity.Kind == unitKind);
            remainingDeficit[bastion.Id] = Math.Max(0, desired - living);
        }

        var attributed = 0;
        foreach (var factory in World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.OwnerId == ownerId
                && MvpDefinitions.FactoryKinds.Contains(entity.Kind)
                && entity.ProductionTargetKind == unitKind
                && entity.WorkTicksRemaining > 0)
            .OrderBy(entity => entity.Id))
        {
            _ = factory;
            var assignee = remainingDeficit
                .Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key)
                .Select(pair => (int?)pair.Key)
                .FirstOrDefault();
            if (assignee is null)
            {
                continue;
            }

            remainingDeficit[assignee.Value]--;
            if (assignee.Value == bastionId)
            {
                attributed++;
            }
        }

        return attributed;
    }

    private bool IsRecipeUnlockedForOwner(PlayerId? ownerId, ProductionRecipe recipe)
    {
        if (ownerId is null || recipe.RequiredTechnology is null)
        {
            return true;
        }

        var owner = GetPlayer(ownerId.Value);
        return owner.ResearchedTechnologies.Contains(recipe.RequiredTechnology.Value)
            || CapabilityResolver.IsRecipeUnlocked(owner.Research, recipe.OutputKind.ToString())
            || owner.Research.UnlockedEntityKinds.Contains(recipe.OutputKind.ToString());
    }

    private static bool CanFactoryProduce(EntityKind factoryKind, EntityKind unitKind)
    {
        return unitKind == EntityKind.Scout ? factoryKind == EntityKind.DroneCenter : factoryKind == EntityKind.TankFactory;
    }

    private bool TrySpawnProducedUnit(WorldEntity factory, EntityKind unitKind)
    {
        if (factory.OwnerId is null)
        {
            return false;
        }

        if (!TryFindSpawnTileNear(factory, unitKind, out var spawnTile))
        {
            return false;
        }

        var unit = CreateEntity(unitKind, spawnTile, factory.OwnerId);
        var bastionId = ChooseSpawnBastionId(factory.OwnerId.Value, unitKind);
        unit.AssignedBastionId = bastionId;
        if (bastionId is not null)
        {
            var bastion = World.GetEntity(bastionId.Value);
            if (bastion is not null)
            {
                ApplyBastionOrderToUnit(unit, bastion.Order);
            }
        }

        World.AddEntity(unit);
        return true;
    }

    /// <summary>
    /// Lowest owned bastion Id that still needs <paramref name="unitKind"/> (living count vs template).
    /// Returns null when no bastion has a deficit (production should have waited at the factory).
    /// </summary>
    private int? ChooseSpawnBastionId(PlayerId ownerId, EntityKind unitKind)
    {
        foreach (var bastion in World.Entities
            .Where(entity => entity.IsAlive && entity.OwnerId == ownerId && entity.Kind == EntityKind.Bastion)
            .OrderBy(entity => entity.Id))
        {
            var desired = bastion.BastionTemplate.GetValueOrDefault(unitKind);
            if (desired <= 0)
            {
                continue;
            }

            var living = World.Entities.Count(entity =>
                entity.IsAlive
                && entity.AssignedBastionId == bastion.Id
                && entity.Kind == unitKind);
            if (living < desired)
            {
                return bastion.Id;
            }
        }

        return null;
    }

    private bool TryFindSpawnTileNear(WorldEntity factory, EntityKind unitKind, out TilePosition spawnTile)
    {
        var footprint = MvpDefinitions.GetFootprint(factory.Kind);
        for (var ring = 1; ring <= 6; ring++)
        {
            var candidates = new List<TilePosition>();
            var minX = factory.Position.X - ring;
            var maxX = factory.Position.X + footprint.Width - 1 + ring;
            var minY = factory.Position.Y - ring;
            var maxY = factory.Position.Y + footprint.Height - 1 + ring;
            for (var x = minX; x <= maxX; x++)
            {
                candidates.Add(new TilePosition(x, minY));
                candidates.Add(new TilePosition(x, maxY));
            }

            for (var y = minY + 1; y <= maxY - 1; y++)
            {
                candidates.Add(new TilePosition(minX, y));
                candidates.Add(new TilePosition(maxX, y));
            }

            foreach (var tile in candidates
                         .Where(tile => World.IsInside(tile))
                         .OrderBy(tile => tile.ManhattanDistance(factory.Position))
                         .ThenBy(tile => tile.X)
                         .ThenBy(tile => tile.Y))
            {
                var probe = new WorldEntity(-1, unitKind, tile, factory.OwnerId);
                if (IsGroundPassable(probe, tile) && CanOccupyWorldPosition(probe, probe.WorldPosition))
                {
                    spawnTile = tile;
                    return true;
                }
            }
        }

        spawnTile = default;
        return false;
    }

    private void ProcessBastions()
    {
        foreach (var bastion in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Bastion).OrderBy(entity => entity.Id))
        {
            TryCompleteBastionOrder(bastion);

            var units = World.Entities
                .Where(entity => entity.IsAlive && entity.AssignedBastionId == bastion.Id && MvpDefinitions.UnitKinds.Contains(entity.Kind))
                .OrderBy(entity => entity.Id)
                .ToList();

            // Home units keep Defend while bastion is Scout/AttackArea; garrison them,
            // but sortie only during active bastion Defend.
            var allowSortie = bastion.Order.Kind == BastionOrderKind.Defend;
            var threat = allowSortie
                ? FindNearestEnemyInRange(bastion, GetBastionVisionRadius(bastion))
                : null;

            foreach (var unit in units)
            {
                if (unit.Order.Kind != BastionOrderKind.Defend)
                {
                    continue;
                }

                if (threat is not null)
                {
                    unit.IsGarrisoned = false;
                    continue;
                }

                // Bastion is multi-tile; units stop on the footprint perimeter (building collision)
                // and must hide when adjacent to any footprint tile — not only near bastion.Position.
                if (IsWithinBastionGarrisonRange(bastion, unit.Position))
                {
                    World.RelocateEntity(unit, bastion.Position);
                    unit.WorldPosition = WorldPosition.FromTileCenter(bastion.Position);
                    ResetMovementPath(unit);
                    unit.IsGarrisoned = true;
                }
            }
        }
    }

    /// <summary>
    /// True when <paramref name="unitPosition"/> is on or Chebyshev-adjacent to the bastion footprint
    /// (units cannot occupy building tiles, so they garrison from the perimeter ring).
    /// </summary>
    private static bool IsWithinBastionGarrisonRange(WorldEntity bastion, TilePosition unitPosition)
    {
        var footprint = MvpDefinitions.GetFootprint(bastion.Kind);
        var minX = bastion.Position.X - 1;
        var maxX = bastion.Position.X + footprint.Width;
        var minY = bastion.Position.Y - 1;
        var maxY = bastion.Position.Y + footprint.Height;
        return unitPosition.X >= minX
            && unitPosition.X <= maxX
            && unitPosition.Y >= minY
            && unitPosition.Y <= maxY;
    }

    private void TryCompleteBastionOrder(WorldEntity bastion)
    {
        if (bastion.Order.Kind == BastionOrderKind.AttackArea && bastion.Order.Target is not null)
        {
            // Scouts join AttackArea for FoW; completion waits for every assigned unit still on
            // AttackArea (combat and scouts), so scout-only pushes and post-wipe leftovers can finish.
            var attackUnits = World.Entities
                .Where(entity =>
                    entity.IsAlive
                    && entity.AssignedBastionId == bastion.Id
                    && MvpDefinitions.UnitKinds.Contains(entity.Kind)
                    && entity.Order.Kind == BastionOrderKind.AttackArea)
                .ToList();
            if (attackUnits.Count > 0
                && attackUnits.All(unit => unit.Position.IsWithinEuclideanRange(bastion.Order.Target.Value, 1)))
            {
                SwitchBastionToDefend(bastion);
            }

            return;
        }

        if (bastion.Order.Kind == BastionOrderKind.Scout && bastion.Order.Target is not null)
        {
            var scouts = World.Entities
                .Where(entity => entity.IsAlive && entity.AssignedBastionId == bastion.Id && entity.Kind == EntityKind.Scout)
                .ToList();
            if (scouts.Count > 0
                && scouts.All(unit => unit.Position.IsWithinEuclideanRange(bastion.Order.Target.Value, 1)))
            {
                SwitchBastionToDefend(bastion);
            }
        }
    }

    private void SwitchBastionToDefend(WorldEntity bastion)
    {
        var defend = new BastionOrder(BastionOrderKind.Defend);
        bastion.Order = defend;
        foreach (var unit in World.Entities.Where(entity =>
                     entity.IsAlive
                     && entity.AssignedBastionId == bastion.Id
                     && MvpDefinitions.UnitKinds.Contains(entity.Kind)))
        {
            unit.Order = defend;
            ResetMovementPath(unit);
        }
    }

    private int GetBastionVisionRadius(WorldEntity bastion)
    {
        var radius = MvpDefinitions.GetStats(EntityKind.Bastion).VisionRadius;
        if (bastion.OwnerId is not null)
        {
            radius = ResolveStat(bastion.OwnerId.Value, ResearchStatIds.VisionRadius, radius, minValue: 0);
        }

        return radius;
    }

    private WorldEntity? FindNearestEnemyInRange(WorldEntity origin, int radius)
    {
        if (origin.OwnerId is null)
        {
            return null;
        }

        return World.Entities
            .Where(entity =>
                entity.IsAlive
                && !entity.IsGarrisoned
                && entity.OwnerId is not null
                && !AreAllied(origin.OwnerId, entity.OwnerId)
                && origin.Position.IsWithinEuclideanRange(entity.Position, radius))
            .OrderBy(entity => origin.Position.EuclideanDistanceSquared(entity.Position))
            .ThenBy(entity => entity.Id)
            .FirstOrDefault();
    }

    private void CascadeBastionDeaths()
    {
        foreach (var bastion in World.Entities.Where(entity => entity.Kind == EntityKind.Bastion && !entity.IsAlive).ToList())
        {
            foreach (var unit in World.Entities
                         .Where(entity =>
                             entity.AssignedBastionId == bastion.Id
                             && entity.IsAlive
                             && MvpDefinitions.UnitKinds.Contains(entity.Kind))
                         .ToList())
            {
                unit.Health = 0;
            }
        }
    }

    private void ProcessMovement()
    {
        foreach (var unit in World.Entities.Where(entity => entity.IsAlive && MvpDefinitions.UnitKinds.Contains(entity.Kind)).OrderBy(entity => entity.Id))
        {
            var target = GetMovementTarget(unit);
            if (target is null || unit.Position == target.Value)
            {
                continue;
            }

            unit.IsGarrisoned = false;
            MoveMobileEntityTowardTile(unit, target.Value);
        }
    }

    private TilePosition? GetMovementTarget(WorldEntity unit)
    {
        if (unit.IsGarrisoned)
        {
            return null;
        }

        if ((unit.Order.Kind is BastionOrderKind.AttackArea or BastionOrderKind.Scout) && unit.Order.Target is not null)
        {
            return unit.Order.Target;
        }

        if (unit.Order.Kind == BastionOrderKind.Patrol && unit.Order.WaypointList.Count >= 2)
        {
            var waypoints = unit.Order.WaypointList;
            var index = ((unit.Order.WaypointIndex % waypoints.Count) + waypoints.Count) % waypoints.Count;
            var waypoint = waypoints[index];
            if (unit.Position.IsWithinEuclideanRange(waypoint, 0))
            {
                var nextIndex = (index + 1) % waypoints.Count;
                unit.Order = unit.Order with { WaypointIndex = nextIndex };
                return waypoints[nextIndex];
            }

            return waypoint;
        }

        if (unit.Order.Kind == BastionOrderKind.Defend && unit.AssignedBastionId is not null)
        {
            var bastion = World.GetEntity(unit.AssignedBastionId.Value);
            if (bastion is null)
            {
                return null;
            }

            // Sortie only for active bastion Defend; Scout/AttackArea home units stay put.
            if (bastion.Order.Kind == BastionOrderKind.Defend)
            {
                var visionRadius = GetBastionVisionRadius(bastion);
                var threat = FindNearestEnemyInRange(bastion, visionRadius);
                if (threat is not null)
                {
                    return threat.Position;
                }
            }

            return bastion.Position;
        }

        if (unit.AssignedBastionId is not null)
        {
            return World.GetEntity(unit.AssignedBastionId.Value)?.Position;
        }

        return null;
    }

    private bool MoveMobileEntityTowardTile(WorldEntity entity, TilePosition target)
    {
        if (!World.IsInside(target))
        {
            ResetMovementPath(entity);
            return false;
        }

        if (entity.Position == target && entity.CurrentWaypoint is null && entity.MovementPath.Count == 0)
        {
            entity.WorldPosition = WorldPosition.FromTileCenter(target);
            return true;
        }

        if (entity.CurrentWaypoint is null)
        {
            if (entity.MovementPath.Count == 0)
            {
                var path = FindGroundPath(entity, entity.Position, target);
                if (path.Count == 0)
                {
                    return !IsGroundPassable(entity, target);
                }

                entity.MovementPathMutable.AddRange(path);
            }

            entity.CurrentWaypoint = entity.MovementPath[0];
            entity.MovementPathMutable.RemoveAt(0);
        }

        var waypointPosition = WorldPosition.FromTileCenter(entity.CurrentWaypoint.Value);
        var distance = entity.WorldPosition.DistanceTo(waypointPosition);
        if (distance <= MvpDefinitions.MobileMoveWorldUnitsPerTick)
        {
            if (!CanOccupyWorldPosition(entity, waypointPosition))
            {
                ResetMovementPath(entity);
                return false;
            }

            entity.WorldPosition = waypointPosition;
            World.RelocateEntity(entity, entity.CurrentWaypoint.Value);
            entity.CurrentWaypoint = null;
            return entity.MovementPath.Count == 0 && (entity.Position == target || !IsGroundPassable(entity, target));
        }

        var dx = waypointPosition.X - entity.WorldPosition.X;
        var dy = waypointPosition.Y - entity.WorldPosition.Y;
        var nextPosition = new WorldPosition(
            entity.WorldPosition.X + dx / distance * MvpDefinitions.MobileMoveWorldUnitsPerTick,
            entity.WorldPosition.Y + dy / distance * MvpDefinitions.MobileMoveWorldUnitsPerTick);
        if (!CanOccupyWorldPosition(entity, nextPosition))
        {
            ResetMovementPath(entity);
            return false;
        }

        entity.WorldPosition = nextPosition;
        World.RelocateEntity(entity, nextPosition.ToTilePosition());
        return false;
    }

    private List<TilePosition> FindGroundPath(WorldEntity entity, TilePosition start, TilePosition target)
    {
        foreach (var pathTarget in GetCandidatePathTargets(entity, start, target))
        {
            var path = FindGroundPathToTarget(entity, start, pathTarget);
            if (path.Count > 0 || start == pathTarget)
            {
                return path;
            }
        }

        return [];
    }

    private List<TilePosition> FindGroundPathToTarget(WorldEntity entity, TilePosition start, TilePosition target)
    {
        if (start == target)
        {
            return [];
        }

        var open = new PriorityQueue<TilePosition, (int F, int H, int Y, int X)>();
        var previous = new Dictionary<TilePosition, TilePosition?>();
        var costSoFar = new Dictionary<TilePosition, int>();
        previous[start] = null;
        costSoFar[start] = 0;
        open.Enqueue(start, (OctileDistance(start, target), OctileDistance(start, target), start.Y, start.X));

        while (open.TryDequeue(out var current, out _))
        {
            if (current == target)
            {
                return ReconstructPath(previous, target);
            }

            foreach (var next in GetNeighborTiles(current))
            {
                if (!CanEnterNeighbor(entity, current, next, start))
                {
                    continue;
                }

                var newCost = costSoFar[current] + GetStepCost(current, next);
                if (costSoFar.TryGetValue(next, out var existingCost) && newCost >= existingCost)
                {
                    continue;
                }

                costSoFar[next] = newCost;
                previous[next] = current;
                var heuristic = OctileDistance(next, target);
                open.Enqueue(next, (newCost + heuristic, heuristic, next.Y, next.X));
            }
        }

        return [];
    }

    private IEnumerable<TilePosition> GetCandidatePathTargets(WorldEntity entity, TilePosition start, TilePosition target)
    {
        if (World.IsInside(target) && IsGroundPassable(entity, target))
        {
            yield return target;
            yield break;
        }

        var maxRadius = Math.Max(World.Size.Width, World.Size.Height);
        for (var radius = 1; radius <= maxRadius; radius++)
        {
            var candidates = new List<TilePosition>();
            for (var y = target.Y - radius; y <= target.Y + radius; y++)
            {
                for (var x = target.X - radius; x <= target.X + radius; x++)
                {
                    if (Math.Max(Math.Abs(target.X - x), Math.Abs(target.Y - y)) != radius)
                    {
                        continue;
                    }

                    var candidate = new TilePosition(x, y);
                    if (World.IsInside(candidate) && IsGroundPassable(entity, candidate))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            foreach (var candidate in candidates
                .OrderBy(candidate => OctileDistance(start, candidate))
                .ThenBy(candidate => candidate.ManhattanDistance(target))
                .ThenBy(candidate => candidate.Y)
                .ThenBy(candidate => candidate.X))
            {
                yield return candidate;
            }
        }
    }

    private static List<TilePosition> ReconstructPath(Dictionary<TilePosition, TilePosition?> previous, TilePosition target)
    {
        var path = new List<TilePosition>();
        var current = target;
        while (previous[current] is not null)
        {
            path.Add(current);
            current = previous[current]!.Value;
        }

        path.Reverse();
        return path;
    }

    private static IEnumerable<TilePosition> GetNeighborTiles(TilePosition current)
    {
        yield return current.Offset(Direction.North);
        yield return current.Offset(Direction.East);
        yield return current.Offset(Direction.South);
        yield return current.Offset(Direction.West);
        yield return new TilePosition(current.X + 1, current.Y - 1);
        yield return new TilePosition(current.X + 1, current.Y + 1);
        yield return new TilePosition(current.X - 1, current.Y + 1);
        yield return new TilePosition(current.X - 1, current.Y - 1);
    }

    private bool CanEnterNeighbor(WorldEntity mover, TilePosition current, TilePosition next, TilePosition start)
    {
        if (!World.IsInside(next) || (next != start && !IsGroundPassable(mover, next)))
        {
            return false;
        }

        if (!IsDiagonalStep(current, next))
        {
            return true;
        }

        var horizontal = new TilePosition(next.X, current.Y);
        var vertical = new TilePosition(current.X, next.Y);
        return (horizontal == start || IsGroundPassable(mover, horizontal))
            && (vertical == start || IsGroundPassable(mover, vertical));
    }

    private static bool IsDiagonalStep(TilePosition from, TilePosition to)
    {
        return from.X != to.X && from.Y != to.Y;
    }

    /// <summary>Integer A* step cost (straight=10, diagonal=14 ≈ 10√2). See ADR 0001.</summary>
    private const int PathCostStraight = 10;
    private const int PathCostDiagonal = 14;

    private static int GetStepCost(TilePosition from, TilePosition to)
    {
        return IsDiagonalStep(from, to) ? PathCostDiagonal : PathCostStraight;
    }

    private static int OctileDistance(TilePosition from, TilePosition to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);
        var diagonal = Math.Min(dx, dy);
        var straight = Math.Max(dx, dy) - diagonal;
        return diagonal * PathCostDiagonal + straight * PathCostStraight;
    }

    private bool IsGroundPassable(WorldEntity mover, TilePosition tile)
    {
        return World.IsInside(tile) && !World.GetEntitiesAt(tile)
            .Any(entity => entity.Id != mover.Id && entity.IsAlive && MvpDefinitions.BlocksGroundMovement(entity.Kind));
    }

    private bool CanOccupyWorldPosition(WorldEntity mover, WorldPosition position)
    {
        if (!World.IsInside(position.ToTilePosition()))
        {
            return false;
        }

        var radius = MvpDefinitions.GetCollisionSize(mover.Kind).Radius;
        if (radius <= 0)
        {
            return true;
        }

        foreach (var entity in World.Entities)
        {
            if (entity.Id == mover.Id || !entity.IsAlive || entity.IsGarrisoned)
            {
                continue;
            }

            if (MvpDefinitions.BlocksGroundMovement(entity.Kind))
            {
                if (CircleIntersectsEntityFootprint(position, radius, entity)
                    && !MovesOutOfExistingOverlap(mover, position, radius, entity))
                {
                    return false;
                }

                continue;
            }

            // Moving units ignore unit↔unit collision (radius effectively 0); stopped units keep full size.
            if (IsMobileEntityMoving(mover) || IsMobileEntityMoving(entity))
            {
                continue;
            }

            var otherRadius = MvpDefinitions.GetCollisionSize(entity.Kind).Radius;
            if (otherRadius <= 0)
            {
                continue;
            }

            var minDistance = radius + otherRadius;
            var minDistanceSq = minDistance * minDistance;
            var nextDistanceSq = position.DistanceSquaredTo(entity.WorldPosition);
            if (nextDistanceSq >= minDistanceSq)
            {
                continue;
            }

            var currentDistanceSq = mover.WorldPosition.DistanceSquaredTo(entity.WorldPosition);
            if (currentDistanceSq < minDistanceSq && nextDistanceSq > currentDistanceSq)
            {
                // Allow sliding out of an existing overlap.
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// True when a mobile entity has active movement (waypoint, path, or move target) and is not garrisoned.
    /// </summary>
    private static bool IsMobileEntityMoving(WorldEntity entity)
    {
        if (entity.IsGarrisoned)
        {
            return false;
        }

        if (entity.CurrentWaypoint is not null || entity.MovementPath.Count > 0)
        {
            return true;
        }

        return entity.MoveTarget is not null && entity.MoveTarget.Value != entity.Position;
    }

    private static bool CircleIntersectsEntityFootprint(WorldPosition position, double radius, WorldEntity obstacle)
    {
        return DistanceSquaredToEntityFootprint(position, obstacle) < radius * radius;
    }

    private static bool MovesOutOfExistingOverlap(WorldEntity mover, WorldPosition nextPosition, double radius, WorldEntity obstacle)
    {
        var currentDistanceSq = DistanceSquaredToEntityFootprint(mover.WorldPosition, obstacle);
        var radiusSq = radius * radius;
        if (currentDistanceSq >= radiusSq)
        {
            return false;
        }

        return DistanceSquaredToEntityFootprint(nextPosition, obstacle) > currentDistanceSq;
    }

    private static double DistanceSquaredToEntityFootprint(WorldPosition position, WorldEntity obstacle)
    {
        var footprintKind = obstacle.Kind == EntityKind.GhostBuild && obstacle.BuildTargetKind is not null
            ? obstacle.BuildTargetKind.Value
            : obstacle.Kind;
        var footprint = MvpDefinitions.GetFootprint(footprintKind);
        var closestX = Math.Clamp(position.X, obstacle.Position.X, obstacle.Position.X + footprint.Width);
        var closestY = Math.Clamp(position.Y, obstacle.Position.Y, obstacle.Position.Y + footprint.Height);
        var dx = position.X - closestX;
        var dy = position.Y - closestY;
        return dx * dx + dy * dy;
    }

    private static void ResetMovementPath(WorldEntity entity)
    {
        entity.MovementPathMutable.Clear();
        entity.CurrentWaypoint = null;
    }

    private void ProcessCombat()
    {
        _presentation.ClearCombatShots();
        // Per-pass combat index keeps range/splash queries neighborhood-limited; GameWorld tile
        // occupancy (#66) does not replace position-radius combat scans yet.
        var spatial = CombatSpatialIndex.Build(World.Entities);
        foreach (var attacker in World.Entities.Where(entity =>
                     entity.IsAlive
                     && !entity.IsGarrisoned
                     && entity.OwnerId is not null
                     && MvpDefinitions.GetStats(entity.Kind).AttackDamage > 0).OrderBy(entity => entity.Id).ToList())
        {
            // Cascade may have killed this attacker earlier in the same pass.
            if (!attacker.IsAlive || attacker.IsGarrisoned)
            {
                continue;
            }

            if (attacker.AttackCooldownRemaining > 0)
            {
                attacker.AttackCooldownRemaining--;
                continue;
            }

            var stats = MvpDefinitions.GetStats(attacker.Kind);
            var attackRange = stats.AttackRange;
            var target = spatial.QueryByPositionInEuclideanRange(attacker.Position, attackRange)
                .Where(entity =>
                    entity.IsAlive
                    && !entity.IsGarrisoned
                    && entity.OwnerId is not null
                    && !AreAllied(attacker.OwnerId, entity.OwnerId))
                .OrderBy(entity => attacker.Position.EuclideanDistanceSquared(entity.Position))
                .ThenBy(entity => entity.Id)
                .FirstOrDefault();
            if (target is null)
            {
                continue;
            }

            if (MvpDefinitions.GetPowerDemand(attacker.Kind) > 0 && !TryConsumeBuildingEnergy(attacker))
            {
                continue;
            }

            attacker.AttackCooldownRemaining = ResolveAttackCooldown(attacker, stats);

            if (IsGroundToGroundBlockedByAlliedWall(attacker, target, stats.ProjectileKind, spatial))
            {
                continue;
            }

            _presentation.AddCombatShot(new CombatShotEvent(
                attacker.Id,
                target.Id,
                attacker.WorldPosition,
                target.WorldPosition,
                stats.ProjectileKind));

            var primaryDamage = ComputeDamageAgainst(attacker, stats, target);
            ApplyCombatDamage(target, primaryDamage);
            var killed = !target.IsAlive;

            if (stats.SplashRadius > 0)
            {
                foreach (var splashTarget in spatial.QueryByPositionInEuclideanRange(target.Position, stats.SplashRadius)
                             .Where(entity =>
                                 entity.IsAlive
                                 && !entity.IsGarrisoned
                                 && entity.Id != target.Id
                                 && entity.OwnerId is not null
                                 && !AreAllied(attacker.OwnerId, entity.OwnerId))
                             .OrderBy(entity => entity.Id)
                             .ToList())
                {
                    var splashDamage = ComputeDamageAgainst(attacker, stats, splashTarget);
                    ApplyCombatDamage(splashTarget, splashDamage);
                    if (!splashTarget.IsAlive)
                    {
                        killed = true;
                    }
                }
            }

            if (killed)
            {
                CascadeBastionDeaths();
            }
        }
    }

    private void CheckVictory()
    {
        var lossKinds = ResolveDefeatLossKinds();
        foreach (var player in _players)
        {
            // Empty loss set (catalog loaded with no Defeat entries) means no entity-based defeat.
            var criticalAlive = lossKinds.Count == 0
                || World.Entities.Any(entity =>
                    entity.OwnerId == player.Id
                    && entity.IsAlive
                    && lossKinds.Contains(entity.Kind));
            player.IsDefeated = !criticalAlive;
        }

        var activePlayers = _players.Where(player => !player.IsDefeated).ToList();
        if (activePlayers.Count == 1)
        {
            Status = GameStatus.PlayerWon;
            WinnerId = activePlayers[0].Id;
        }
    }

    /// <summary>
    /// Defeat kinds come from <see cref="EntityCatalog"/> when loaded; empty catalog keeps MVP
    /// commander-survival fallback so headless tests without JSON stay valid.
    /// </summary>
    private IReadOnlySet<EntityKind> ResolveDefeatLossKinds()
    {
        if (EntityCatalog.Entities.Count == 0)
        {
            return s_defaultDefeatLossKinds;
        }

        return EntityCatalog.GetDefeatLossKinds();
    }

    private static readonly HashSet<EntityKind> s_defaultDefeatLossKinds = [EntityKind.Commander];

    private void UpdateFogOfWar()
    {
        foreach (var player in _players)
        {
            player.DecayVisibility();
            foreach (var entity in World.Entities.Where(entity =>
                         entity.IsAlive
                         && !entity.IsGarrisoned
                         && entity.OwnerId is not null
                         && AreAllied(entity.OwnerId.Value, player.Id)))
            {
                var radius = MvpDefinitions.GetStats(entity.Kind).VisionRadius;
                if (entity.OwnerId is not null)
                {
                    radius = ResolveStat(entity.OwnerId.Value, ResearchStatIds.VisionRadius, radius, minValue: 0);
                }
                for (var y = entity.Position.Y - radius; y <= entity.Position.Y + radius; y++)
                {
                    for (var x = entity.Position.X - radius; x <= entity.Position.X + radius; x++)
                    {
                        var position = new TilePosition(x, y);
                        if (World.IsInside(position) && entity.Position.IsWithinEuclideanRange(position, radius))
                        {
                            player.SetVisible(position);
                        }
                    }
                }
            }
        }
    }

    private void UpdateTechSignatures()
    {
        foreach (var player in _players)
        {
            var hotspots = World.Entities
                .Where(entity =>
                    entity.IsAlive
                    && entity.OwnerId is not null
                    && !AreAllied(entity.OwnerId.Value, player.Id))
                .Select(entity => new
                {
                    ZoneX = entity.Position.X / 8,
                    ZoneY = entity.Position.Y / 8,
                    Intensity = MvpDefinitions.TechSignatureIntensity.GetValueOrDefault(entity.Kind)
                })
                .Where(signal => signal.Intensity > 0)
                .GroupBy(signal => new { signal.ZoneX, signal.ZoneY })
                .Select(group => new TechSignatureHotspot(group.Key.ZoneX, group.Key.ZoneY, group.Sum(signal => signal.Intensity)))
                .OrderByDescending(signal => signal.Intensity)
                .ToList();
            player.SetTechSignatures(hotspots);
        }
    }
}
