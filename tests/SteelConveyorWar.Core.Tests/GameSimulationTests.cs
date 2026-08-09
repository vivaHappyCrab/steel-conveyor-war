using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public class GameSimulationTests
{
    [Fact]
    public void AdvanceTick_IncrementsSimulationTickByOne()
    {
        var simulation = GameSimulation.CreateNewGame();

        simulation.AdvanceTick();

        Assert.Equal(1, simulation.Tick);
    }

    [Fact]
    public void CreateNewGame_CreatesCommanderAndStartingOrePatches()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);

        Assert.Equal(new WorldSize(192, 112), simulation.World.Size);
        Assert.Contains(simulation.World.Entities, entity => entity.Kind == EntityKind.Commander);
        Assert.Equal(2, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.Commander));
        Assert.Equal(2, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.SolarPanel));
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.IronOre && t.X < simulation.World.Size.Width / 2);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.CopperOre && t.X < simulation.World.Size.Width / 2);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.IronOre && t.X >= simulation.World.Size.Width / 2);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.CopperOre && t.X >= simulation.World.Size.Width / 2);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.Coal);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.Oil);
    }

    [Fact]
    public void DamageEntity_WhenEnemyCommanderDies_PlayerWins()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var enemyCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));

        simulation.DamageEntity(enemyCommander.Id, enemyCommander.Health);

        Assert.Equal(GameStatus.PlayerWon, simulation.Status);
        Assert.Equal(new PlayerId(1), simulation.WinnerId);
    }

    [Fact]
    public void TryPlaceGhostBuild_SpendsResourcesAndCompletesBuilding()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var initialIron = commander.Inventory.Count(ItemId.IronPlate);
        var ironTile = FindReachableTerrain(simulation, commander, TerrainType.IronOre);

        var placed = simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, ironTile, out var ghostId);
        AdvanceTicks(simulation, 30);

        var built = simulation.World.GetEntity(ghostId);
        Assert.True(placed);
        Assert.NotNull(built);
        Assert.Equal(EntityKind.Mine, built.Kind);
        Assert.True(commander.Inventory.Count(ItemId.IronPlate) < initialIron);
    }

    [Fact]
    public void MineAndSmelter_ProduceT1Resources()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var ironTile = FindReachableTerrain(simulation, commander, TerrainType.IronOre);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, ironTile, out var mineId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(simulation, 6, -2), out var smelterId));
        AdvanceTicks(simulation, 30);

        AdvanceTicks(simulation, 15);
        Assert.True(simulation.World.GetEntity(mineId)!.OutputBuffer.Count(ItemId.IronOre) > 0);

        simulation.AddItemToEntity(smelterId, ItemId.IronOre, 1);
        AdvanceTicks(simulation, 21);

        Assert.Equal(1, simulation.World.GetEntity(smelterId)!.OutputBuffer.Count(ItemId.IronPlate));
    }

    [Fact]
    public void InserterAndConveyor_MoveItemsDeterministically()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, NearBlue(simulation, 2, 4), out var sourceHubId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Inserter, NearBlue(simulation, 3, 4), out var inserterId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, NearBlue(simulation, 4, 4), out var conveyorId));
        AdvanceTicks(simulation, 30);

        simulation.TryAddOutputItemToEntity(sourceHubId, ItemId.IronPlate, 1);
        AdvanceTicks(simulation, MvpDefinitions.InserterTransferTicks + 2);

        Assert.Single(simulation.World.GetEntity(conveyorId)!.ConveyorItems);
        Assert.Equal(ItemId.IronPlate, simulation.World.GetEntity(conveyorId)!.ConveyorItems[0].Item);
    }

    [Fact]
    public void Factory_ProducesUnitForAssignedBastion()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        AdvanceTicks(simulation, 36);

        Assert.Contains(simulation.World.Entities, entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id);
    }

    [Fact]
    public void BastionTemplate_AssignedFactoryAutofillsMissingUnits()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, null));
        AdvanceTicks(simulation, 70);

        Assert.Contains(simulation.World.Entities, entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id);
    }

    [Fact]
    public void TrySetBastionTemplate_RejectsSumOverCapacity()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.Equal(MvpDefinitions.BaseBastionTemplateCapacity, simulation.GetBastionTemplateCapacity(playerId));

        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 10));
        Assert.False(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.LightBot, 1));
        Assert.Equal(10, bastion.BastionTemplate[EntityKind.BasicTank]);
        Assert.False(bastion.BastionTemplate.ContainsKey(EntityKind.LightBot));
    }

    [Fact]
    public void BastionCount_CapStartsAtOne_RaisesAfterAdditionalBastions()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        Assert.Equal(1, simulation.GetMaxBastionCount(playerId));
        Assert.Equal(1, simulation.CountOwnedBastions(playerId));

        var commander = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Commander);
        Assert.False(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Bastion,
            NearBlue(simulation, 8, -4),
            out _));

        UnlockTier2ForTests(simulation, playerId);
        Assert.True(simulation.TryForceCompleteResearch(playerId, TechnologyId.AdditionalBastions));
        Assert.Equal(MvpDefinitions.MaxBastionsAfterUnlock, simulation.GetMaxBastionCount(playerId));
        Assert.Equal(MvpDefinitions.BaseBastionTemplateCapacity + 2, simulation.GetBastionTemplateCapacity(playerId));

        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Bastion,
            NearBlue(simulation, 8, -4),
            out var ghostId));
        Assert.Equal(2, simulation.CountOwnedBastions(playerId));
        Assert.NotEqual(0, ghostId);
    }

    [Fact]
    public void TrySetFactoryProduction_ManualRecipe_ContinuesAfterSpawn()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 40);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 20);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        AdvanceTicks(simulation, 36);

        var factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(EntityKind.BasicTank, factory.ProductionTargetKind);
        Assert.True(factory.IsManualProductionTarget);
        Assert.Equal(1, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id));

        AdvanceTicks(simulation, 40);
        Assert.True(simulation.World.Entities.Count(entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id) >= 2);
        Assert.Equal(EntityKind.BasicTank, simulation.World.GetEntity(factoryId)!.ProductionTargetKind);
    }

    [Fact]
    public void TrySetFactoryProduction_RejectsFactoryUnitMismatch()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.False(simulation.TrySetFactoryProduction(factoryId, EntityKind.Scout));
        Assert.Null(simulation.World.GetEntity(factoryId)!.ProductionTargetKind);
    }

    [Fact]
    public void TrySetFactoryProduction_RecipeOnly_PreservesAssignedBastion()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        Assert.Equal(bastion.Id, simulation.World.GetEntity(factoryId)!.AssignedBastionId);

        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.LightBot));
        var factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(EntityKind.LightBot, factory.ProductionTargetKind);
        Assert.Equal(bastion.Id, factory.AssignedBastionId);
        Assert.True(factory.IsManualProductionTarget);
    }

    [Fact]
    public void TryAssignFactoryBastion_PreservesAutofillMode()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, null));
        Assert.False(simulation.World.GetEntity(factoryId)!.IsManualProductionTarget);

        UnlockTier2ForTests(simulation, playerId);
        Assert.True(simulation.TryForceCompleteResearch(playerId, TechnologyId.AdditionalBastions));
        var commander = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Commander);
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Bastion,
            NearBlue(simulation, 8, -4),
            out var secondBastionGhostId));
        AdvanceTicks(simulation, 30);
        var secondBastion = simulation.World.GetEntity(secondBastionGhostId)!;
        Assert.Equal(EntityKind.Bastion, secondBastion.Kind);

        // Idle autofill with no inputs leaves a sticky deficit pick; reassignment must clear it.
        AdvanceTicks(simulation, 1);
        Assert.Equal(EntityKind.BasicTank, simulation.World.GetEntity(factoryId)!.ProductionTargetKind);
        Assert.True(simulation.TryAssignFactoryBastion(factoryId, secondBastion.Id));
        var factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(secondBastion.Id, factory.AssignedBastionId);
        Assert.False(factory.IsManualProductionTarget);
        Assert.Null(factory.ProductionTargetKind);

        Assert.True(simulation.TryAssignFactoryBastion(factoryId, bastion.Id));
        factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(bastion.Id, factory.AssignedBastionId);
        Assert.False(factory.IsManualProductionTarget);
        Assert.Null(factory.ProductionTargetKind);
    }

    [Fact]
    public void BastionTemplate_AutofillCountsInFlightProductionAcrossFactories()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryA));
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 6, 6), out var factoryB));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        foreach (var factoryId in new[] { factoryA, factoryB })
        {
            simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
            simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
            Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
            Assert.True(simulation.TrySetFactoryProduction(factoryId, null));
        }

        AdvanceTicks(simulation, 70);
        Assert.Equal(1, simulation.World.Entities.Count(entity =>
            entity.IsAlive && entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id));
    }

    [Fact]
    public void BastionTemplate_AutofillSkipsLockedRecipeAndPicksLaterDeficit()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.LightBot, 1));
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 40);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 20);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, null));

        AdvanceTicks(simulation, 70);
        Assert.Contains(simulation.World.Entities, entity =>
            entity.IsAlive && entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id);
        Assert.DoesNotContain(simulation.World.Entities, entity => entity.Kind == EntityKind.LightBot);
    }

    [Fact]
    public void TrySetFactoryProduction_RejectsForeignBastionAssignment()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var enemyBastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(2) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.False(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, enemyBastion.Id));
        Assert.Null(simulation.World.GetEntity(factoryId)!.AssignedBastionId);
        Assert.Null(simulation.World.GetEntity(factoryId)!.ProductionTargetKind);
    }

    [Fact]
    public void TrySetFactoryProduction_RejectsOutputChangeWhileWorkInProgress()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        AdvanceTicks(simulation, 1);

        var factory = simulation.World.GetEntity(factoryId)!;
        Assert.True(factory.WorkTicksRemaining > 0);
        Assert.False(simulation.TrySetFactoryProduction(factoryId, null));
        Assert.Equal(EntityKind.BasicTank, factory.ProductionTargetKind);
    }

    [Fact]
    public void TryForceCompleteResearch_CompletesImmediatelyAndIsIdempotent()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);

        Assert.True(simulation.TryForceCompleteResearch(playerId, TechnologyId.LightBot, confirmExclusive: true));
        Assert.Contains(TechnologyId.LightBot, simulation.GetPlayer(playerId).Research.CompletedTechnologies);
        Assert.True(simulation.TryForceCompleteResearch(playerId, TechnologyId.LightBot, confirmExclusive: true));
        Assert.DoesNotContain(TechnologyId.LightBot, simulation.GetPlayer(playerId).Research.ProgressWorkUnits.Keys);
    }

    [Fact]
    public void Laboratory_ConsumesSciencePacksAndUnlocksResearch()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Laboratory, NearBlue(simulation, 5, -2), out var labId));
        AdvanceTicks(simulation, 30);

        simulation.AddItemToEntity(labId, ItemId.SciencePackT1, 3);
        Assert.True(simulation.TryStartResearch(new PlayerId(1), TechnologyId.LightBot));
        AdvanceTicks(simulation, 91);

        Assert.Contains(TechnologyId.LightBot, simulation.GetPlayer(new PlayerId(1)).ResearchedTechnologies);
    }

    [Fact]
    public void RefineryAndSmelter_ProduceT2Products()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        UnlockTier2ForTests(simulation, new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.SolarPanel, NearBlue(simulation, 6, 4), out _));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.SolarPanel, NearBlue(simulation, 7, 4), out _));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(simulation, 5, -2), out var smelterId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Refinery, NearBlue(simulation, 8, -2), out var refineryId));
        AdvanceTicks(simulation, 30);

        simulation.AddItemToEntity(smelterId, ItemId.IronPlate, 2);
        simulation.AddItemToEntity(smelterId, ItemId.Coal, 1);
        simulation.AddItemToEntity(refineryId, ItemId.CrudeOil, 1);
        AdvanceTicks(simulation, 31);

        Assert.Equal(1, simulation.World.GetEntity(smelterId)!.OutputBuffer.Count(ItemId.Steel));
        Assert.Equal(1, simulation.World.GetEntity(refineryId)!.OutputBuffer.Count(ItemId.Fuel));
    }

    [Fact]
    public void FogOfWarAndTechSignatures_AreTrackedPerPlayer()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerOne = new PlayerId(1);
        var blueCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == playerOne);
        var redCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(playerOne, blueCommander.Position));
        Assert.Equal(VisibilityState.Unknown, simulation.GetVisibility(playerOne, redCommander.Position));

        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(2), EntityKind.Smelter, NearBlue(simulation, 8, 0, playerId: 2), out _));
        AdvanceTicks(simulation, 31);

        Assert.Contains(simulation.GetTechSignatureHotspots(playerOne), hotspot => hotspot.Intensity > 0);
    }

    [Fact]
    public void TryRotateEntity_RotatesDirectedEntityCyclically()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, NearBlue(simulation, 7, 0), out var conveyorId));
        AdvanceTicks(simulation, 30);

        Assert.Equal(Direction.East, simulation.World.GetEntity(conveyorId)!.Direction);
        Assert.True(simulation.TryRotateEntity(conveyorId, clockwise: true));
        Assert.Equal(Direction.South, simulation.World.GetEntity(conveyorId)!.Direction);
        Assert.True(simulation.TryRotateEntity(conveyorId, clockwise: false));
        Assert.Equal(Direction.East, simulation.World.GetEntity(conveyorId)!.Direction);
    }

    [Fact]
    public void TryPlaceGhostBuildFromCommander_AppliesDirectionAndAssemblerRecipe()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));

        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Conveyor,
            NearBlue(simulation, 7, 0),
            out var conveyorGhostId,
            Direction.North));
        Assert.Equal(Direction.North, simulation.World.GetEntity(conveyorGhostId)!.Direction);
        AdvanceTicks(simulation, 30);
        Assert.Equal(Direction.North, simulation.World.GetEntity(conveyorGhostId)!.Direction);

        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Assembler,
            NearBlue(simulation, 2, 4),
            out var assemblerGhostId,
            Direction.East,
            ItemRecipeId.CopperWire));
        Assert.Equal(ItemRecipeId.CopperWire, simulation.World.GetEntity(assemblerGhostId)!.SelectedItemRecipe);
        AdvanceTicks(simulation, 30);
        Assert.Equal(ItemRecipeId.CopperWire, simulation.World.GetEntity(assemblerGhostId)!.SelectedItemRecipe);
    }

    [Fact]
    public void Inventory_AffordableSets_UsesBottleneckFromCommanderStock()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var mineCost = MvpDefinitions.BuildCosts[EntityKind.Mine];
        var expected = commander.Inventory.AffordableSets(mineCost);
        Assert.True(expected >= 1);
        Assert.Equal(commander.Inventory.Count(ItemId.IronPlate) / mineCost[ItemId.IronPlate], expected);
    }

    [Fact]
    public void TryQueueCommanderBuild_MovesCommanderUntilTargetIsInBuildRadius()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        UnlockTier2ForTests(simulation, new PlayerId(1));
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var coalTile = FindTerrain(simulation, TerrainType.Coal, tile => tile.X < simulation.World.Size.Width / 2);

        Assert.True(simulation.TryQueueCommanderBuild(commander.Id, EntityKind.CoalMine, coalTile));
        Assert.NotNull(commander.QueuedBuildOrder);

        AdvanceTicks(simulation, 900);

        Assert.Null(commander.QueuedBuildOrder);
        Assert.Contains(simulation.World.Entities, entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.CoalMine && entity.Position == coalTile);
    }

    [Fact]
    public void FootprintPlacement_BlocksOverlappingBuildings()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var ironTile = FindReachableTerrain(simulation, commander, TerrainType.IronOre);

        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, ironTile, out _));
        Assert.False(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Laboratory, new TilePosition(ironTile.X + 1, ironTile.Y + 1), out _));

        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out _));
        Assert.False(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(simulation, 4, 8), out _));
    }

    [Fact]
    public void PowerState_ExposesObjectAndGridEnergy()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var startingSolar = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.SolarPanel);

        Assert.Equal(5, MvpDefinitions.PowerProduction[EntityKind.SolarPanel]);
        Assert.Equal(5, simulation.GetPlayer(new PlayerId(1)).PowerProduced);
        Assert.Equal(0, MvpDefinitions.PowerDemand.GetValueOrDefault(startingSolar.Kind));
        Assert.Equal(TerrainType.Grass, simulation.World.GetTerrain(startingSolar.Position));
    }

    [Fact]
    public void OutputBuffer_IsLimitedByItemStackSize()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);

        Assert.True(simulation.TryAddOutputItemToEntity(bastion.Id, ItemId.IronPlate, MvpDefinitions.GetMaxStackSize(ItemId.IronPlate)));
        Assert.False(simulation.TryAddOutputItemToEntity(bastion.Id, ItemId.IronPlate, 1));
    }

    [Fact]
    public void Conveyor_AcceptsOnlyTwoItemsPerTile()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, NearBlue(simulation, 7, 0), out var conveyorId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.AddItemToEntity(conveyorId, ItemId.IronPlate, 1));
        Assert.True(simulation.AddItemToEntity(conveyorId, ItemId.CopperPlate, 1));
        Assert.False(simulation.AddItemToEntity(conveyorId, ItemId.Coal, 1));
        Assert.Equal(2, simulation.World.GetEntity(conveyorId)!.ConveyorItems.Count);
    }

    [Fact]
    public void Conveyor_MovesItemOnlyAfterConfiguredTicks()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, NearBlue(simulation, 7, 0), out var firstId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, NearBlue(simulation, 8, 0), out var secondId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.AddItemToEntity(firstId, ItemId.IronPlate, 1));
        AdvanceTicks(simulation, MvpDefinitions.ConveyorMoveTicks - 1);

        Assert.Single(simulation.World.GetEntity(firstId)!.ConveyorItems);
        Assert.Empty(simulation.World.GetEntity(secondId)!.ConveyorItems);

        AdvanceTicks(simulation, 1);

        Assert.Empty(simulation.World.GetEntity(firstId)!.ConveyorItems);
        Assert.Single(simulation.World.GetEntity(secondId)!.ConveyorItems);
    }

    [Fact]
    public void Inserter_HoldsOnlyOneItemUntilTransferCompletes()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, NearBlue(simulation, 2, 4), out var sourceHubId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Inserter, NearBlue(simulation, 3, 4), out var inserterId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TryAddOutputItemToEntity(sourceHubId, ItemId.IronPlate, 2));
        AdvanceTicks(simulation, 1);

        var inserter = simulation.World.GetEntity(inserterId)!;
        Assert.Equal(ItemId.IronPlate, inserter.HeldItem);
        Assert.Equal(1, simulation.World.GetEntity(sourceHubId)!.Inventory.Count(ItemId.IronPlate));
        AdvanceTicks(simulation, MvpDefinitions.InserterTransferTicks / 2);

        Assert.Equal(ItemId.IronPlate, inserter.HeldItem);
        Assert.Equal(1, simulation.World.GetEntity(sourceHubId)!.Inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void TryIssueMoveCommand_MovesCommanderTowardTarget()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var start = commander.Position;

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(start.X, start.Y - 3)));
        AdvanceTicks(simulation, MvpDefinitions.GetStats(EntityKind.Commander).MoveEveryTicks);

        Assert.Equal(new TilePosition(start.X, start.Y - 1), commander.Position);
    }

    [Fact]
    public void MoveCommand_UpdatesWorldPositionBeforeTilePosition()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var startTile = commander.Position;
        var startWorld = commander.WorldPosition;

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(startTile.X, startTile.Y - 3)));
        simulation.AdvanceTick();

        Assert.Equal(startTile, commander.Position);
        Assert.True(commander.WorldPosition.DistanceTo(startWorld) > 0);
        Assert.True(commander.WorldPosition.DistanceTo(WorldPosition.FromTileCenter(new TilePosition(startTile.X, startTile.Y - 1))) < startWorld.DistanceTo(WorldPosition.FromTileCenter(new TilePosition(startTile.X, startTile.Y - 1))));
    }

    [Fact]
    public void Pathfinding_UsesDiagonalStepWhenClear()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var start = commander.Position;

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(start.X + 3, start.Y - 3)));
        AdvanceTicks(simulation, 12);

        Assert.Equal(new TilePosition(start.X + 1, start.Y - 1), commander.Position);
    }

    [Fact]
    public void Pathfinding_GoesAroundLargeObstacle()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(simulation, 5, -1), out _));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, NearBlue(simulation, 8, 0)));
        AdvanceTicks(simulation, 90);

        Assert.Equal(NearBlue(simulation, 8, 0), commander.Position);
    }

    [Fact]
    public void Pathfinding_GhostBuildBlocksFutureFootprintAndRepaths()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, NearBlue(simulation, 8, 0)));
        AdvanceTicks(simulation, 2);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, NearBlue(simulation, 5, 0), out var blockerId));
        AdvanceTicks(simulation, 80);

        Assert.True(simulation.World.GetEntity(blockerId)!.IsAlive);
        Assert.NotEqual(NearBlue(simulation, 5, 0), commander.Position);
        Assert.Equal(NearBlue(simulation, 8, 0), commander.Position);
    }

    [Fact]
    public void Pathfinding_DoesNotCutDiagonalCorner()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, NearBlue(simulation, 5, 0), out _));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, NearBlue(simulation, 4, -1), out _));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, NearBlue(simulation, 5, -1)));
        AdvanceTicks(simulation, 16);

        Assert.NotEqual(NearBlue(simulation, 5, -1), commander.Position);
    }

    [Fact]
    public void StartingBastions_DoNotOverlapResourcePatches()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastions = simulation.World.Entities.Where(entity => entity.Kind == EntityKind.Bastion);
        var solars = simulation.World.Entities.Where(entity => entity.Kind == EntityKind.SolarPanel);

        foreach (var bastion in bastions)
        {
            foreach (var tile in GameWorld.GetFootprintTiles(bastion.Kind, bastion.Position))
            {
                Assert.Equal(TerrainType.Grass, simulation.World.GetTerrain(tile));
            }
        }

        foreach (var solar in solars)
        {
            Assert.Equal(TerrainType.Grass, simulation.World.GetTerrain(solar.Position));
            var bastion = bastions.Single(entity => entity.OwnerId == solar.OwnerId);
            var bastionTiles = GameWorld.GetFootprintTiles(bastion.Kind, bastion.Position).ToHashSet();
            var adjacent = bastionTiles.Any(tile =>
                Math.Max(Math.Abs(tile.X - solar.Position.X), Math.Abs(tile.Y - solar.Position.Y)) == 1);
            Assert.True(adjacent);
        }
    }

    [Fact]
    public void StartingSolar_IsMirroredAcrossMapForPvP()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blueSolar = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.SolarPanel && entity.OwnerId == new PlayerId(1));
        var redSolar = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.SolarPanel && entity.OwnerId == new PlayerId(2));

        Assert.Equal(blueSolar.Position.Y, redSolar.Position.Y);
        Assert.Equal(simulation.World.Size.Width - 1 - blueSolar.Position.X, redSolar.Position.X);
    }

    [Fact]
    public void GroundMovement_BlockedByBuildingsButNotLogistics()
    {
        var blockedSimulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blockedCommander = blockedSimulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(blockedSimulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(blockedSimulation, 4, -2), out _));
        AdvanceTicks(blockedSimulation, 30);

        Assert.True(blockedSimulation.TryIssueMoveCommand(blockedCommander.Id, NearBlue(blockedSimulation, 4, -2)));
        AdvanceTicks(blockedSimulation, 40);

        Assert.NotEqual(NearBlue(blockedSimulation, 4, -2), blockedCommander.Position);

        var passableSimulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var passableCommander = passableSimulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(passableSimulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, NearBlue(passableSimulation, 4, -1), out _));
        Assert.True(passableSimulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Inserter, NearBlue(passableSimulation, 4, -2), out _));
        AdvanceTicks(passableSimulation, 30);

        Assert.True(passableSimulation.TryIssueMoveCommand(passableCommander.Id, NearBlue(passableSimulation, 4, -3)));
        AdvanceTicks(passableSimulation, 32);

        Assert.Equal(NearBlue(passableSimulation, 4, -3), passableCommander.Position);
    }

    [Fact]
    public void SmelterFootprint_IsTwoByTwo()
    {
        Assert.Equal(new WorldSize(2, 2), MvpDefinitions.GetFootprint(EntityKind.Smelter));
    }

    [Fact]
    public void HubStorage_AcceptsTwentyStacksOnly()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var hub = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Hub);
        var maxIronStack = MvpDefinitions.GetMaxStackSize(ItemId.IronPlate);

        Assert.True(simulation.AddItemToEntity(hub.Id, ItemId.IronPlate, maxIronStack * MvpDefinitions.HubStorageStacks));
        Assert.Equal(MvpDefinitions.HubStorageStacks, hub.Inventory.TotalStacks);
        Assert.False(simulation.AddItemToEntity(hub.Id, ItemId.CopperPlate, 1));
    }

    [Fact]
    public void Assembler_ProducesIntermediateIngredientsAndSciencePacks()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, NearBlue(simulation, 2, 4), out var assemblerId));
        AdvanceTicks(simulation, 30);
        var assembler = simulation.World.GetEntity(assemblerId)!;

        ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.IronGear, (ItemId.IronPlate, 2));
        Assert.Equal(1, assembler.OutputBuffer.Count(ItemId.IronGear));

        ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.CopperWire, (ItemId.CopperPlate, 1));
        Assert.Equal(2, assembler.OutputBuffer.Count(ItemId.CopperWire));

        ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.Circuit, (ItemId.IronPlate, 1), (ItemId.CopperWire, 2));
        Assert.Equal(1, assembler.OutputBuffer.Count(ItemId.Circuit));

        ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.SciencePackT1, (ItemId.IronGear, 1), (ItemId.CopperPlate, 1));
        Assert.Equal(1, assembler.OutputBuffer.Count(ItemId.SciencePackT1));

        UnlockTier2ForTests(simulation, new PlayerId(1));
        ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.SciencePackT2, (ItemId.Circuit, 1), (ItemId.Steel, 1), (ItemId.Fuel, 1));
        Assert.Equal(1, assembler.OutputBuffer.Count(ItemId.SciencePackT2));
    }

    [Fact]
    public void Laboratory_ConsumesSciencePacksProducedByAssembler()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.SolarPanel, NearBlue(simulation, 6, 4), out _));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.SolarPanel, NearBlue(simulation, 7, 4), out _));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, NearBlue(simulation, 2, 4), out var assemblerId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Laboratory, NearBlue(simulation, 5, -2), out var labId));
        AdvanceTicks(simulation, 30);

        for (var i = 0; i < 3; i++)
        {
            ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.SciencePackT1, (ItemId.IronGear, 1), (ItemId.CopperPlate, 1));
        }

        var assembler = simulation.World.GetEntity(assemblerId)!;
        Assert.True(assembler.OutputBuffer.TryRemove(ItemId.SciencePackT1, 3));
        Assert.True(simulation.AddItemToEntity(labId, ItemId.SciencePackT1, 3));
        Assert.True(simulation.TryStartResearch(new PlayerId(1), TechnologyId.LightBot));
        AdvanceTicks(simulation, 91);

        Assert.Contains(TechnologyId.LightBot, simulation.GetPlayer(new PlayerId(1)).ResearchedTechnologies);
    }

    [Fact]
    public void TryCollectOutputBuffer_MovesItemsToCommanderInventory()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var initialIron = commander.Inventory.Count(ItemId.IronPlate);

        Assert.True(simulation.TryAddOutputItemToEntity(bastion.Id, ItemId.IronPlate, 5));
        Assert.True(simulation.TryCollectOutputBuffer(commander.Id, bastion.Id));

        Assert.Equal(initialIron + 5, commander.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(0, bastion.OutputBuffer.Count(ItemId.IronPlate));
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }

    private static void ProduceAssemblerRecipe(GameSimulation simulation, int assemblerId, ItemRecipeId recipeId, params (ItemId Item, int Amount)[] inputs)
    {
        foreach (var input in inputs)
        {
            Assert.True(simulation.AddItemToEntity(assemblerId, input.Item, input.Amount));
        }

        Assert.True(simulation.TrySetAssemblerRecipe(assemblerId, recipeId));
        AdvanceTicks(simulation, MvpDefinitions.ItemRecipes[recipeId].WorkTicks + 1);
    }

    private static void UnlockTier2ForTests(GameSimulation simulation, PlayerId playerId)
    {
        foreach (var technology in new[] { TechnologyId.ProductionI, TechnologyId.EnergyI, TechnologyId.CommandI })
        {
            Assert.True(simulation.TryForceCompleteResearch(playerId, technology));
            simulation.AdvanceTick();
        }

        Assert.Equal(ResearchTierIds.T2, simulation.GetPlayer(playerId).Research.CurrentTierId);
    }

    private static TilePosition NearBlue(GameSimulation simulation, int x, int yOffsetFromMid, int playerId = 1)
    {
        var midY = simulation.World.Size.Height / 2;
        if (playerId == 1)
        {
            return new TilePosition(x, midY + yOffsetFromMid);
        }

        // Mirror X for Red start side (left-half x → right-half).
        return new TilePosition(simulation.World.Size.Width - 1 - x, midY + yOffsetFromMid);
    }

    private static TilePosition FindTerrain(GameSimulation simulation, TerrainType type, Func<TilePosition, bool>? predicate = null)
    {
        for (var y = 0; y < simulation.World.Size.Height; y++)
        {
            for (var x = 0; x < simulation.World.Size.Width; x++)
            {
                var tile = new TilePosition(x, y);
                if (simulation.World.GetTerrain(tile) == type && (predicate?.Invoke(tile) ?? true))
                {
                    return tile;
                }
            }
        }

        throw new InvalidOperationException($"Terrain {type} not found.");
    }

    private static TilePosition FindReachableTerrain(GameSimulation simulation, WorldEntity commander, TerrainType type)
    {
        return FindTerrain(
            simulation,
            type,
            tile => Math.Abs(tile.X - commander.Position.X) + Math.Abs(tile.Y - commander.Position.Y) <= MvpDefinitions.CommanderBuildRadius);
    }

    private static TilePosition FindNearbyGrass(GameSimulation simulation, WorldEntity commander, TilePosition near)
    {
        for (var radius = 1; radius <= 6; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var tile = new TilePosition(near.X + dx, near.Y + dy);
                    if (!simulation.World.IsInside(tile) || simulation.World.GetTerrain(tile) != TerrainType.Grass)
                    {
                        continue;
                    }

                    if (simulation.World.GetEntitiesAt(tile).Any(entity => entity.IsAlive))
                    {
                        continue;
                    }

                    if (Math.Abs(tile.X - commander.Position.X) + Math.Abs(tile.Y - commander.Position.Y) > MvpDefinitions.CommanderBuildRadius)
                    {
                        continue;
                    }

                    return tile;
                }
            }
        }

        throw new InvalidOperationException("No nearby grass tile within build radius.");
    }

    private static IEnumerable<(int X, int Y, TerrainType Type)> EnumerateTerrain(GameSimulation simulation)
    {
        for (var y = 0; y < simulation.World.Size.Height; y++)
        {
            for (var x = 0; x < simulation.World.Size.Width; x++)
            {
                yield return (x, y, simulation.World.GetTerrain(new TilePosition(x, y)));
            }
        }
    }
}
