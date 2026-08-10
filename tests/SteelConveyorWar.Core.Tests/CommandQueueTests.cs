using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

public sealed class CommandQueueTests
{
    [Fact]
    public void EnqueueCommand_AppliesAtStartOfMatchingAdvanceTick_BeforeSystems()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 7);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == player && entity.Kind == EntityKind.Commander);

        simulation.EnqueueForNextTick(tick => new IssueMoveCommand(
            player, tick, commander.Id, new TilePosition(12, 10)));

        Assert.Single(simulation.PendingCommands);
        Assert.Null(simulation.World.GetEntity(commander.Id)!.MoveTarget);

        simulation.AdvanceTick();

        Assert.Empty(simulation.PendingCommands);
        Assert.Equal(new TilePosition(12, 10), simulation.World.GetEntity(commander.Id)!.MoveTarget);
        Assert.Equal(1, simulation.Tick);
    }

    [Fact]
    public void SameQueuedCommands_ProduceIdenticalHash()
    {
        var hashA = RunQueuedMove(seed: 42, ticks: 180);
        var hashB = RunQueuedMove(seed: 42, ticks: 180);
        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length);
    }

    [Fact]
    public void ImmediateTryMove_AndQueuedMove_SameTickSchedule_MatchHash()
    {
        // Legacy Try* applies between ticks; queued path applies at start of next AdvanceTick.
        // Matching schedule: enqueue for tick 1, then AdvanceTick N times vs Try* then AdvanceTick N times.
        var queued = RunQueuedMove(seed: 11, ticks: 90);
        var immediate = RunImmediateMove(seed: 11, ticks: 90);
        Assert.Equal(immediate, queued);
    }

    [Fact]
    public void Serializer_RoundTripsIssueMoveAndBastionOrder()
    {
        var move = new IssueMoveCommand(new PlayerId(1), 5, 3, new TilePosition(8, 9));
        var roundTripMove = SimulationCommandSerializer.Deserialize(SimulationCommandSerializer.Serialize(move));
        Assert.Equal(move, roundTripMove);

        var order = new IssueBastionOrderCommand(
            new PlayerId(2),
            10,
            4,
            new BastionOrder(
                BastionOrderKind.Patrol,
                Target: new TilePosition(1, 2),
                Waypoints: new[] { new TilePosition(1, 2), new TilePosition(3, 4) },
                WaypointIndex: 1));
        var roundTripOrder = Assert.IsType<IssueBastionOrderCommand>(
            SimulationCommandSerializer.Deserialize(SimulationCommandSerializer.Serialize(order)));
        Assert.Equal(order.Actor, roundTripOrder.Actor);
        Assert.Equal(order.Tick, roundTripOrder.Tick);
        Assert.Equal(order.BastionId, roundTripOrder.BastionId);
        Assert.Equal(order.Order.Kind, roundTripOrder.Order.Kind);
        Assert.Equal(order.Order.Target, roundTripOrder.Order.Target);
        Assert.Equal(order.Order.WaypointIndex, roundTripOrder.Order.WaypointIndex);
        Assert.Equal(order.Order.WaypointList, roundTripOrder.Order.WaypointList);
    }

    [Fact]
    public void EnqueueCommand_RejectsTickNotInFuture()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 1);
        var cmd = new StopCommanderCommand(new PlayerId(1), Tick: 0, CommanderId: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.EnqueueCommand(cmd));
    }

    private static string RunQueuedMove(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
        simulation.EnqueueForNextTick(tick => new IssueMoveCommand(
            new PlayerId(1), tick, commander.Id, new TilePosition(12, 10)));
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }

    private static string RunImmediateMove(int seed, int ticks)
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
}
