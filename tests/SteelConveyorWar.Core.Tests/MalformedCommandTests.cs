using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

// R09: untrusted/malformed command input must be rejected without throwing out of the tick loop.
public sealed class MalformedCommandTests
{
    [Fact]
    public void TryDeserialize_ReturnsFalse_ForGarbageInput_NeverThrows()
    {
        var random = new Random(1234);
        for (var i = 0; i < 500; i++)
        {
            var length = random.Next(0, 64);
            var bytes = new byte[length];
            random.NextBytes(bytes);
            var garbage = Convert.ToBase64String(bytes);

            // Must not throw for any input.
            var ok = SimulationCommandSerializer.TryDeserialize(garbage, out var command);
            if (!ok)
            {
                Assert.Null(command);
            }
        }

        Assert.False(SimulationCommandSerializer.TryDeserialize(null, out _));
        Assert.False(SimulationCommandSerializer.TryDeserialize(string.Empty, out _));
        Assert.False(SimulationCommandSerializer.TryDeserialize("{", out _));
        Assert.False(SimulationCommandSerializer.TryDeserialize("{\"kind\":\"issueMove\"}", out _)); // missing payload fields
    }

    [Fact]
    public void ApplyCommand_UnknownActor_IsRejected_NotThrown()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var stranger = new PlayerId(999);

        // Research command with a non-roster actor must reject rather than throw from GetPlayer().Single().
        Assert.False(simulation.ApplyCommand(
            new StartResearchCommand(stranger, 1, new TechnologyId("does.not.matter"))));
        Assert.False(simulation.ApplyCommand(
            new CancelResearchCommand(stranger, 1, new TechnologyId("does.not.matter"))));
        Assert.Equal(
            ResearchCommandResult.InvalidAllocation,
            simulation.TrySetTrackAllocation(stranger, new Dictionary<string, int>()));
    }

    [Fact]
    public void QueuedMalformedCommand_DoesNotAbortTick()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == player && entity.Kind == EntityKind.Commander);

        // A hostile command referencing a non-existent actor, queued alongside a valid move for the same tick.
        simulation.EnqueueForNextTick(tick => new StartResearchCommand(new PlayerId(777), tick, new TechnologyId("junk")));
        simulation.EnqueueForNextTick(tick => new IssueMoveCommand(player, tick, commander.Id, new TilePosition(12, 10)));

        simulation.AdvanceTick();

        // The valid command still applied — the tick was not aborted by the malformed one.
        Assert.Equal(new TilePosition(12, 10), simulation.World.GetEntity(commander.Id)!.MoveTarget);
        Assert.Empty(simulation.PendingCommands);
    }

    [Fact]
    public void SetTrackAllocation_ExtraKey_IsRejected_WithoutKeyNotFound()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var owner = new PlayerId(1);
        var schedule = simulation.ResearchProfile.Schedule;

        var allocations = new Dictionary<string, int>();
        foreach (var track in schedule.Tracks)
        {
            allocations[track.Id] = 0;
        }

        // Put the full budget onto the first real track so the total matches Scale.
        if (schedule.Tracks.Count > 0)
        {
            allocations[schedule.Tracks[0].Id] = schedule.Budget.Scale;
        }

        // Inject a key that is not a schedule track. This must be rejected, never a KeyNotFoundException.
        allocations["__not_a_real_track__"] = 0;

        var result = simulation.TrySetTrackAllocation(owner, allocations);
        Assert.Equal(ResearchCommandResult.InvalidAllocation, result);
    }
}
