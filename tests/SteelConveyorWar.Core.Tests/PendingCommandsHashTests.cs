using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R13: the not-yet-applied command queue is part of the session fingerprint. Two peers can share an
/// identical <see cref="GameSimulation.ComputeStateHash"/> yet diverge on future ticks; the separate
/// <see cref="GameSimulation.ComputePendingCommandsHash"/> makes that divergence observable, is
/// insensitive to enqueue/arrival order, and a replay of the same queue reproduces the same state hash.
/// </summary>
public sealed class PendingCommandsHashTests
{
    private static WorldEntity Commander(GameSimulation simulation, PlayerId actor) =>
        simulation.World.Entities.First(entity =>
            entity.OwnerId == actor && entity.Kind == EntityKind.Commander);

    [Fact]
    public void IdenticalState_DifferentPendingCommands_AreDetected()
    {
        var actor = new PlayerId(1);
        var a = GameSimulation.CreateNewGame(randomSeed: 5);
        var b = GameSimulation.CreateNewGame(randomSeed: 5);

        // Precondition: identical present state.
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());

        a.EnqueueForNextTick(tick => new IssueMoveCommand(actor, tick, Commander(a, actor).Id, new TilePosition(6, 6)));
        b.EnqueueForNextTick(tick => new IssueMoveCommand(actor, tick, Commander(b, actor).Id, new TilePosition(7, 7)));

        // State hash is unchanged (commands are not applied yet)...
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        // ...but the pending-commands hash exposes the future divergence.
        Assert.NotEqual(a.ComputePendingCommandsHash(), b.ComputePendingCommandsHash());
    }

    [Fact]
    public void PendingCommandsHash_IsInsensitiveToEnqueueOrder()
    {
        var actor = new PlayerId(1);
        var a = GameSimulation.CreateNewGame(randomSeed: 5);
        var b = GameSimulation.CreateNewGame(randomSeed: 5);
        var commanderId = Commander(a, actor).Id;

        var first = new IssueMoveCommand(actor, 1, commanderId, new TilePosition(6, 6)) { Sequence = 1 };
        var second = new IssueMoveCommand(actor, 1, commanderId, new TilePosition(7, 7)) { Sequence = 2 };

        a.EnqueueCommand(first);
        a.EnqueueCommand(second);
        // Reverse insertion order on the second peer.
        b.EnqueueCommand(second);
        b.EnqueueCommand(first);

        Assert.Equal(a.ComputePendingCommandsHash(), b.ComputePendingCommandsHash());
    }

    [Fact]
    public void EmptyQueue_HashesToStableConstant_NotStateHash()
    {
        var a = GameSimulation.CreateNewGame(randomSeed: 5);
        var b = GameSimulation.CreateNewGame(randomSeed: 5);

        var hash = a.ComputePendingCommandsHash();

        Assert.Equal(hash, b.ComputePendingCommandsHash());
        Assert.Equal(64, hash.Length);
        // The empty-queue fingerprint is distinct from the authoritative state fingerprint.
        Assert.NotEqual(a.ComputeStateHash(), hash);
    }

    [Fact]
    public void ReplayingTheSameQueue_ReproducesIdenticalStateHash()
    {
        var actor = new PlayerId(1);
        var live = GameSimulation.CreateNewGame(randomSeed: 5);
        var replay = GameSimulation.CreateNewGame(randomSeed: 5);

        live.EnqueueForNextTick(tick => new IssueMoveCommand(actor, tick, Commander(live, actor).Id, new TilePosition(6, 6)));
        replay.EnqueueForNextTick(tick => new IssueMoveCommand(actor, tick, Commander(replay, actor).Id, new TilePosition(6, 6)));

        // Same queue → same pending fingerprint before applying.
        Assert.Equal(live.ComputePendingCommandsHash(), replay.ComputePendingCommandsHash());

        for (var i = 0; i < 10; i++)
        {
            live.AdvanceTick();
            replay.AdvanceTick();
        }

        Assert.Equal(live.ComputeStateHash(), replay.ComputeStateHash());
    }
}
