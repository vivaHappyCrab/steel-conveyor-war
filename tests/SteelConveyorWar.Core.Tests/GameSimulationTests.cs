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
        Assert.True(simulation.TrySetEnergyBufferForTests(mineId, int.MaxValue));
        Assert.True(simulation.TrySetEnergyBufferForTests(smelterId, int.MaxValue));

        AdvanceTicks(simulation, 30);
        Assert.True(simulation.World.GetEntity(mineId)!.OutputBuffer.Count(ItemId.IronOre) > 0);

        simulation.AddItemToEntity(smelterId, ItemId.IronOre, 1);
        Assert.True(simulation.TrySetEnergyBufferForTests(smelterId, int.MaxValue));
        AdvanceTicks(simulation, 41);

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
    public void Factory_ProducesUnitAssignedToDeficitBastion()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank));
        Assert.Null(simulation.World.GetEntity(factoryId)!.AssignedBastionId);
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);

        Assert.Contains(simulation.World.Entities, entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id);
    }

    [Fact]
    public void BastionTemplate_FactoryAutofillsMissingUnitsAcrossBastions()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 10);

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
    public void GetBastionUnitSupply_CountsLivingAssignedUnitsAndInFlightProduction()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.Equal(0, simulation.GetBastionUnitSupply(bastion.Id, EntityKind.BasicTank));

        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);
        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 12);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 4);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 2));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank));
        simulation.AdvanceTick();
        Assert.True(simulation.World.GetEntity(factoryId)!.WorkTicksRemaining > 0);
        Assert.Equal(1, simulation.GetBastionUnitSupply(bastion.Id, EntityKind.BasicTank));

        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);
        Assert.Equal(1, simulation.GetBastionUnitSupply(bastion.Id, EntityKind.BasicTank));
        Assert.Contains(simulation.World.Entities, entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 12);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 4);
        simulation.AdvanceTick();
        Assert.Equal(2, simulation.GetBastionUnitSupply(bastion.Id, EntityKind.BasicTank));
    }

    [Fact]
    public void IsUnitProductionUnlocked_MatchesFactoryRecipeGates()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        Assert.True(simulation.IsUnitProductionUnlocked(playerId, EntityKind.BasicTank));
        Assert.False(simulation.IsUnitProductionUnlocked(playerId, EntityKind.LightBot));
        Assert.True(simulation.TryForceCompleteResearch(playerId, TechnologyId.LightBot, confirmExclusive: true));
        Assert.True(simulation.IsUnitProductionUnlocked(playerId, EntityKind.LightBot));
    }

    [Fact]
    public void TryPlaceGhostBuildFromCommander_PaysRemainingCostFromNearbyOwnedHubs()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Commander);
        var hub = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Hub);
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(hub.Id, ItemId.IronPlate, 1));
        Assert.Equal(1, hub.Inventory.Count(ItemId.IronPlate));

        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Conveyor,
            NearBlue(simulation, 7, 0),
            out var ghostId));
        Assert.NotEqual(0, ghostId);
        Assert.Equal(0, commander.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(0, hub.Inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void TryPlaceGhostBuildFromCommander_DoesNotPartialSpendWhenCombinedStockInsufficient()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Commander);
        var hub = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Hub);
        ClearInventory(commander.Inventory);
        Assert.True(commander.Inventory.TryAddWithinTotalStackLimit(ItemId.IronPlate, 1, 40));
        Assert.True(simulation.AddItemToEntity(hub.Id, ItemId.IronPlate, 1));
        // Inserter costs 2 iron + 1 copper; commander+hub have only 2 iron and no copper.
        Assert.False(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Inserter,
            NearBlue(simulation, 7, 0),
            out _));
        Assert.Equal(1, commander.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(1, hub.Inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void TryPlaceGhostBuildFromCommander_IgnoresHubsOutsideInteractRadius()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Commander);
        Assert.True(commander.Inventory.TryAddWithinTotalStackLimit(ItemId.IronPlate, 20, 100));
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Hub,
            NearBlue(simulation, 11, 2),
            out var farGhostId));
        AdvanceTicks(simulation, 30);
        var farHub = simulation.World.GetEntity(farGhostId)!;
        Assert.Equal(EntityKind.Hub, farHub.Kind);

        ClearInventory(commander.Inventory);
        var nearHub = simulation.World.Entities.Single(entity =>
            entity.OwnerId == playerId && entity.Kind == EntityKind.Hub && entity.Id != farHub.Id);
        ClearInventory(nearHub.Inventory);
        Assert.True(simulation.AddItemToEntity(farHub.Id, ItemId.IronPlate, 1));
        Assert.False(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Conveyor,
            NearBlue(simulation, 7, 0),
            out _));
        Assert.Equal(1, farHub.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(0, nearHub.Inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void TryPlaceGhostBuildFromCommander_SpendsNearbyHubsInAscendingEntityIdOrder()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Commander);
        var startHub = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Hub);
        ClearInventory(commander.Inventory);
        Assert.True(commander.Inventory.TryAddWithinTotalStackLimit(ItemId.IronPlate, 20, 40));
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Hub,
            NearBlue(simulation, 6, 0),
            out var secondHubGhostId));
        AdvanceTicks(simulation, 30);
        var secondHub = simulation.World.GetEntity(secondHubGhostId)!;
        Assert.Equal(EntityKind.Hub, secondHub.Kind);

        ClearInventory(commander.Inventory);
        ClearInventory(startHub.Inventory);
        ClearInventory(secondHub.Inventory);
        Assert.True(simulation.AddItemToEntity(startHub.Id, ItemId.IronPlate, 1));
        Assert.True(simulation.AddItemToEntity(secondHub.Id, ItemId.IronPlate, 1));

        var ordered = new[] { startHub, secondHub }.OrderBy(hub => hub.Id).ToArray();
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Conveyor,
            NearBlue(simulation, 7, 0),
            out _));
        Assert.Equal(0, ordered[0].Inventory.Count(ItemId.IronPlate));
        Assert.Equal(1, ordered[1].Inventory.Count(ItemId.IronPlate));
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
    public void TrySetFactoryProduction_ManualRecipe_ContinuesAfterSpawn_WhileUnderTemplate()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 2));
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 40);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 20);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);

        var factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(EntityKind.BasicTank, factory.ProductionTargetKind);
        Assert.True(factory.IsManualProductionTarget);
        Assert.Null(factory.AssignedBastionId);
        Assert.Equal(1, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id));

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);
        Assert.Equal(2, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id));
        Assert.Equal(EntityKind.BasicTank, simulation.World.GetEntity(factoryId)!.ProductionTargetKind);
    }

    [Fact]
    public void TrySetFactoryProduction_ManualRecipe_PausesAtTemplateAndResumesAfterDeath()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 80);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 40);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);

        Assert.Equal(1, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.BasicTank && entity.OwnerId == playerId));
        var factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(EntityKind.BasicTank, factory.ProductionTargetKind);
        Assert.Equal(0, factory.WorkTicksRemaining);

        // At cap: another full craft window must not create a second tank.
        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        var ironBefore = factory.InputBuffer.Count(ItemId.IronPlate);
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);
        factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(1, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.BasicTank && entity.OwnerId == playerId));
        Assert.Equal(0, factory.WorkTicksRemaining);
        Assert.Equal(ironBefore, factory.InputBuffer.Count(ItemId.IronPlate));

        var tank = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.BasicTank && entity.OwnerId == playerId);
        simulation.DamageEntity(tank.Id, tank.Health);
        AdvanceTicks(simulation, 1);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);
        Assert.Equal(1, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.BasicTank && entity.OwnerId == playerId && entity.IsAlive));
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
    public void TrySetFactoryProduction_IgnoresBastionIdArgument()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        var factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(EntityKind.BasicTank, factory.ProductionTargetKind);
        Assert.Null(factory.AssignedBastionId);
        Assert.True(factory.IsManualProductionTarget);

        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.LightBot));
        factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(EntityKind.LightBot, factory.ProductionTargetKind);
        Assert.Null(factory.AssignedBastionId);
    }

    [Fact]
    public void TryAssignFactoryBastion_IsObsoleteNoOp()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.False(simulation.TryAssignFactoryBastion(factoryId, bastion.Id));
        Assert.Null(simulation.World.GetEntity(factoryId)!.AssignedBastionId);
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
            Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
            simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
            simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        }

        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 10);
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
        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 40);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 20);

        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 10);
        Assert.Contains(simulation.World.Entities, entity =>
            entity.IsAlive && entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id);
        Assert.DoesNotContain(simulation.World.Entities, entity => entity.Kind == EntityKind.LightBot);
    }

    [Fact]
    public void TrySetFactoryProduction_AcceptsObsoleteBastionIdWithoutAssignment()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var enemyBastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(2) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, enemyBastion.Id));
        Assert.Null(simulation.World.GetEntity(factoryId)!.AssignedBastionId);
        Assert.Equal(EntityKind.BasicTank, simulation.World.GetEntity(factoryId)!.ProductionTargetKind);
    }

    [Fact]
    public void TrySetFactoryProduction_RejectsOutputChangeWhileWorkInProgress()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank));
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

        Assert.True(simulation.TrySetEnergyBufferForTests(labId, int.MaxValue));
        simulation.AddItemToEntity(labId, ItemId.SciencePackT1, 30);
        Assert.True(simulation.TryStartResearch(new PlayerId(1), TechnologyId.LightBot));
        AdvanceTicks(simulation, ResearchSystem.LabCycleTicks * 30);

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

        Assert.True(simulation.TrySetEnergyBufferForTests(smelterId, int.MaxValue));
        Assert.True(simulation.TrySetEnergyBufferForTests(refineryId, int.MaxValue));
        simulation.AddItemToEntity(smelterId, ItemId.IronPlate, 2);
        simulation.AddItemToEntity(smelterId, ItemId.Coal, 1);
        simulation.AddItemToEntity(refineryId, ItemId.CrudeOil, 1);
        AdvanceTicks(simulation, 61);

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
        Assert.True(simulation.TryRotateEntity(conveyorId, new PlayerId(1), clockwise: true));
        Assert.Equal(Direction.South, simulation.World.GetEntity(conveyorId)!.Direction);
        Assert.True(simulation.TryRotateEntity(conveyorId, new PlayerId(1), clockwise: false));
        Assert.Equal(Direction.East, simulation.World.GetEntity(conveyorId)!.Direction);
    }

    [Fact]
    public void TryRotateEntity_RejectsEnemyOwnedDirectedBuilding()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, NearBlue(simulation, 7, 0), out var conveyorId));
        AdvanceTicks(simulation, 30);

        Assert.False(simulation.TryRotateEntity(conveyorId, new PlayerId(2), clockwise: true));
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
            ItemRecipeId.Composite));
        Assert.Equal(ItemRecipeId.Composite, simulation.World.GetEntity(assemblerGhostId)!.SelectedItemRecipe);
        AdvanceTicks(simulation, 30);
        Assert.Equal(ItemRecipeId.Composite, simulation.World.GetEntity(assemblerGhostId)!.SelectedItemRecipe);
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
        // Use 1x1 solar panels so the diagonal corner stays a single-tile choke (hubs are 2x2).
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.SolarPanel, NearBlue(simulation, 5, 0), out _));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.SolarPanel, NearBlue(simulation, 4, -1), out _));
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

        ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.Composite, (ItemId.IronPlate, 1), (ItemId.CopperPlate, 1));
        Assert.Equal(1, assembler.OutputBuffer.Count(ItemId.Composite));

        ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.SciencePackT1, (ItemId.IronGear, 1), (ItemId.CopperPlate, 1));
        Assert.Equal(1, assembler.OutputBuffer.Count(ItemId.SciencePackT1));

        UnlockTier2ForTests(simulation, new PlayerId(1));
        ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.SciencePackT2, (ItemId.Composite, 1), (ItemId.Steel, 1), (ItemId.Fuel, 1));
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

        for (var i = 0; i < 30; i++)
        {
            ProduceAssemblerRecipe(simulation, assemblerId, ItemRecipeId.SciencePackT1, (ItemId.IronGear, 1), (ItemId.CopperPlate, 1));
        }

        var assembler = simulation.World.GetEntity(assemblerId)!;
        Assert.True(assembler.OutputBuffer.TryRemove(ItemId.SciencePackT1, 30));
        Assert.True(simulation.AddItemToEntity(labId, ItemId.SciencePackT1, 30));
        Assert.True(simulation.TryStartResearch(new PlayerId(1), TechnologyId.LightBot));
        AdvanceTicks(simulation, ResearchSystem.LabCycleTicks * 30);

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

    [Fact]
    public void TryWithdrawFromHubOrOutput_WithdrawsHubInventory()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var hub = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Hub);
        var initialIron = commander.Inventory.Count(ItemId.IronPlate);

        Assert.True(simulation.AddItemToEntity(hub.Id, ItemId.IronPlate, 7));
        Assert.True(simulation.TryWithdrawFromHubOrOutput(commander.Id, hub.Id));

        Assert.Equal(initialIron + 7, commander.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(0, hub.Inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void TryWithdrawFromHubOrOutput_CollectsResourceOutputBuffer()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var initialOre = commander.Inventory.Count(ItemId.IronOre);

        Assert.True(simulation.TryAddOutputItemToEntity(bastion.Id, ItemId.IronOre, 4));
        Assert.True(simulation.TryWithdrawFromHubOrOutput(commander.Id, bastion.Id));

        Assert.Equal(initialOre + 4, commander.Inventory.Count(ItemId.IronOre));
        Assert.Equal(0, bastion.OutputBuffer.Count(ItemId.IronOre));
    }

    [Fact]
    public void TryDepositToHubOrInput_DepositsIntoHub()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var hub = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Hub);
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.CopperPlate, 6));
        var hubBefore = hub.Inventory.Count(ItemId.CopperPlate);

        Assert.True(simulation.TryDepositToHubOrInput(commander.Id, hub.Id));

        Assert.Equal(0, commander.Inventory.Count(ItemId.CopperPlate));
        Assert.Equal(hubBefore + 6, hub.Inventory.Count(ItemId.CopperPlate));
    }

    [Fact]
    public void TryDepositToHubOrInput_DepositsIntoBuildingInput()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Laboratory, NearBlue(simulation, 5, -2), out var labId));
        AdvanceTicks(simulation, 30);
        var lab = simulation.World.GetEntity(labId)!;
        Assert.True(simulation.TryIssueMoveCommand(commander.Id, lab.Position));
        AdvanceTicks(simulation, 240);
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.SciencePackT1, 3));

        Assert.True(simulation.TryDepositToHubOrInput(commander.Id, labId));

        Assert.Equal(0, commander.Inventory.Count(ItemId.SciencePackT1));
        Assert.Equal(3, lab.InputBuffer.Count(ItemId.SciencePackT1));
    }

    [Fact]
    public void TryDepositToHubOrInput_RejectsWhenBuildingHasNoRecipe()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, NearBlue(simulation, 5, -2), out var assemblerId));
        AdvanceTicks(simulation, 30);
        var assembler = simulation.World.GetEntity(assemblerId)!;
        Assert.Null(assembler.SelectedItemRecipe);
        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, assembler.Position));
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, 4));

        Assert.False(simulation.TryDepositToHubOrInput(commander.Id, assemblerId));
        Assert.Equal(4, commander.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(0, assembler.InputBuffer.Count(ItemId.IronPlate));
    }

    [Fact]
    public void Smelter_StickyRecipe_FiltersDepositAndSwitchesOnNewInput()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(simulation, 5, -2), out var smelterId));
        AdvanceTicks(simulation, 30);
        Assert.True(simulation.TrySetEnergyBufferForTests(smelterId, int.MaxValue));
        var smelter = simulation.World.GetEntity(smelterId)!;
        Assert.Null(smelter.ActiveSmeltRecipe);
        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, smelter.Position));
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronOre, 2));
        Assert.False(simulation.TryDepositToHubOrInput(commander.Id, smelterId));

        simulation.AddItemToEntity(smelterId, ItemId.IronOre, 1);
        AdvanceTicks(simulation, 41);
        Assert.Equal(SmeltRecipeId.IronPlate, smelter.ActiveSmeltRecipe);
        Assert.Equal(1, smelter.OutputBuffer.Count(ItemId.IronPlate));

        Assert.True(simulation.TryDepositToHubOrInput(commander.Id, smelterId));
        Assert.Equal(0, commander.Inventory.Count(ItemId.IronOre));
        Assert.True(smelter.InputBuffer.Count(ItemId.IronOre) >= 1);

        // Empty input keeps sticky recipe; copper ore in buffer switches recipe.
        while (smelter.InputBuffer.Count(ItemId.IronOre) > 0)
        {
            smelter.InputBuffer.TryRemove(ItemId.IronOre, 1);
        }

        Assert.Equal(SmeltRecipeId.IronPlate, smelter.ActiveSmeltRecipe);
        simulation.AddItemToEntity(smelterId, ItemId.CopperOre, 1);
        AdvanceTicks(simulation, 41);
        Assert.Equal(SmeltRecipeId.CopperPlate, smelter.ActiveSmeltRecipe);
        Assert.Equal(1, smelter.OutputBuffer.Count(ItemId.CopperPlate));
    }

    [Fact]
    public void EnergyBuffer_IdleDoesNotDrain_EmptyPausesWork_EmptiestFirstFills()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, NearBlue(simulation, 5, -2), out var assemblerId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(simulation, 8, -2), out var smelterId));
        AdvanceTicks(simulation, 30);
        var assembler = simulation.World.GetEntity(assemblerId)!;
        var smelter = simulation.World.GetEntity(smelterId)!;
        Assert.Equal(MvpDefinitions.GetEnergyBufferCapacity(EntityKind.Assembler), assembler.EnergyBufferCapacity);
        var filled = assembler.EnergyBuffer;
        AdvanceTicks(simulation, 10);
        Assert.True(assembler.EnergyBuffer >= filled);

        Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, assembler.EnergyBufferCapacity));
        Assert.True(simulation.TrySetEnergyBufferForTests(smelterId, 0));
        var beforeSmelter = smelter.EnergyBuffer;
        var beforeAssembler = assembler.EnergyBuffer;
        AdvanceTicks(simulation, 1);
        Assert.True(smelter.EnergyBuffer > beforeSmelter);
        Assert.Equal(beforeAssembler, assembler.EnergyBuffer);

        var solar = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.SolarPanel);
        Assert.True(simulation.TrySetEntityHealthForTests(solar.Id, 0));

        Assert.True(simulation.TrySetAssemblerRecipe(assemblerId, ItemRecipeId.IronGear));
        Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, 0));
        simulation.AddItemToEntity(assemblerId, ItemId.IronPlate, 2);
        AdvanceTicks(simulation, 1);
        Assert.True(assembler.WorkTicksRemaining > 0);
        var remaining = assembler.WorkTicksRemaining;
        AdvanceTicks(simulation, 5);
        Assert.Equal(remaining, assembler.WorkTicksRemaining);

        Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, int.MaxValue));
        AdvanceTicks(simulation, remaining + 1);
        Assert.Equal(1, assembler.OutputBuffer.Count(ItemId.IronGear));
    }

    [Fact]
    public void EnergyStats_RecordsActualDrain_NotInstalledDemandWhenStarved()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, NearBlue(simulation, 5, -2), out var assemblerId));
        AdvanceTicks(simulation, 30);

        var solar = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.SolarPanel);
        Assert.True(simulation.TrySetEntityHealthForTests(solar.Id, 0));

        Assert.True(simulation.TrySetAssemblerRecipe(assemblerId, ItemRecipeId.IronGear));
        simulation.AddItemToEntity(assemblerId, ItemId.IronPlate, 40);
        var demand = MvpDefinitions.GetPowerDemand(EntityKind.Assembler);
        Assert.True(demand > 0);

        Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, int.MaxValue));
        simulation.AdvanceTick(); // start craft
        Assert.True(simulation.World.GetEntity(assemblerId)!.WorkTicksRemaining > 0);
        // Fill a full 1s display bucket with working drains.
        for (var i = 0; i < GameSimulation.TicksPerSecond; i++)
        {
            Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, int.MaxValue));
            simulation.AdvanceTick();
        }

        var working = simulation.GetPlayer(new PlayerId(1)).EnergyStats.Query(10);
        Assert.Equal(1, EnergyStatsHistory.DisplayBucketSeconds(10));
        Assert.Equal(demand, working.DemandSeries[^1]);
        var assemblerWorking = working.ConsumerRows.Single(row => row.Kind == EntityKind.Assembler);
        Assert.Equal(demand, assemblerWorking.Series[^1]);

        Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, 0));
        for (var i = 0; i < GameSimulation.TicksPerSecond; i++)
        {
            Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, 0));
            simulation.AdvanceTick();
        }

        var starved = simulation.GetPlayer(new PlayerId(1)).EnergyStats.Query(10);
        Assert.Equal(0, starved.DemandSeries[^1]);
        Assert.DoesNotContain(starved.ConsumerRows, row => row.Kind == EntityKind.Assembler && row.Series[^1] > 0);
        Assert.True(simulation.World.GetEntity(assemblerId)!.WorkTicksRemaining > 0);
    }

    [Fact]
    public void EnergyStats_Query_UsesFixedAbsoluteBuckets()
    {
        Assert.Equal(1, EnergyStatsHistory.DisplayBucketSeconds(10));
        Assert.Equal(5, EnergyStatsHistory.DisplayBucketSeconds(300));
        Assert.Equal(10, EnergyStatsHistory.DisplayBucketSeconds(600));

        var history = new EnergyStatsHistory();
        var empty = new Dictionary<EntityKind, int>();
        var tps = GameSimulation.TicksPerSecond;

        // Ticks 0..29 → bucket 0 avg 1; 30..59 → bucket 1 avg 9. Mid-bucket noise must not rewrite bucket 0.
        for (long tick = 0; tick < tps; tick++)
        {
            history.Record(tick, 1, 1, empty, empty);
        }

        var afterFirst = history.Query(10);
        Assert.Equal(1, afterFirst.SampleCount);
        Assert.Equal(1, afterFirst.DemandSeries[0]);

        for (long tick = tps; tick < tps + tps / 2; tick++)
        {
            history.Record(tick, 100, 100, empty, empty);
        }

        var midSecond = history.Query(10);
        Assert.Equal(1, midSecond.SampleCount);
        Assert.Equal(1, midSecond.DemandSeries[0]); // completed bucket unchanged

        for (long tick = tps + tps / 2; tick < 2 * tps; tick++)
        {
            history.Record(tick, 9, 9, empty, empty);
        }

        var afterSecond = history.Query(10);
        Assert.Equal(2, afterSecond.SampleCount);
        Assert.Equal(1, afterSecond.DemandSeries[0]);
        // Second bucket = 15 ticks of 100 + 15 ticks of 9 → avg 54.5 → 54 or 55
        Assert.InRange(afterSecond.DemandSeries[1], 54, 55);
    }

    [Fact]
    public void EnergyStats_Query_UsesLargerBucketsForLongWindows()
    {
        var history = new EnergyStatsHistory();
        var empty = new Dictionary<EntityKind, int>();
        for (long tick = 0; tick < 10 * GameSimulation.TicksPerSecond; tick++)
        {
            history.Record(tick, 5, 4, empty, empty);
        }

        var shortWindow = history.Query(10);
        Assert.Equal(10, shortWindow.SampleCount);
        Assert.All(shortWindow.DemandSeries, value => Assert.Equal(4, value));

        for (long tick = 10 * GameSimulation.TicksPerSecond; tick < 5 * 60 * GameSimulation.TicksPerSecond; tick++)
        {
            history.Record(tick, 5, 4, empty, empty);
        }

        var fiveMin = history.Query(300);
        Assert.Equal(60, fiveMin.SampleCount);
        Assert.All(fiveMin.DemandSeries, value => Assert.Equal(4, value));
    }

    [Fact]
    public void Mine_DrivesWorkTicksAndConsumesEnergyOnCycleComplete()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var ironTile = FindReachableTerrain(simulation, commander, TerrainType.IronOre);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, ironTile, out var mineId));
        AdvanceTicks(simulation, 30);

        var mine = simulation.World.GetEntity(mineId)!;
        Assert.True(simulation.TrySetEnergyBufferForTests(mineId, int.MaxValue));
        var beforeOre = mine.OutputBuffer.Count(ItemId.IronOre);
        simulation.AdvanceTick();
        Assert.Equal(MvpDefinitions.OreMineWorkTicks, mine.WorkTicksTotal);
        Assert.True(mine.WorkTicksRemaining > 0);
        Assert.True(mine.WorkTicksRemaining < MvpDefinitions.OreMineWorkTicks);

        var energyBefore = mine.EnergyBuffer;
        AdvanceTicks(simulation, mine.WorkTicksRemaining);
        Assert.Equal(beforeOre + 1, mine.OutputBuffer.Count(ItemId.IronOre));
        Assert.True(mine.EnergyBuffer < energyBefore);
    }

    [Fact]
    public void Mine_FullOutput_DoesNotDrainEnergy()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var ironTile = FindReachableTerrain(simulation, commander, TerrainType.IronOre);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, ironTile, out var mineId));
        AdvanceTicks(simulation, 30);

        var mine = simulation.World.GetEntity(mineId)!;
        var maxStack = MvpDefinitions.GetMaxStackSize(ItemId.IronOre);
        var missing = maxStack - mine.OutputBuffer.Count(ItemId.IronOre);
        if (missing > 0)
        {
            Assert.True(simulation.TryAddOutputItemToEntity(mineId, ItemId.IronOre, missing));
        }

        Assert.Equal(maxStack, mine.OutputBuffer.Count(ItemId.IronOre));

        var solar = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.SolarPanel);
        Assert.True(simulation.TrySetEntityHealthForTests(solar.Id, 0));
        Assert.True(simulation.TrySetEnergyBufferForTests(mineId, 100));

        var before = mine.EnergyBuffer;
        AdvanceTicks(simulation, MvpDefinitions.OreMineWorkTicks);
        Assert.Equal(before, mine.EnergyBuffer);
        Assert.Equal(maxStack, mine.OutputBuffer.Count(ItemId.IronOre));
        Assert.Equal(0, mine.WorkTicksRemaining);
        Assert.Equal(0, mine.WorkTicksTotal);
    }

    [Fact]
    public void Inserters_RoundRobinExtractFromSameSource()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var ironTile = FindReachableTerrain(simulation, commander, TerrainType.IronOre);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, ironTile, out var mineId));
        AdvanceTicks(simulation, 30);

        var mine = simulation.World.GetEntity(mineId)!;
        Assert.True(simulation.TrySetEnergyBufferForTests(mineId, int.MaxValue));
        Assert.True(simulation.TryAddOutputItemToEntity(mineId, ItemId.IronOre, 20));

        var footprint = MvpDefinitions.GetFootprint(EntityKind.Mine);
        var eastInserterPos = new TilePosition(mine.Position.X + footprint.Width, mine.Position.Y);
        var westInserterPos = new TilePosition(mine.Position.X - 1, mine.Position.Y);
        var eastHubPos = new TilePosition(eastInserterPos.X + 1, eastInserterPos.Y);
        var westHubPos = new TilePosition(westInserterPos.X - 2, westInserterPos.Y);

        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Inserter, eastInserterPos, out var eastInserterId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Inserter, westInserterPos, out var westInserterId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, eastHubPos, out var eastHubId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, westHubPos, out var westHubId));
        AdvanceTicks(simulation, 30);

        // East inserter already faces East (drop east / pull west from mine).
        // West inserter must face West (drop west / pull east from mine).
        Assert.True(simulation.TryRotateEntity(westInserterId, new PlayerId(1), clockwise: true));
        Assert.True(simulation.TryRotateEntity(westInserterId, new PlayerId(1), clockwise: true));
        Assert.Equal(Direction.West, simulation.World.GetEntity(westInserterId)!.Direction);

        AdvanceTicks(simulation, (MvpDefinitions.InserterTransferTicks + 2) * 12);

        var eastCount = simulation.World.GetEntity(eastHubId)!.Inventory.Count(ItemId.IronOre);
        var westCount = simulation.World.GetEntity(westHubId)!.Inventory.Count(ItemId.IronOre);
        Assert.True(eastCount > 0, $"east hub got {eastCount}");
        Assert.True(westCount > 0, $"west hub got {westCount}");
    }

    [Fact]
    public void HubFootprint_IsTwoByTwo_AndStartingHubsDoNotOverlap()
    {
        Assert.Equal(new WorldSize(2, 2), MvpDefinitions.GetFootprint(EntityKind.Hub));
        Assert.True(MvpDefinitions.HasPlayerInventory(EntityKind.Hub));
        Assert.True(MvpDefinitions.HasPlayerInventory(EntityKind.Commander));
        Assert.False(MvpDefinitions.HasPlayerInventory(EntityKind.BasicTank));

        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        foreach (var hub in simulation.World.Entities.Where(entity => entity.Kind == EntityKind.Hub))
        {
            var hubTiles = GameWorld.GetFootprintTiles(hub.Kind, hub.Position).ToHashSet();
            foreach (var other in simulation.World.Entities.Where(entity => entity.Id != hub.Id && entity.IsAlive))
            {
                var otherTiles = GameWorld.GetFootprintTiles(other.Kind, other.Position);
                Assert.Empty(hubTiles.Intersect(otherTiles));
            }
        }
    }

    [Fact]
    public void TryDepositAndWithdrawItemType_MovesAllOfType()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var hub = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Hub);
        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, hub.Position));
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, 7));
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.CopperPlate, 4));

        Assert.True(simulation.TryDepositItemTypeToHubOrInput(commander.Id, hub.Id, ItemId.IronPlate));
        Assert.Equal(0, commander.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(4, commander.Inventory.Count(ItemId.CopperPlate));
        Assert.Equal(7, hub.Inventory.Count(ItemId.IronPlate));

        Assert.True(simulation.TryWithdrawItemTypeFromHubOrOutput(commander.Id, hub.Id, ItemId.IronPlate));
        Assert.Equal(7, commander.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(0, hub.Inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void Assembler_DefaultsToNoRecipe()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, NearBlue(simulation, 5, -2), out var assemblerId));
        AdvanceTicks(simulation, 30);
        Assert.Null(simulation.World.GetEntity(assemblerId)!.SelectedItemRecipe);
    }

    [Fact]
    public void TryWithdrawFromHubOrOutput_FailsOutsideInteractRadius()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, NearBlue(simulation, 8, 0), out var farHubId));
        AdvanceTicks(simulation, 30);
        Assert.True(simulation.AddItemToEntity(farHubId, ItemId.IronPlate, 2));

        Assert.False(simulation.TryWithdrawFromHubOrOutput(commander.Id, farHubId));
        Assert.Equal(2, simulation.World.GetEntity(farHubId)!.Inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void TryWithdrawFromHubOrOutput_RejectsEnemyHub()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var enemyHub = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(2) && entity.Kind == EntityKind.Hub);
        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, enemyHub.Position));
        Assert.True(simulation.AddItemToEntity(enemyHub.Id, ItemId.IronPlate, 5));

        Assert.False(simulation.TryWithdrawFromHubOrOutput(commander.Id, enemyHub.Id));
        Assert.Equal(5, enemyHub.Inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void TryDepositToHubOrInput_RejectsEnemyHub()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var enemyHub = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(2) && entity.Kind == EntityKind.Hub);
        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, enemyHub.Position));
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.CopperPlate, 4));
        var hubBefore = enemyHub.Inventory.Count(ItemId.CopperPlate);

        Assert.False(simulation.TryDepositToHubOrInput(commander.Id, enemyHub.Id));
        Assert.Equal(4, commander.Inventory.Count(ItemId.CopperPlate));
        Assert.Equal(hubBefore, enemyHub.Inventory.Count(ItemId.CopperPlate));
    }

    [Fact]
    public void EntityStats_CombatStubs_DefaultArmorProjectileSplash()
    {
        var stats = MvpDefinitions.GetStats(EntityKind.LightBot);
        Assert.Equal(0, stats.Armor);
        Assert.Equal(ProjectileKind.GroundToGround, stats.ProjectileKind);
        Assert.Equal(0, stats.SplashRadius);
        Assert.True(stats.VisionRadius > 0);
        Assert.True(stats.AttackDamage > 0);
    }

    [Fact]
    public void Demolish_RefundsHalfCostAndReturnsBuffers()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == player);
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, 15));

        var tile = NearBlue(simulation, 6, 0);
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(commander.Id, EntityKind.Smelter, tile, out var smelterId));
        AdvanceTicks(simulation, 30);
        var smelter = simulation.World.GetEntity(smelterId)!;
        Assert.Equal(EntityKind.Smelter, smelter.Kind);
        Assert.True(simulation.AddItemToEntity(smelterId, ItemId.IronOre, 3));
        Assert.True(simulation.TryAddOutputItemToEntity(smelterId, ItemId.IronPlate, 2));

        ClearInventory(commander.Inventory);
        Assert.True(simulation.TryDemolishBuilding(commander.Id, smelterId));
        AdvanceTicks(simulation, 1);

        Assert.Null(simulation.World.GetEntity(smelterId));
        // Half of BuildCosts Smelter = 15/2 = 7 iron plate + 2 from output + 3 ore from input
        Assert.Equal(9, commander.Inventory.Count(ItemId.IronPlate));
        Assert.Equal(3, commander.Inventory.Count(ItemId.IronOre));
    }

    [Fact]
    public void Demolish_OverflowGoesToHubThenDiscard()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == player);
        var hub = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Hub && entity.OwnerId == player);
        ClearInventory(commander.Inventory);
        ClearInventory(hub.Inventory);

        var maxIron = MvpDefinitions.GetMaxStackSize(ItemId.IronPlate);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, maxIron));
        // Fill hub completely with copper so iron overflow cannot deposit (and iron-only refund discards).
        var hubCap = maxIron * MvpDefinitions.HubStorageStacks;
        Assert.True(simulation.AddItemToEntity(hub.Id, ItemId.CopperPlate, hubCap));

        ClearInventory(commander.Inventory);
        // Leave commander one below max so half-cost (7) fits partially? Use conveyor cost 1 → refund 0, put plates in buffer.
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, 1));
        var tile = NearBlue(simulation, 7, 2);
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(commander.Id, EntityKind.Conveyor, tile, out var beltId));
        AdvanceTicks(simulation, 30);
        Assert.True(simulation.AddItemToEntity(beltId, ItemId.IronPlate, 1));
        Assert.True(simulation.AddItemToEntity(beltId, ItemId.IronPlate, 1));

        // Fill commander to stack cap so belt items + refund must go to hub / discard.
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, maxIron));
        var hubIronBefore = hub.Inventory.Count(ItemId.IronPlate);

        Assert.True(simulation.TryDemolishBuilding(commander.Id, beltId));
        AdvanceTicks(simulation, 1);

        Assert.Equal(maxIron, commander.Inventory.Count(ItemId.IronPlate));
        // Hub full of copper — iron discarded; refund for conveyor is 1/2=0.
        Assert.Equal(hubIronBefore, hub.Inventory.Count(ItemId.IronPlate));
        Assert.Null(simulation.World.GetEntity(beltId));
    }

    [Fact]
    public void Demolish_RejectsBastionAndEnemy()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == player);
        var bastion = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Bastion && entity.OwnerId == player);
        Assert.False(simulation.TryDemolishBuilding(commander.Id, bastion.Id));

        var enemyHub = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Hub && entity.OwnerId == new PlayerId(2));
        Assert.False(simulation.IsDemolishableTarget(commander.Id, enemyHub.Id));
        Assert.False(simulation.TryDemolishBuilding(commander.Id, enemyHub.Id));
    }

    [Fact]
    public void StopCommander_ClearsQueuedBuildDemolishAndMove()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == player);
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, 40));
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.CopperPlate, 20));

        var farTile = NearBlue(simulation, 20, 0);
        Assert.True(simulation.TryQueueCommanderBuild(commander.Id, EntityKind.Smelter, farTile));
        Assert.NotNull(commander.QueuedBuildOrder);

        Assert.True(simulation.TryStopCommander(commander.Id));
        Assert.Null(commander.QueuedBuildOrder);
        Assert.Null(commander.QueuedDemolishOrder);
        Assert.Null(commander.MoveTarget);
        Assert.Empty(commander.MovementPath);
        Assert.Null(commander.CurrentWaypoint);

        var nearTile = NearBlue(simulation, 7, -1);
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(commander.Id, EntityKind.Conveyor, nearTile, out var targetId));
        AdvanceTicks(simulation, 30);
        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, NearBlue(simulation, 4, 20)));
        Assert.True(simulation.TryQueueCommanderDemolish(commander.Id, targetId));
        Assert.NotNull(commander.QueuedDemolishOrder);
        Assert.True(simulation.TryStopCommander(commander.Id));
        Assert.Null(commander.QueuedDemolishOrder);
        Assert.NotNull(simulation.World.GetEntity(targetId));
    }

    [Fact]
    public void SmelterAndRecipes_UseBalancedWorkTicks()
    {
        Assert.Equal(40, MvpDefinitions.ItemRecipes[ItemRecipeId.IronGear].WorkTicks);
        Assert.Equal(60, MvpDefinitions.ItemRecipes[ItemRecipeId.Composite].WorkTicks);

        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == player);
        ClearInventory(commander.Inventory);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, 40));
        var tile = NearBlue(simulation, 6, -2);
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(commander.Id, EntityKind.Smelter, tile, out var smelterId));
        AdvanceTicks(simulation, 30);
        Assert.True(simulation.TrySetEnergyBufferForTests(smelterId, int.MaxValue));
        Assert.True(simulation.AddItemToEntity(smelterId, ItemId.IronOre, 1));
        simulation.AdvanceTick();
        var smelter = simulation.World.GetEntity(smelterId)!;
        Assert.Equal(40, smelter.WorkTicksTotal);
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }

    private static void ClearInventory(Inventory inventory)
    {
        foreach (var pair in inventory.Items.ToList())
        {
            Assert.True(inventory.TryRemove(pair.Key, pair.Value));
        }
    }

    private static void ProduceAssemblerRecipe(GameSimulation simulation, int assemblerId, ItemRecipeId recipeId, params (ItemId Item, int Amount)[] inputs)
    {
        Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, int.MaxValue));
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
