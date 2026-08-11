using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R12: command / order payloads carrying collections take a deep, immutable snapshot at construction,
/// so a caller that keeps and later mutates the source list/dictionary — even after the command is
/// enqueued — cannot retroactively alter the intent the simulation will apply.
/// </summary>
public sealed class ImmutablePayloadTests
{
    [Fact]
    public void BastionOrder_SnapshotsWaypoints_SourceMutationHasNoEffect()
    {
        var source = new List<TilePosition> { new(1, 1), new(2, 2) };

        var order = new BastionOrder(BastionOrderKind.Patrol, Waypoints: source);

        // Mutate the caller-owned list after the order has copied it.
        source.Add(new TilePosition(3, 3));
        source[0] = new TilePosition(99, 99);

        Assert.Equal(2, order.WaypointList.Count);
        Assert.Equal(new TilePosition(1, 1), order.WaypointList[0]);
        Assert.Equal(new TilePosition(2, 2), order.WaypointList[1]);
    }

    [Fact]
    public void BastionOrder_WithNullWaypoints_YieldsEmptyImmutableSnapshot()
    {
        var order = new BastionOrder(BastionOrderKind.Defend);

        Assert.Empty(order.WaypointList);
    }

    [Fact]
    public void IssueBastionOrderCommand_KeepsSnapshot_AfterEnqueue()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 5);
        var actor = new PlayerId(1);

        var source = new List<TilePosition> { new(1, 1), new(2, 2) };
        var command = simulation.EnqueueForNextTick(tick =>
            new IssueBastionOrderCommand(actor, tick, BastionId: 1, new BastionOrder(BastionOrderKind.Patrol, Waypoints: source)));

        // Mutate the source after the command is already queued.
        source.Clear();

        Assert.Equal(2, command.Order.WaypointList.Count);
    }

    [Fact]
    public void SetTrackAllocationCommand_SnapshotsAllocations_SourceMutationHasNoEffect()
    {
        var source = new Dictionary<string, int> { ["cycle"] = 2, ["burst"] = 1 };

        var command = new SetTrackAllocationCommand(new PlayerId(1), 5, source);

        // Mutate the caller-owned dictionary after the command copied it.
        source["cycle"] = 999;
        source["extra"] = 7;
        source.Remove("burst");

        Assert.Equal(2, command.Allocations.Count);
        Assert.Equal(2, command.Allocations["cycle"]);
        Assert.Equal(1, command.Allocations["burst"]);
        Assert.False(command.Allocations.ContainsKey("extra"));
    }

    [Fact]
    public void SetTrackAllocationCommand_WithNullAllocations_YieldsEmptySnapshot()
    {
        var command = new SetTrackAllocationCommand(new PlayerId(1), 5, null!);

        Assert.Empty(command.Allocations);
    }
}
