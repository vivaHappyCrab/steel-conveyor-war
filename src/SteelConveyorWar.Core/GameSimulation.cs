namespace SteelConveyorWar.Core;

public sealed class GameSimulation
{
    public const int TicksPerSecond = 30;

    private readonly List<PlayerState> _players;
    private readonly ResearchSystem _researchSystem;
    private int _nextEntityId = 1;

    private GameSimulation(
        GameWorld world,
        IReadOnlyList<PlayerState> players,
        int randomSeed,
        ResearchCatalog catalog,
        ResearchProfileDefinition profile,
        TileCatalog tiles,
        EntityCatalog entities)
    {
        World = world;
        _players = players.ToList();
        RandomSeed = randomSeed;
        ResearchCatalog = catalog;
        ResearchProfile = profile;
        TileCatalog = tiles;
        EntityCatalog = entities;
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

    public long Tick { get; private set; }

    public IReadOnlyList<PlayerState> Players => _players;

    public GameStatus Status { get; private set; } = GameStatus.InProgress;

    public PlayerId? WinnerId { get; private set; }

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

        var size = new WorldSize(48, 28);
        var terrain = CreateStartingTerrain(size, options.RandomSeed);
        var players = new[]
        {
            new PlayerState(new PlayerId(1), "Blue", size),
            new PlayerState(new PlayerId(2), "Red", size)
        };

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
            options.Entities);
        simulation.CreateStartingEntities();
        simulation.UpdatePower();
        simulation.UpdateFogOfWar();
        simulation.UpdateTechSignatures();
        return simulation;
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

    public bool TryPlaceGhostBuildFromCommander(int commanderId, EntityKind targetKind, TilePosition position, out int ghostId)
    {
        ghostId = 0;
        var commander = World.GetEntity(commanderId);
        if (commander is null || commander.Kind != EntityKind.Commander || commander.OwnerId is null || !commander.IsAlive)
        {
            return false;
        }

        if (!World.IsInside(position) || !MvpDefinitions.BuildCosts.TryGetValue(targetKind, out var cost))
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

        if (!commander.Inventory.TryRemoveAll(cost))
        {
            return false;
        }

        var ghost = CreateEntity(EntityKind.GhostBuild, position, commander.OwnerId);
        ghost.BuildTargetKind = targetKind;
        var buildTicks = MvpDefinitions.BuildTicks.GetValueOrDefault(targetKind, TicksPerSecond);
        if (commander.OwnerId is not null)
        {
            buildTicks = ResolveStat(commander.OwnerId.Value, ResearchStatIds.ConstructionTicks, buildTicks);
        }

        ghost.ConstructionTicksRemaining = buildTicks;
        ghostId = ghost.Id;
        World.AddEntity(ghost);
        commander.QueuedBuildOrder = null;
        return true;
    }

    public bool TryQueueCommanderBuild(int commanderId, EntityKind targetKind, TilePosition position)
    {
        var commander = World.GetEntity(commanderId);
        if (commander is null || commander.Kind != EntityKind.Commander || commander.OwnerId is null || !commander.IsAlive)
        {
            return false;
        }

        if (!MvpDefinitions.BuildCosts.ContainsKey(targetKind) || !IsBuildUnlocked(commander.OwnerId.Value, targetKind) || !CanPlaceBuilding(targetKind, position))
        {
            return false;
        }

        if (IsWithinBuildRadius(commander, targetKind, position))
        {
            return TryPlaceGhostBuildFromCommander(commanderId, targetKind, position, out _);
        }

        commander.QueuedBuildOrder = new CommanderBuildOrder(targetKind, position);
        commander.IsGarrisoned = false;
        commander.MoveTarget = null;
        ResetMovementPath(commander);
        return true;
    }

    public bool TryRotateEntity(int entityId, bool clockwise)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null || entity.Kind is not (EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter))
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
        entity.IsGarrisoned = false;
        ResetMovementPath(entity);
        return true;
    }

    public bool TryStartResearch(PlayerId playerId, TechnologyId technology)
    {
        return TrySelectResearch(playerId, technology) == ResearchCommandResult.Ok;
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
            if (bastionId is not null)
            {
                factory.AssignedBastionId = bastionId;
            }

            return true;
        }

        if (!MvpDefinitions.ProductionRecipes.ContainsKey(outputKind.Value)
            || !CanFactoryProduce(factory.Kind, outputKind.Value))
        {
            return false;
        }

        factory.ProductionTargetKind = outputKind;
        factory.AssignedBastionId = bastionId;
        return true;
    }

    public bool TryForceCompleteResearch(PlayerId playerId, TechnologyId technologyId, bool confirmExclusive = true)
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
        return research.CompletedTechnologies.Contains(technologyId);
    }

    public bool TrySetBastionTemplate(int bastionId, EntityKind unitKind, int count)
    {
        var bastion = World.GetEntity(bastionId);
        if (bastion is null || bastion.Kind != EntityKind.Bastion || count < 0 || !MvpDefinitions.UnitKinds.Contains(unitKind))
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
        return true;
    }

    public bool TryIssueBastionOrder(int bastionId, BastionOrder order)
    {
        var bastion = World.GetEntity(bastionId);
        if (bastion is null || bastion.Kind != EntityKind.Bastion)
        {
            return false;
        }

        bastion.Order = order;
        foreach (var unit in World.Entities.Where(entity => entity.AssignedBastionId == bastionId && MvpDefinitions.UnitKinds.Contains(entity.Kind)))
        {
            unit.Order = order;
            unit.IsGarrisoned = false;
        }

        return true;
    }

    public void AddPlayerItems(PlayerId playerId, ItemId item, int amount)
    {
        GetPlayer(playerId).Inventory.Add(item, amount);
    }

    public bool AddItemToEntity(int entityId, ItemId item, int amount)
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
        if (commander is null || target is null || commander.Kind != EntityKind.Commander || !commander.IsAlive)
        {
            return false;
        }

        if (DistanceToFootprint(commander.WorldPosition, target.Kind, target.Position) > MvpDefinitions.CommanderInteractRadius)
        {
            return false;
        }

        foreach (var item in target.OutputBuffer.Items.ToList())
        {
            commander.Inventory.Add(item.Key, item.Value);
        }

        target.OutputBuffer.Clear();
        return true;
    }

    public void DamageEntity(int entityId, int damage)
    {
        var entity = World.GetEntity(entityId);
        if (entity is null || !entity.IsAlive || damage <= 0)
        {
            return;
        }

        entity.Health = Math.Max(0, entity.Health - damage);
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

    public void AdvanceTick()
    {
        if (Status != GameStatus.InProgress)
        {
            return;
        }

        Tick++;
        ProcessCommanderBuildOrders();
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
        ProcessRepairOutOfCombat();
        CheckVictory();
        World.RemoveDead();
        UpdateFogOfWar();
        UpdateTechSignatures();
    }

    private void CreateStartingEntities()
    {
        var playerOne = new PlayerId(1);
        var playerTwo = new PlayerId(2);

        var commanderOne = AddCompletedEntity(EntityKind.Commander, new TilePosition(4, World.Size.Height / 2), playerOne);
        AddStartingCommanderInventory(commanderOne);
        AddCompletedEntity(EntityKind.Bastion, new TilePosition(1, World.Size.Height / 2), playerOne);
        AddCompletedEntity(EntityKind.Hub, new TilePosition(5, World.Size.Height / 2 + 2), playerOne);

        var commanderTwo = AddCompletedEntity(EntityKind.Commander, new TilePosition(World.Size.Width - 5, World.Size.Height / 2), playerTwo);
        AddStartingCommanderInventory(commanderTwo);
        AddCompletedEntity(EntityKind.Bastion, new TilePosition(World.Size.Width - 4, World.Size.Height / 2), playerTwo);
        AddCompletedEntity(EntityKind.Hub, new TilePosition(World.Size.Width - 6, World.Size.Height / 2 + 2), playerTwo);
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
        return entity;
    }

    private static void ConfigureEntityDefaults(WorldEntity entity)
    {
        if (entity.Kind == EntityKind.Conveyor || entity.Kind == EntityKind.UndergroundConveyor)
        {
            entity.MaxHealth = 40;
            entity.Health = 40;
        }

        if (entity.Kind == EntityKind.Assembler && entity.SelectedItemRecipe is null)
        {
            entity.SelectedItemRecipe = ItemRecipeId.IronGear;
        }
    }

    private static TerrainType[,] CreateStartingTerrain(WorldSize size, int randomSeed)
    {
        // Local RNG only — not retained for later ticks (determinism stays seed → layout).
        var rng = new Random(randomSeed);
        var terrain = new TerrainType[size.Width, size.Height];
        var halfWidth = size.Width / 2;

        // Left-half start ores near Blue; right half is mirrored for PvP fairness.
        var ironCenter = JitterTile(rng, baseX: 7, baseY: 7, maxOffset: 1, minX: 5, maxX: halfWidth - 1, minY: 4, maxY: size.Height - 5);
        var copperCenter = JitterTile(rng, baseX: 7, baseY: 13, maxOffset: 1, minX: 5, maxX: halfWidth - 1, minY: 4, maxY: size.Height - 5);
        FillOrePatchLeftHalf(terrain, ironCenter, TerrainType.IronOre, maxDistance: 2 + rng.Next(0, 2), halfWidth);
        FillOrePatchLeftHalf(terrain, copperCenter, TerrainType.CopperOre, maxDistance: 2 + rng.Next(0, 2), halfWidth);

        // Neutral coal/oil near center on the left half, then mirrored.
        var coalCenter = JitterTile(rng, baseX: halfWidth - 4, baseY: 8, maxOffset: 1, minX: halfWidth - 6, maxX: halfWidth - 1, minY: 4, maxY: size.Height / 2);
        var oilCenter = JitterTile(rng, baseX: halfWidth - 4, baseY: size.Height - 9, maxOffset: 1, minX: halfWidth - 6, maxX: halfWidth - 1, minY: size.Height / 2, maxY: size.Height - 5);
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
        for (var y = center.Y - maxDistance; y <= center.Y + maxDistance; y++)
        {
            for (var x = center.X - maxDistance; x <= center.X + maxDistance; x++)
            {
                var distance = Math.Abs(center.X - x) + Math.Abs(center.Y - y);
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
                TryPlaceGhostBuildFromCommander(commander.Id, order.TargetKind, order.TargetPosition, out _);
                continue;
            }

            MoveMobileEntityTowardTile(commander, order.TargetPosition);
        }
    }

    private void ProcessCommanderMoveCommands()
    {
        foreach (var commander in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Commander && entity.MoveTarget is not null && entity.QueuedBuildOrder is null))
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
        return DistanceToFootprint(commander.WorldPosition, targetKind, anchor) <= MvpDefinitions.CommanderBuildRadius;
    }

    private static int DistanceToFootprint(TilePosition from, EntityKind targetKind, TilePosition anchor)
    {
        return GameWorld.GetFootprintTiles(targetKind, anchor)
            .Min(tile => from.ManhattanDistance(tile));
    }

    private static double DistanceToFootprint(WorldPosition from, EntityKind targetKind, TilePosition anchor)
    {
        var footprint = MvpDefinitions.GetFootprint(targetKind);
        var closestX = Math.Clamp(from.X, anchor.X, anchor.X + footprint.Width);
        var closestY = Math.Clamp(from.Y, anchor.Y, anchor.Y + footprint.Height);
        var dx = from.X - closestX;
        var dy = from.Y - closestY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

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
        if (kind == EntityKind.Bastion && World.Entities.Any(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion))
        {
            return CapabilityResolver.HasCapability(player.Research, ResearchCapabilityIds.AdditionalBastions);
        }

        if (CapabilityResolver.IsEntityUnlocked(player.Research, kind, ResearchCatalog, ResearchProfile))
        {
            return true;
        }

        if (MvpDefinitions.BuildRequirements.TryGetValue(kind, out var requiredTechnology))
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
        }
    }

    private void UpdatePower()
    {
        foreach (var player in _players)
        {
            player.PowerProduced = 0;
            player.PowerDemand = 0;
        }

        foreach (var entity in World.Entities.Where(entity => entity.IsAlive && entity.OwnerId is not null))
        {
            var player = GetPlayer(entity.OwnerId!.Value);
            player.PowerProduced += entity.Kind switch
            {
                EntityKind.SolarPanel => MvpDefinitions.PowerProduction.GetValueOrDefault(EntityKind.SolarPanel),
                EntityKind.CoalPlant when entity.InputBuffer.TryRemove(ItemId.Coal, 1) => MvpDefinitions.PowerProduction.GetValueOrDefault(EntityKind.CoalPlant),
                _ => 0
            };
            player.PowerDemand += MvpDefinitions.PowerDemand.GetValueOrDefault(entity.Kind);
        }
    }

    private void ProduceRawResources()
    {
        if (Tick % 15 != 0)
        {
            return;
        }

        foreach (var entity in World.Entities.Where(entity => entity.IsAlive))
        {
            var terrain = World.GetTerrain(entity.Position);
            switch (entity.Kind)
            {
                case EntityKind.Mine when terrain == TerrainType.IronOre:
                    TryAddToBuffer(entity.OutputBuffer, ItemId.IronOre, 1);
                    break;
                case EntityKind.Mine when terrain == TerrainType.CopperOre:
                    TryAddToBuffer(entity.OutputBuffer, ItemId.CopperOre, 1);
                    break;
                case EntityKind.CoalMine when terrain == TerrainType.Coal:
                    TryAddToBuffer(entity.OutputBuffer, ItemId.Coal, 1);
                    break;
                case EntityKind.OilWell when terrain == TerrainType.Oil:
                    TryAddToBuffer(entity.OutputBuffer, ItemId.CrudeOil, 1);
                    break;
            }
        }
    }

    private void ProcessBuildingWork()
    {
        foreach (var building in World.Entities.Where(entity => entity.IsAlive))
        {
            if (building.WorkTicksRemaining > 0 && building.PendingOutputItem is not null && !MvpDefinitions.FactoryKinds.Contains(building.Kind))
            {
                building.WorkTicksRemaining--;
                if (building.WorkTicksRemaining == 0)
                {
                    TryCompletePendingOutput(building);
                }

                continue;
            }

            if (building.WorkTicksRemaining == 0 && building.PendingOutputItem is not null && !MvpDefinitions.FactoryKinds.Contains(building.Kind))
            {
                TryCompletePendingOutput(building);
                continue;
            }

            if (building.WorkTicksRemaining > 0)
            {
                continue;
            }

            switch (building.Kind)
            {
                case EntityKind.Smelter:
                    StartItemRecipe(building, ItemId.IronOre, ItemId.IronPlate, 20);
                    StartItemRecipe(building, ItemId.CopperOre, ItemId.CopperPlate, 20);
                    if (building.PendingOutputItem is null && building.InputBuffer.Has(ItemId.IronPlate, 2) && building.InputBuffer.TryRemove(ItemId.Coal, 1))
                    {
                        building.InputBuffer.TryRemove(ItemId.IronPlate, 2);
                        building.PendingOutputItem = ItemId.Steel;
                        var steelTicks = 30;
                        if (building.OwnerId is not null)
                        {
                            steelTicks = ResolveStat(building.OwnerId.Value, ResearchStatIds.SmelterWorkTicks, steelTicks);
                            steelTicks = ApplyEnergyShortage(building.OwnerId.Value, steelTicks);
                        }
                        building.WorkTicksRemaining = steelTicks;
                    }

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
            workTicks = ApplyEnergyShortage(building.OwnerId.Value, workTicks);
        }

        building.PendingOutputItem = output;
        building.PendingOutputAmount = 1;
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
            workTicks = ApplyEnergyShortage(assembler.OwnerId.Value, workTicks);
        }

        assembler.PendingOutputItem = recipe.OutputItem;
        assembler.PendingOutputAmount = recipe.OutputAmount;
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
        foreach (var inserter in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Inserter).OrderBy(entity => entity.Id))
        {
            if (inserter.HeldItem is null)
            {
                var source = World.GetTopEntityAt(inserter.Position.Offset(Opposite(inserter.Direction)));
                if (source is not null && TryExtractItem(source, inserter.FilterItem, out var item))
                {
                    inserter.HeldItem = item;
                    inserter.HeldTransferTicksRemaining = MvpDefinitions.InserterTransferTicks;
                }

                continue;
            }

            if (inserter.HeldTransferTicksRemaining > 0)
            {
                inserter.HeldTransferTicksRemaining--;
                continue;
            }

            var target = World.GetTopEntityAt(inserter.Position.Offset(inserter.Direction));
            if (target is not null && TryInsertItem(target, inserter.HeldItem.Value))
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
    }

    private int ApplyEnergyShortage(PlayerId playerId, int workTicks)
    {
        var player = GetPlayer(playerId);
        // No generators yet: treat as pre-power economy (no shortage slowdown).
        if (player.PowerProduced <= 0 || player.PowerDemand <= player.PowerProduced || player.PowerDemand <= 0)
        {
            return workTicks;
        }

        var shortageBasisPoints = Math.Min(
            ModifierResolver.BasisPointsScale,
            (player.PowerDemand - player.PowerProduced) * ModifierResolver.BasisPointsScale / player.PowerDemand);
        var penalty = ResolveStat(playerId, ResearchStatIds.EnergyShortagePenalty, shortageBasisPoints, minValue: 0);
        var slowed = workTicks + (int)((long)workTicks * penalty / ModifierResolver.BasisPointsScale);
        return Math.Max(workTicks, slowed);
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
                factory.WorkTicksRemaining--;
                if (factory.WorkTicksRemaining == 0)
                {
                    SpawnProducedUnit(factory, factory.ProductionTargetKind.Value);
                    factory.ProductionTargetKind = null;
                }

                continue;
            }

            if (factory.ProductionTargetKind is null && factory.AssignedBastionId is not null)
            {
                factory.ProductionTargetKind = ChooseBastionDeficit(factory);
            }

            if (factory.ProductionTargetKind is null || !MvpDefinitions.ProductionRecipes.TryGetValue(factory.ProductionTargetKind.Value, out var recipe))
            {
                continue;
            }

            if (factory.OwnerId is not null && recipe.RequiredTechnology is not null)
            {
                var owner = GetPlayer(factory.OwnerId.Value);
                var unlocked = owner.ResearchedTechnologies.Contains(recipe.RequiredTechnology.Value)
                    || CapabilityResolver.IsRecipeUnlocked(owner.Research, recipe.OutputKind.ToString())
                    || owner.Research.UnlockedEntityKinds.Contains(recipe.OutputKind.ToString());
                if (!unlocked)
                {
                    continue;
                }
            }

            if (!CanFactoryProduce(factory.Kind, recipe.OutputKind) || !factory.InputBuffer.TryRemoveAll(recipe.Inputs))
            {
                continue;
            }

            var workTicks = recipe.WorkTicks;
            if (factory.OwnerId is not null)
            {
                workTicks = ResolveStat(factory.OwnerId.Value, ResearchStatIds.FactoryWorkTicks, workTicks, recipe.OutputKind.ToString());
                workTicks = ApplyEnergyShortage(factory.OwnerId.Value, workTicks);
            }

            factory.WorkTicksRemaining = workTicks;
        }
    }

    private EntityKind? ChooseBastionDeficit(WorldEntity factory)
    {
        if (factory.AssignedBastionId is null)
        {
            return null;
        }

        var bastion = World.GetEntity(factory.AssignedBastionId.Value);
        if (bastion is null)
        {
            return null;
        }

        foreach (var desired in bastion.BastionTemplate.OrderBy(pair => pair.Key))
        {
            if (!CanFactoryProduce(factory.Kind, desired.Key))
            {
                continue;
            }

            var current = World.Entities.Count(entity => entity.IsAlive && entity.AssignedBastionId == bastion.Id && entity.Kind == desired.Key);
            if (current < desired.Value)
            {
                return desired.Key;
            }
        }

        return null;
    }

    private static bool CanFactoryProduce(EntityKind factoryKind, EntityKind unitKind)
    {
        return unitKind == EntityKind.Scout ? factoryKind == EntityKind.DroneCenter : factoryKind == EntityKind.TankFactory;
    }

    private void SpawnProducedUnit(WorldEntity factory, EntityKind unitKind)
    {
        if (factory.OwnerId is null)
        {
            return;
        }

        var unit = CreateEntity(unitKind, FindSpawnTileNear(factory, unitKind), factory.OwnerId);
        unit.AssignedBastionId = factory.AssignedBastionId;
        World.AddEntity(unit);
    }

    private TilePosition FindSpawnTileNear(WorldEntity factory, EntityKind unitKind)
    {
        var footprint = MvpDefinitions.GetFootprint(factory.Kind);
        var candidates = new List<TilePosition>();
        for (var x = factory.Position.X - 1; x <= factory.Position.X + footprint.Width; x++)
        {
            candidates.Add(new TilePosition(x, factory.Position.Y - 1));
            candidates.Add(new TilePosition(x, factory.Position.Y + footprint.Height));
        }

        for (var y = factory.Position.Y; y < factory.Position.Y + footprint.Height; y++)
        {
            candidates.Add(new TilePosition(factory.Position.X - 1, y));
            candidates.Add(new TilePosition(factory.Position.X + footprint.Width, y));
        }

        return candidates
            .Where(tile => World.IsInside(tile))
            .OrderBy(tile => tile.ManhattanDistance(factory.Position))
            .FirstOrDefault(tile =>
            {
                var probe = new WorldEntity(-1, unitKind, tile, factory.OwnerId);
                return IsGroundPassable(probe, tile) && CanOccupyWorldPosition(probe, probe.WorldPosition);
            }, factory.Position);
    }

    private void ProcessBastions()
    {
        foreach (var bastion in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Bastion))
        {
            foreach (var unit in World.Entities.Where(entity => entity.IsAlive && entity.AssignedBastionId == bastion.Id && MvpDefinitions.UnitKinds.Contains(entity.Kind)))
            {
                if (unit.Position.ManhattanDistance(bastion.Position) <= 1 && bastion.Order.Kind == BastionOrderKind.Defend)
                {
                    unit.Position = bastion.Position;
                    unit.WorldPosition = WorldPosition.FromTileCenter(bastion.Position);
                    ResetMovementPath(unit);
                    unit.IsGarrisoned = true;
                }
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
        if (unit.Order.Kind == BastionOrderKind.AttackArea && unit.Order.Target is not null)
        {
            return unit.Order.Target;
        }

        if (unit.Order.Kind == BastionOrderKind.Support && unit.Order.FollowEntityId is not null)
        {
            return World.GetEntity(unit.Order.FollowEntityId.Value)?.Position;
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
            entity.Position = entity.CurrentWaypoint.Value;
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
        entity.Position = nextPosition.ToTilePosition();
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

        var open = new PriorityQueue<TilePosition, (double F, double H, int Y, int X)>();
        var previous = new Dictionary<TilePosition, TilePosition?>();
        var costSoFar = new Dictionary<TilePosition, double>();
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

    private static double GetStepCost(TilePosition from, TilePosition to)
    {
        return IsDiagonalStep(from, to) ? Math.Sqrt(2) : 1;
    }

    private static double OctileDistance(TilePosition from, TilePosition to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);
        var diagonal = Math.Min(dx, dy);
        var straight = Math.Max(dx, dy) - diagonal;
        return diagonal * Math.Sqrt(2) + straight;
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

        return !World.Entities.Any(entity =>
            entity.Id != mover.Id
            && entity.IsAlive
            && MvpDefinitions.BlocksGroundMovement(entity.Kind)
            && CircleIntersectsEntityFootprint(position, radius, entity)
            && !MovesOutOfExistingOverlap(mover, position, radius, entity));
    }

    private static bool CircleIntersectsEntityFootprint(WorldPosition position, double radius, WorldEntity obstacle)
    {
        return DistanceToEntityFootprint(position, obstacle) < radius;
    }

    private static bool MovesOutOfExistingOverlap(WorldEntity mover, WorldPosition nextPosition, double radius, WorldEntity obstacle)
    {
        var currentDistance = DistanceToEntityFootprint(mover.WorldPosition, obstacle);
        if (currentDistance >= radius)
        {
            return false;
        }

        return DistanceToEntityFootprint(nextPosition, obstacle) > currentDistance;
    }

    private static double DistanceToEntityFootprint(WorldPosition position, WorldEntity obstacle)
    {
        var footprintKind = obstacle.Kind == EntityKind.GhostBuild && obstacle.BuildTargetKind is not null
            ? obstacle.BuildTargetKind.Value
            : obstacle.Kind;
        var footprint = MvpDefinitions.GetFootprint(footprintKind);
        var closestX = Math.Clamp(position.X, obstacle.Position.X, obstacle.Position.X + footprint.Width);
        var closestY = Math.Clamp(position.Y, obstacle.Position.Y, obstacle.Position.Y + footprint.Height);
        var dx = position.X - closestX;
        var dy = position.Y - closestY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void ResetMovementPath(WorldEntity entity)
    {
        entity.MovementPathMutable.Clear();
        entity.CurrentWaypoint = null;
    }

    private void ProcessCombat()
    {
        foreach (var attacker in World.Entities.Where(entity => entity.IsAlive && entity.OwnerId is not null && MvpDefinitions.GetStats(entity.Kind).AttackDamage > 0).OrderBy(entity => entity.Id).ToList())
        {
            if (attacker.AttackCooldownRemaining > 0)
            {
                attacker.AttackCooldownRemaining--;
                continue;
            }

            var stats = MvpDefinitions.GetStats(attacker.Kind);
            var target = World.Entities
                .Where(entity => entity.IsAlive && entity.OwnerId is not null && entity.OwnerId != attacker.OwnerId)
                .Where(entity => entity.Position.ManhattanDistance(attacker.Position) <= stats.AttackRange)
                .OrderBy(entity => entity.Position.ManhattanDistance(attacker.Position))
                .ThenBy(entity => entity.Id)
                .FirstOrDefault();
            if (target is null)
            {
                continue;
            }

            target.Health = Math.Max(0, target.Health - stats.AttackDamage);
            attacker.AttackCooldownRemaining = stats.AttackCooldownTicks;
        }
    }

    private void CheckVictory()
    {
        foreach (var player in _players)
        {
            var commanderAlive = World.Entities.Any(entity => entity.OwnerId == player.Id && entity.Kind == EntityKind.Commander && entity.IsAlive);
            player.IsDefeated = !commanderAlive;
        }

        var activePlayers = _players.Where(player => !player.IsDefeated).ToList();
        if (activePlayers.Count == 1)
        {
            Status = GameStatus.PlayerWon;
            WinnerId = activePlayers[0].Id;
        }
    }

    private void UpdateFogOfWar()
    {
        foreach (var player in _players)
        {
            player.DecayVisibility();
            foreach (var entity in World.Entities.Where(entity => entity.IsAlive && entity.OwnerId == player.Id))
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
                        if (World.IsInside(position) && entity.Position.ManhattanDistance(position) <= radius)
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
                .Where(entity => entity.IsAlive && entity.OwnerId is not null && entity.OwnerId != player.Id)
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
