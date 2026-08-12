using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

public sealed class BoundActorSinkTests
{
    [Fact]
    public void BoundSink_RejectsMismatchedActor()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bound = new PlayerId(1);
        var sink = new BoundPlayerCommandSink(new DeferredCommandSink(simulation), bound);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == bound && entity.Kind == EntityKind.Commander);

        var mismatched = new IssueMoveCommand(
            new PlayerId(2),
            simulation.Tick + 1,
            commander.Id,
            new TilePosition(12, 10));

        Assert.Throws<InvalidOperationException>(() => sink.Enqueue(mismatched));
        Assert.Empty(simulation.PendingCommands);
    }

    [Fact]
    public void BoundSink_AcceptsMatchingActor()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bound = new PlayerId(1);
        var sink = new BoundPlayerCommandSink(new DeferredCommandSink(simulation), bound);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == bound && entity.Kind == EntityKind.Commander);

        var scheduled = sink.Enqueue(new IssueMoveCommand(
            bound,
            0,
            commander.Id,
            new TilePosition(12, 10)));

        Assert.Equal(bound, scheduled.Actor);
        Assert.True(scheduled.Sequence >= 1);
        Assert.Single(simulation.PendingCommands);
    }
}
