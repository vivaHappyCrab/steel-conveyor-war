namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R17: factory/bastion accounting uses reusable scratch buffers with live mid-tick updates.
/// Dual-run state hashes must stay identical for factory production and bastion order scenarios.
/// </summary>
public sealed class FactoryBastionAccountingTests
{
    [Fact]
    public void FactoryProductionScenario_DualRun_PreservesStateHash()
    {
        var hashA = RunFactoryProductionScenario(seed: 42);
        var hashB = RunFactoryProductionScenario(seed: 42);
        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length);
    }

    [Fact]
    public void BastionOrdersScenario_DualRun_PreservesStateHash()
    {
        var hashA = RunBastionOrdersScenario(seed: 99);
        var hashB = RunBastionOrdersScenario(seed: 99);
        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length);
    }

    [Fact]
    public void MidTickSpawn_StillAttributesSupplyDeterministically()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryA));
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 6, 6), out var factoryB));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, playerId, EntityKind.BasicTank, 2));
        foreach (var factoryId in new[] { factoryA, factoryB })
        {
            Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
            simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 40);
            simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 20);
            Assert.True(simulation.TrySetFactoryProduction(factoryId, playerId, EntityKind.BasicTank));
        }

        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 8);

        Assert.Equal(2, simulation.World.Entities.Count(entity =>
            entity.IsAlive && entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastion.Id));
        Assert.Equal(2, simulation.GetBastionUnitSupply(bastion.Id, EntityKind.BasicTank));
    }

    private static string RunFactoryProductionScenario(int seed)
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: seed);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 40);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 20);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, playerId, EntityKind.BasicTank, 2));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, playerId, EntityKind.BasicTank));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks * 2 + 20);

        return simulation.ComputeStateHash();
    }

    private static string RunBastionOrdersScenario(int seed)
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: seed);
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, playerId, EntityKind.BasicTank, 1));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, playerId, EntityKind.BasicTank));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);

        var target = NearBlue(simulation, 12, 0);
        Assert.True(simulation.TryIssueBastionOrder(
            bastion.Id,
            playerId,
            new BastionOrder(BastionOrderKind.AttackArea, target)));
        AdvanceTicks(simulation, 120);

        return simulation.ComputeStateHash();
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }

    private static TilePosition NearBlue(GameSimulation simulation, int dx, int dy)
    {
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        return new TilePosition(bastion.Position.X + dx, bastion.Position.Y + dy);
    }
}
