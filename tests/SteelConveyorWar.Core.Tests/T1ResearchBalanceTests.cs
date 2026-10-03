using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class T1ResearchBalanceTests
{
    [Fact]
    public void EnergyI_RaisesSolarOutputAndBufferFactor()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Mine, GrassNear(simulation, 8, 0), player, out var mineId));
        Assert.Equal(2 * MvpDefinitions.EnergyBufferCapacityFactor, simulation.World.GetEntity(mineId)!.EnergyBufferCapacity);

        Assert.True(simulation.TryForceCompleteResearch(player, TechnologyId.EnergyI, confirmExclusive: true));
        simulation.AdvanceTick();

        Assert.Equal(8, simulation.GetPlayer(player).PowerProduced);
        Assert.Equal(2 * 150, simulation.World.GetEntity(mineId)!.EnergyBufferCapacity);
    }

    [Fact]
    public void PowerReserve_DoublesLabAndFactoryBuffers_NotMines()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TryForceCompleteResearch(player, TechnologyId.EnergyI, confirmExclusive: true));
        Assert.True(simulation.TryForceCompleteResearch(player, new TechnologyId("technology.t1.power-reserve"), confirmExclusive: true));

        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Laboratory, GrassNear(simulation, 6, 0), player, out var labId));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.TankFactory, GrassNear(simulation, 8, 2), player, out var factoryId));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Mine, GrassNear(simulation, 10, 0), player, out var mineId));

        Assert.Equal(4 * 300, simulation.World.GetEntity(labId)!.EnergyBufferCapacity);
        Assert.Equal(5 * 300, simulation.World.GetEntity(factoryId)!.EnergyBufferCapacity);
        Assert.Equal(2 * 150, simulation.World.GetEntity(mineId)!.EnergyBufferCapacity);
    }

    [Fact]
    public void ProductionI_CutsFactoryWorkToThreeQuarters()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.Equal(60, simulation.ResolveStat(player, ResearchStatIds.FactoryWorkTicks, 60));
        Assert.True(simulation.TryForceCompleteResearch(player, TechnologyId.ProductionI, confirmExclusive: true));
        Assert.Equal(45, simulation.ResolveStat(player, ResearchStatIds.FactoryWorkTicks, 60));
    }

    [Fact]
    public void CommandI_ExtendsBuildRadiusAndSpeedsAllConstruction()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TryForceCompleteResearch(player, TechnologyId.CommandI, confirmExclusive: true));
        Assert.Equal(16, simulation.GetCommanderBuildRadius(player));

        var commander = BlueCommander(simulation);
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id, EntityKind.Conveyor, GrassNear(simulation, 3, 1), out var ghostId, actor: player));
        Assert.Equal(24, simulation.World.GetEntity(ghostId)!.ConstructionTicksRemaining);
    }

    [Fact]
    public void PrefabFortifications_DiscountWallsAndTurretsOnly()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TryForceCompleteResearch(player, new TechnologyId("technology.t1.prefab-fortifications"), confirmExclusive: true));

        var commander = BlueCommander(simulation);
        Assert.Equal(12, simulation.GetCommanderBuildRadius(player));
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id, EntityKind.Wall, GrassNear(simulation, 3, 1), out var wallGhostId, actor: player));
        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id, EntityKind.Conveyor, GrassNear(simulation, 5, 1), out var beltGhostId, actor: player));

        Assert.Equal(12, simulation.World.GetEntity(wallGhostId)!.ConstructionTicksRemaining);
        Assert.Equal(30, simulation.World.GetEntity(beltGhostId)!.ConstructionTicksRemaining);
        Assert.Equal(1, simulation.ResolveStat(player, ResearchStatIds.BuildIronDiscount, 0, EntityKind.Wall.ToString(), minValue: 0));
        Assert.Equal(0, simulation.ResolveStat(player, ResearchStatIds.BuildIronDiscount, 0, EntityKind.Mine.ToString(), minValue: 0));
        Assert.Equal(
            MvpDefinitions.GetStats(EntityKind.LightBot).AttackCooldownTicks,
            simulation.ResolveStat(player, ResearchStatIds.AttackCooldownTicks, MvpDefinitions.GetStats(EntityKind.LightBot).AttackCooldownTicks, EntityKind.LightBot.ToString()));
    }

    [Fact]
    public void FastRegroup_ShortensMoveTicks_NotFactoryWork()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TryForceCompleteResearch(player, new TechnologyId("technology.t1.fast-regroup"), confirmExclusive: true));

        Assert.Equal(3, simulation.ResolveStat(player, ResearchStatIds.MoveEveryTicks, 5, EntityKind.LightBot.ToString(), minValue: 3));
        Assert.Equal(7, simulation.ResolveStat(player, ResearchStatIds.MoveEveryTicks, 9, EntityKind.BasicTank.ToString(), minValue: 3));
        Assert.Equal(60, simulation.ResolveStat(player, ResearchStatIds.FactoryWorkTicks, 60, EntityKind.LightBot.ToString()));
    }

    [Fact]
    public void MobileGroups_BuffsLightAndMediumBotsOnly()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TryForceCompleteResearch(player, new TechnologyId("technology.t1.doctrine.mobile-groups"), confirmExclusive: true));

        Assert.Equal(6, simulation.ResolveStat(player, ResearchStatIds.AttackDamage, 5, EntityKind.LightBot.ToString(), minValue: 0));
        Assert.Equal(11, simulation.ResolveStat(player, ResearchStatIds.AttackDamage, 10, EntityKind.MediumBot.ToString(), minValue: 0));
        Assert.Equal(14, simulation.ResolveStat(player, ResearchStatIds.AttackDamage, 14, EntityKind.BasicTank.ToString(), minValue: 0));
        Assert.Equal(3, simulation.ResolveStat(player, ResearchStatIds.MoveEveryTicks, 5, EntityKind.LightBot.ToString(), minValue: 3));
        Assert.Equal(3, simulation.ResolveStat(player, ResearchStatIds.MoveEveryTicks, 4, EntityKind.MediumBot.ToString(), minValue: 3));
        Assert.Equal(9, simulation.ResolveStat(player, ResearchStatIds.MoveEveryTicks, 9, EntityKind.BasicTank.ToString(), minValue: 3));
    }

    [Fact]
    public void FortifiedLine_StrengthensWallsNotEconomyBuildings()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, GrassNear(simulation, 4, 1), player, out var wallId));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Smelter, GrassNear(simulation, 6, 1), player, out var smelterId));
        Assert.True(simulation.TryForceCompleteResearch(player, new TechnologyId("technology.t1.doctrine.fortified-line"), confirmExclusive: true));

        Assert.Equal(252, simulation.World.GetEntity(wallId)!.MaxHealth);
        Assert.Equal(120, simulation.World.GetEntity(smelterId)!.MaxHealth);
        Assert.Equal(15, simulation.ResolveStat(player, ResearchStatIds.ConstructionTicks, 30, EntityKind.Wall.ToString(), minValue: 1));
        Assert.Equal(30, simulation.ResolveStat(player, ResearchStatIds.ConstructionTicks, 30, EntityKind.Mine.ToString(), minValue: 1));
    }

    [Fact]
    public void MachineGunTurret_UnlocksWithoutGlobalAttackBonus()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TryForceCompleteResearch(player, TechnologyId.MachineGunTurret, confirmExclusive: true));

        Assert.Contains(EntityKind.MachineGunTurret.ToString(), simulation.GetPlayer(player).Research.UnlockedEntityKinds);
        Assert.Equal(5, simulation.ResolveStat(player, ResearchStatIds.AttackDamage, 5, EntityKind.LightBot.ToString(), minValue: 0));
    }

    [Fact]
    public void T1Effort_UsesThreeMinutePillars()
    {
        var catalog = MvpResearchCatalog.CreateEmbedded();
        Assert.Equal(180, catalog.Technologies[TechnologyId.ProductionI].Cost.EffortUnits);
        Assert.Equal(180, catalog.Technologies[TechnologyId.EnergyI].Cost.EffortUnits);
        Assert.Equal(180, catalog.Technologies[TechnologyId.CommandI].Cost.EffortUnits);
        Assert.Equal(120, catalog.Technologies[TechnologyId.ImprovedConveyors].Cost.EffortUnits);
        Assert.Equal(120, catalog.Technologies[TechnologyId.GroundUnitAttack].Cost.EffortUnits);
        Assert.Equal(90, catalog.Technologies[new TechnologyId("technology.t1.power-reserve")].Cost.EffortUnits);
    }

    [Fact]
    public void SecondLaboratory_AwardsHalfAPointPerCycle()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, TechnologyId.ProductionI));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Laboratory, GrassNear(simulation, 4, 0), player, out _));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Laboratory, GrassNear(simulation, 6, 0), player, out var secondId));
        var second = simulation.World.GetEntity(secondId)!;
        Assert.True(simulation.TrySetEnergyBufferForTests(secondId, second.EnergyBufferCapacity));
        simulation.AddItemToEntity(secondId, ItemId.SciencePackT1, 2);

        Advance(simulation, ResearchSystem.LabCycleTicks);
        var research = simulation.GetPlayer(player).Research;
        Assert.Equal(0, research.ProgressWorkUnits.GetValueOrDefault(TechnologyId.ProductionI));
        Assert.Equal(5_000, research.ProgressRemainderBasisPoints[TechnologyId.ProductionI]);

        Assert.True(simulation.TrySetEnergyBufferForTests(secondId, second.EnergyBufferCapacity));
        Advance(simulation, ResearchSystem.LabCycleTicks);
        Assert.Equal(1, research.ProgressWorkUnits[TechnologyId.ProductionI]);
        Assert.False(research.ProgressRemainderBasisPoints.ContainsKey(TechnologyId.ProductionI));
    }

    [Fact]
    public void LightBot_StepsByMoveEveryTicks()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 7);
        var player = new PlayerId(1);
        Assert.True(TryFindClearRow(simulation, 8, out var lightStart));
        Assert.True(TryFindClearRow(simulation, 8, out var tankStart, skipY: lightStart.Y));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.LightBot, lightStart, player, out var lightId));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.BasicTank, tankStart, player, out var tankId));

        var light = simulation.World.GetEntity(lightId)!;
        var tank = simulation.World.GetEntity(tankId)!;
        light.Order = new BastionOrder(BastionOrderKind.AttackArea, new TilePosition(lightStart.X + 6, lightStart.Y));
        tank.Order = new BastionOrder(BastionOrderKind.AttackArea, new TilePosition(tankStart.X + 6, tankStart.Y));
        light.IsGarrisoned = false;
        tank.IsGarrisoned = false;
        var lightOrigin = light.WorldPosition;
        var tankOrigin = tank.WorldPosition;

        Advance(simulation, 5);

        Assert.Equal(1_000, light.WorldPosition.X - lightOrigin.X);
        Assert.Equal(555, tank.WorldPosition.X - tankOrigin.X);
    }

    private static WorldEntity BlueCommander(GameSimulation simulation) =>
        simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));

    private static TilePosition GrassNear(GameSimulation simulation, int dx, int dy)
    {
        var commander = BlueCommander(simulation);
        var tile = new TilePosition(commander.Position.X + dx, commander.Position.Y + dy);
        if (!simulation.World.IsInside(tile))
        {
            throw new InvalidOperationException($"Tile {tile} is outside the map.");
        }

        return tile;
    }

    private static bool TryFindClearRow(GameSimulation simulation, int length, out TilePosition start, int? skipY = null)
    {
        var world = simulation.World;
        for (var y = 2; y < world.Size.Height - 2; y++)
        {
            if (skipY == y)
            {
                continue;
            }

            for (var x = 8; x < world.Size.Width / 2 - length; x++)
            {
                var clear = true;
                for (var i = 0; i < length; i++)
                {
                    var tile = new TilePosition(x + i, y);
                    if (!world.IsInside(tile)
                        || world.GetTerrain(tile) != TerrainType.Grass
                        || world.GetEntitiesAt(tile).Any(entity => entity.IsAlive))
                    {
                        clear = false;
                        break;
                    }
                }

                if (clear)
                {
                    start = new TilePosition(x, y);
                    return true;
                }
            }
        }

        start = default;
        return false;
    }

    private static void Advance(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }
}
