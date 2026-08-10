namespace SteelConveyorWar.Core.Tests;

public sealed class DeterminismHashTests
{
    [Fact]
    public void SameSeedIdleRuns_ProduceIdenticalHash()
    {
        var hashA = RunIdle(seed: 42, ticks: 120);
        var hashB = RunIdle(seed: 42, ticks: 120);
        Assert.Equal(hashA, hashB);
        Assert.False(string.IsNullOrWhiteSpace(hashA));
        Assert.Equal(64, hashA.Length);
    }

    [Fact]
    public void SameSeedCommandRuns_ProduceIdenticalHash()
    {
        var hashA = RunWithMove(seed: 42, ticks: 180);
        var hashB = RunWithMove(seed: 42, ticks: 180);
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void SameSeedQueuedCommandRuns_ProduceIdenticalHash()
    {
        var hashA = RunWithQueuedMove(seed: 42, ticks: 180);
        var hashB = RunWithQueuedMove(seed: 42, ticks: 180);
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentHashes()
    {
        var hashA = RunIdle(seed: 42, ticks: 30);
        var hashB = RunIdle(seed: 43, ticks: 30);
        // RandomSeed is part of the hash surface even when terrain ignores it today.
        Assert.NotEqual(hashA, hashB);
    }

    private static string RunIdle(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }

    private static string RunWithMove(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(12, 10)));
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }

    private static string RunWithQueuedMove(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
        simulation.EnqueueForNextTick(tick => new Commands.IssueMoveCommand(
            new PlayerId(1), tick, commander.Id, new TilePosition(12, 10)));
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }
}
