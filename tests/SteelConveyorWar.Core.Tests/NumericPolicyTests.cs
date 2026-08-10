namespace SteelConveyorWar.Core.Tests;

public sealed class NumericPolicyTests
{
    [Fact]
    public void WorldPosition_DistanceSquared_MatchesDistanceSquared()
    {
        var a = new WorldPosition(1.25, 4.5);
        var b = new WorldPosition(-2.75, 0.5);
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        Assert.Equal(dx * dx + dy * dy, a.DistanceSquaredTo(b));
        Assert.Equal(Math.Sqrt(a.DistanceSquaredTo(b)), a.DistanceTo(b));
    }

    [Fact]
    public void EnergyFillRatioComparer_UsesIntegerCrossMultiply_ThenEntityId()
    {
        // 1/3 < 1/2
        Assert.True(EnergyFillRatioComparer.CompareRatios(1, 3, 9, 1, 2, 1) < 0);
        // 2/4 == 1/2 → lower id wins
        Assert.True(EnergyFillRatioComparer.CompareRatios(2, 4, 1, 1, 2, 2) < 0);
        Assert.True(EnergyFillRatioComparer.CompareRatios(2, 4, 5, 1, 2, 2) > 0);
        // Equal everything
        Assert.Equal(0, EnergyFillRatioComparer.CompareRatios(3, 10, 7, 3, 10, 7));
    }

    [Fact]
    public void EnergyFill_LowerIntegerRatioBeatsHigherRatio()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, NearBlue(simulation, 5, -2), out var assemblerId));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(simulation, 8, -2), out var smelterId));
        AdvanceTicks(simulation, 30);

        var assembler = simulation.World.GetEntity(assemblerId)!;
        var smelter = simulation.World.GetEntity(smelterId)!;
        Assert.True(simulation.TrySetEnergyBufferForTests(assemblerId, assembler.EnergyBufferCapacity / 10));
        Assert.True(simulation.TrySetEnergyBufferForTests(smelterId, smelter.EnergyBufferCapacity / 2));

        var beforeAssembler = assembler.EnergyBuffer;
        var beforeSmelter = smelter.EnergyBuffer;
        AdvanceTicks(simulation, 1);
        Assert.True(assembler.EnergyBuffer > beforeAssembler);
        Assert.Equal(beforeSmelter, smelter.EnergyBuffer);
    }

    [Fact]
    public void MovementPathDualRun_HashesMatch_WithIntegerPathCosts()
    {
        var hashA = RunMove(seed: 7, ticks: 240);
        var hashB = RunMove(seed: 7, ticks: 240);
        Assert.Equal(hashA, hashB);
    }

    private static string RunMove(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new PlayerId(1), new TilePosition(18, 14)));
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

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
        var bastion = simulation.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        return new TilePosition(bastion.Position.X + dx, bastion.Position.Y + dy);
    }
}
