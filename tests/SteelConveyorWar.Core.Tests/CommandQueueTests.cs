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

    // R02: SelectResearch became a first-class queued command (research input previously bypassed the
    // queue). Its DTO must survive a serialize/deserialize round-trip so it can travel over the wire and
    // replay from a log identically.
    [Fact]
    public void Serializer_RoundTripsSelectResearch()
    {
        var select = new SelectResearchCommand(
            new PlayerId(2),
            7,
            new TechnologyId("logistics-1"),
            ConfirmExclusive: true,
            PreferredTrackId: "cycle");
        var roundTrip = Assert.IsType<SelectResearchCommand>(
            SimulationCommandSerializer.Deserialize(SimulationCommandSerializer.Serialize(select)));

        Assert.Equal(select, roundTrip);
    }

    [Fact]
    public void Serializer_RoundTripsSelectResearch_WithDefaults()
    {
        var select = new SelectResearchCommand(new PlayerId(1), 3, new TechnologyId("mining-1"));
        var roundTrip = Assert.IsType<SelectResearchCommand>(
            SimulationCommandSerializer.Deserialize(SimulationCommandSerializer.Serialize(select)));

        Assert.Equal(select, roundTrip);
        Assert.False(roundTrip.ConfirmExclusive);
        Assert.Null(roundTrip.PreferredTrackId);
    }

    [Fact]
    public void EnqueueCommand_RejectsTickNotInFuture()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 1);
        var cmd = new StopCommanderCommand(new PlayerId(1), Tick: 0, CommanderId: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.EnqueueCommand(cmd));
    }

    // R03: applied order must follow canonical (Actor, Sequence), not enqueue/network arrival order.
    [Fact]
    public void SameTickCommands_DifferentEnqueueOrder_ProduceIdenticalHash()
    {
        var forward = RunTwoSequencedMoves(reverseEnqueue: false);
        var reversed = RunTwoSequencedMoves(reverseEnqueue: true);
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void DuplicateActorSequence_IsAppliedAtMostOnce()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == player && entity.Kind == EntityKind.Commander);

        // Same (Actor, Sequence) => second occurrence must be suppressed. Distinct targets let us
        // observe which one won: the first (stable order) is applied, the duplicate is dropped.
        var first = new IssueMoveCommand(player, 1, commander.Id, new TilePosition(12, 10)) { Sequence = 5 };
        var duplicate = new IssueMoveCommand(player, 1, commander.Id, new TilePosition(8, 9)) { Sequence = 5 };
        simulation.EnqueueCommand(first);
        simulation.EnqueueCommand(duplicate);

        simulation.AdvanceTick();

        Assert.Equal(new TilePosition(12, 10), simulation.World.GetEntity(commander.Id)!.MoveTarget);
    }

    private static string RunTwoSequencedMoves(bool reverseEnqueue)
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == player && entity.Kind == EntityKind.Commander);

        var seq1 = new IssueMoveCommand(player, 1, commander.Id, new TilePosition(12, 10)) { Sequence = 1 };
        var seq2 = new IssueMoveCommand(player, 1, commander.Id, new TilePosition(8, 9)) { Sequence = 2 };

        if (reverseEnqueue)
        {
            simulation.EnqueueCommand(seq2);
            simulation.EnqueueCommand(seq1);
        }
        else
        {
            simulation.EnqueueCommand(seq1);
            simulation.EnqueueCommand(seq2);
        }

        for (var i = 0; i < 30; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
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
        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new PlayerId(1), new TilePosition(12, 10)));
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }
}
