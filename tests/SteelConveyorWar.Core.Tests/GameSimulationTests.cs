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

        Assert.Contains(simulation.World.Entities, entity => entity.Kind == EntityKind.Commander);
        Assert.Equal(2, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.Commander));
        Assert.Equal(TerrainType.IronOre, simulation.World.GetTerrain(new TilePosition(7, 7)));
        Assert.Equal(TerrainType.CopperOre, simulation.World.GetTerrain(new TilePosition(7, 13)));
        Assert.Equal(TerrainType.IronOre, simulation.World.GetTerrain(new TilePosition(40, 7)));
        Assert.Equal(TerrainType.CopperOre, simulation.World.GetTerrain(new TilePosition(40, 13)));
        Assert.Equal(TerrainType.Coal, simulation.World.GetTerrain(new TilePosition(20, 8)));
        Assert.Equal(TerrainType.Oil, simulation.World.GetTerrain(new TilePosition(20, 19)));
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

        var placed = simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, new TilePosition(7, 7), out var ghostId);
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, new TilePosition(7, 7), out var mineId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, new TilePosition(10, 8), out var smelterId));
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, new TilePosition(2, 18), out var sourceHubId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Inserter, new TilePosition(3, 18), out var inserterId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, new TilePosition(4, 18), out var conveyorId));
        AdvanceTicks(simulation, 30);

        simulation.World.GetEntity(inserterId)!.Direction = Direction.East;
        simulation.World.GetEntity(conveyorId)!.Direction = Direction.East;
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, new TilePosition(2, 20), out var factoryId));
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, new TilePosition(2, 20), out var factoryId));
        AdvanceTicks(simulation, 30);

        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, EntityKind.BasicTank, 1));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        simulation.World.GetEntity(factoryId)!.ProductionTargetKind = null;
        AdvanceTicks(simulation, 70);

        Assert.Contains(simulation.World.Entities, entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id);
    }

    [Fact]
    public void Laboratory_ConsumesSciencePacksAndUnlocksResearch()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Laboratory, new TilePosition(5, 12), out var labId));
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, new TilePosition(5, 12), out var smelterId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Refinery, new TilePosition(8, 12), out var refineryId));
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
        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(playerOne, new TilePosition(4, 14)));
        Assert.Equal(VisibilityState.Unknown, simulation.GetVisibility(playerOne, new TilePosition(43, 14)));

        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(2), EntityKind.Smelter, new TilePosition(36, 14), out _));
        AdvanceTicks(simulation, 31);

        Assert.Contains(simulation.GetTechSignatureHotspots(playerOne), hotspot => hotspot.Intensity > 0);
    }

    [Fact]
    public void TryRotateEntity_RotatesDirectedEntityCyclically()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, new TilePosition(7, 14), out var conveyorId));
        AdvanceTicks(simulation, 30);

        Assert.Equal(Direction.East, simulation.World.GetEntity(conveyorId)!.Direction);
        Assert.True(simulation.TryRotateEntity(conveyorId, clockwise: true));
        Assert.Equal(Direction.South, simulation.World.GetEntity(conveyorId)!.Direction);
        Assert.True(simulation.TryRotateEntity(conveyorId, clockwise: false));
        Assert.Equal(Direction.East, simulation.World.GetEntity(conveyorId)!.Direction);
    }

    [Fact]
    public void TryQueueCommanderBuild_MovesCommanderUntilTargetIsInBuildRadius()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        UnlockTier2ForTests(simulation, new PlayerId(1));
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));

        Assert.True(simulation.TryQueueCommanderBuild(commander.Id, EntityKind.CoalMine, new TilePosition(20, 8)));
        Assert.NotNull(commander.QueuedBuildOrder);

        AdvanceTicks(simulation, 140);

        Assert.Null(commander.QueuedBuildOrder);
        Assert.Contains(simulation.World.Entities, entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.CoalMine && entity.Position == new TilePosition(20, 8));
    }

    [Fact]
    public void FootprintPlacement_BlocksOverlappingBuildings()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);

        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Mine, new TilePosition(7, 7), out _));
        Assert.False(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Laboratory, new TilePosition(8, 8), out _));

        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, new TilePosition(2, 20), out _));
        Assert.False(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, new TilePosition(4, 22), out _));
    }

    [Fact]
    public void PowerState_ExposesObjectAndGridEnergy()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);

        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.SolarPanel, new TilePosition(3, 18), out var solarId));
        AdvanceTicks(simulation, 31);

        Assert.Equal(5, MvpDefinitions.PowerProduction[EntityKind.SolarPanel]);
        Assert.Equal(5, simulation.GetPlayer(new PlayerId(1)).PowerProduced);
        Assert.Equal(0, MvpDefinitions.PowerDemand.GetValueOrDefault(simulation.World.GetEntity(solarId)!.Kind));
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, new TilePosition(7, 14), out var conveyorId));
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, new TilePosition(7, 14), out var firstId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, new TilePosition(8, 14), out var secondId));
        AdvanceTicks(simulation, 30);

        simulation.World.GetEntity(firstId)!.Direction = Direction.East;
        simulation.World.GetEntity(secondId)!.Direction = Direction.East;
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, new TilePosition(2, 18), out var sourceHubId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Inserter, new TilePosition(3, 18), out var inserterId));
        AdvanceTicks(simulation, 30);

        simulation.World.GetEntity(inserterId)!.Direction = Direction.East;
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

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(commander.Position.X + 3, commander.Position.Y - 3)));
        AdvanceTicks(simulation, 12);

        Assert.Equal(new TilePosition(5, 13), commander.Position);
    }

    [Fact]
    public void Pathfinding_GoesAroundLargeObstacle()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, new TilePosition(5, 13), out _));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(8, 14)));
        AdvanceTicks(simulation, 90);

        Assert.Equal(new TilePosition(8, 14), commander.Position);
    }

    [Fact]
    public void Pathfinding_GhostBuildBlocksFutureFootprintAndRepaths()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(8, 14)));
        AdvanceTicks(simulation, 2);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, new TilePosition(5, 14), out var blockerId));
        AdvanceTicks(simulation, 80);

        Assert.True(simulation.World.GetEntity(blockerId)!.IsAlive);
        Assert.NotEqual(new TilePosition(5, 14), commander.Position);
        Assert.Equal(new TilePosition(8, 14), commander.Position);
    }

    [Fact]
    public void Pathfinding_DoesNotCutDiagonalCorner()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, new TilePosition(5, 14), out _));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Hub, new TilePosition(4, 13), out _));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(5, 13)));
        AdvanceTicks(simulation, 16);

        Assert.NotEqual(new TilePosition(5, 13), commander.Position);
    }

    [Fact]
    public void StartingBastions_DoNotOverlapResourcePatches()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastions = simulation.World.Entities.Where(entity => entity.Kind == EntityKind.Bastion);

        foreach (var bastion in bastions)
        {
            foreach (var tile in GameWorld.GetFootprintTiles(bastion.Kind, bastion.Position))
            {
                Assert.Equal(TerrainType.Grass, simulation.World.GetTerrain(tile));
            }
        }
    }

    [Fact]
    public void GroundMovement_BlockedByBuildingsButNotLogistics()
    {
        var blockedSimulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blockedCommander = blockedSimulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(blockedSimulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, new TilePosition(4, 12), out _));
        AdvanceTicks(blockedSimulation, 30);

        Assert.True(blockedSimulation.TryIssueMoveCommand(blockedCommander.Id, new TilePosition(4, 12)));
        AdvanceTicks(blockedSimulation, 40);

        Assert.NotEqual(new TilePosition(4, 12), blockedCommander.Position);

        var passableSimulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var passableCommander = passableSimulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(passableSimulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Conveyor, new TilePosition(4, 13), out _));
        Assert.True(passableSimulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Inserter, new TilePosition(4, 12), out _));
        AdvanceTicks(passableSimulation, 30);

        Assert.True(passableSimulation.TryIssueMoveCommand(passableCommander.Id, new TilePosition(4, 11)));
        AdvanceTicks(passableSimulation, 32);

        Assert.Equal(new TilePosition(4, 11), passableCommander.Position);
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, new TilePosition(2, 18), out var assemblerId));
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
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, new TilePosition(2, 18), out var assemblerId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Laboratory, new TilePosition(5, 12), out var labId));
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
            Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(playerId, technology));
            simulation.GetPlayer(playerId).Research.ProgressWorkUnits[technology] =
                simulation.ResearchCatalog.Technologies[technology].Cost.EffortUnits;
            simulation.AdvanceTick();
        }

        Assert.Equal(ResearchTierIds.T2, simulation.GetPlayer(playerId).Research.CurrentTierId);
    }
}
