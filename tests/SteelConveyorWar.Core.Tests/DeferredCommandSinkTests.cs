using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R02: covers the production input funnel (<see cref="DeferredCommandSink"/>). The sink is the single
/// path player input takes into the simulation, so these tests pin down the three properties the
/// production path relies on: sink-scheduled input is deterministic across runs, the recorded command
/// log replays to an identical state hash, and per-actor sequences are monotonic (so canonical ordering
/// and duplicate suppression stay well-defined).
/// </summary>
public sealed class DeferredCommandSinkTests
{
    [Fact]
    public void Enqueue_SchedulesOnFutureTick_WithMonotonicPerActorSequence()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 3);
        var sink = new DeferredCommandSink(simulation);
        var player = new PlayerId(1);
        var commander = CommanderOf(simulation, player);

        var first = sink.Enqueue(new IssueMoveCommand(player, 0, commander.Id, new TilePosition(12, 10)));
        var second = sink.Enqueue(new StopCommanderCommand(player, 0, commander.Id));

        // Input-delay policy: both land on the strictly-future tick (Tick 0 + 1).
        Assert.Equal(simulation.Tick + sink.InputDelayTicks, first.Tick);
        Assert.Equal(simulation.Tick + sink.InputDelayTicks, second.Tick);

        // Per-actor sequences start at 1 and increase by one so they participate in canonical order (R03).
        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
    }

    [Fact]
    public void Enqueue_TracksSequencesIndependentlyPerActor()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 3);
        var sink = new DeferredCommandSink(simulation);
        var red = new PlayerId(1);
        var blue = new PlayerId(2);
        var redCommander = CommanderOf(simulation, red);
        var blueCommander = CommanderOf(simulation, blue);

        var red1 = sink.Enqueue(new IssueMoveCommand(red, 0, redCommander.Id, new TilePosition(12, 10)));
        var blue1 = sink.Enqueue(new IssueMoveCommand(blue, 0, blueCommander.Id, new TilePosition(12, 10)));
        var red2 = sink.Enqueue(new StopCommanderCommand(red, 0, redCommander.Id));

        Assert.Equal(1, red1.Sequence);
        Assert.Equal(1, blue1.Sequence);
        Assert.Equal(2, red2.Sequence);
    }

    [Fact]
    public void Enqueue_RejectsInputDelayBelowOneTick()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeferredCommandSink(simulation, inputDelayTicks: 0));
    }

    [Fact]
    public void SinkDrivenInput_ProducesIdenticalHashAcrossRuns()
    {
        var (hashA, _) = RunScriptedSession(seed: 42);
        var (hashB, _) = RunScriptedSession(seed: 42);

        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length);
    }

    [Fact]
    public void RecordedCommandLog_ReplaysToIdenticalHash()
    {
        var (liveHash, log) = RunScriptedSession(seed: 77);

        // Replay: feed the recorded log (already stamped with absolute ticks + sequences) into a fresh
        // simulation and advance the same number of ticks. A matching hash proves the log is a faithful,
        // self-contained record of the session.
        var replay = GameSimulation.CreateNewGame(randomSeed: 77);
        foreach (var command in log)
        {
            replay.EnqueueCommand(command);
        }

        for (var i = 0; i < ScriptedTickCount; i++)
        {
            replay.AdvanceTick();
        }

        Assert.Equal(liveHash, replay.ComputeStateHash());
    }

    private const int ScriptedTickCount = 120;

    // Drives a fixed sequence of inputs through the sink at scripted ticks, mirroring how the SFML/Headless
    // hosts feed window/AI events into the funnel as ticks advance.
    private static (string Hash, IReadOnlyList<ISimulationCommand> Log) RunScriptedSession(int seed)
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: seed);
        var sink = new DeferredCommandSink(simulation);
        var player = new PlayerId(1);
        var commander = CommanderOf(simulation, player);

        for (var tick = 0; tick < ScriptedTickCount; tick++)
        {
            switch (tick)
            {
                case 0:
                    sink.Enqueue(new IssueMoveCommand(player, 0, commander.Id, new TilePosition(14, 11)));
                    break;
                case 20:
                    sink.Enqueue(new IssueMoveCommand(player, 0, commander.Id, new TilePosition(9, 8)));
                    break;
                case 45:
                    sink.Enqueue(new StopCommanderCommand(player, 0, commander.Id));
                    break;
            }

            simulation.AdvanceTick();
        }

        return (simulation.ComputeStateHash(), sink.CommandLog);
    }

    private static WorldEntity CommanderOf(GameSimulation simulation, PlayerId player) =>
        simulation.World.Entities.First(entity => entity.OwnerId == player && entity.Kind == EntityKind.Commander);
}
