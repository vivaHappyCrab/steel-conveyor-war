using SteelConveyorWar.Core.Commands;
using SteelConveyorWar.Core.Replay;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// Durable command-log replay: ledger on EnqueueCommand, document round-trip, fail-fast compatibility,
/// and hash-identical playback.
/// </summary>
public sealed class ReplayDocumentTests
{
    private const int ScriptedTickCount = 80;

    [Fact]
    public void EnqueueCommand_AppendsToRecordedCommands_WithStampedSequence()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 3);
        var player = new PlayerId(1);
        var commander = CommanderOf(simulation, player);

        simulation.EnqueueCommand(new IssueMoveCommand(player, 1, commander.Id, new TilePosition(12, 10)));

        var recorded = Assert.Single(simulation.RecordedCommands);
        Assert.Equal(1, recorded.Tick);
        Assert.True(recorded.Sequence > 0);
    }

    [Fact]
    public void TryIssueMove_DoesNotEnterRecordedCommands()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 3);
        var player = new PlayerId(1);
        var commander = CommanderOf(simulation, player);

        Assert.True(simulation.TryIssueMoveCommand(commander.Id, player, new TilePosition(12, 10)));
        Assert.Empty(simulation.RecordedCommands);
    }

    [Fact]
    public void Capture_RoundTrip_PreservesHeaderAndCommands()
    {
        var (live, _) = RunScriptedSession(seed: 51);
        var captured = ReplayDocument.Capture(live);
        var json = ReplayDocumentSerializer.Serialize(captured);
        var loaded = ReplayDocumentSerializer.Deserialize(json);

        Assert.Equal(ReplayDocument.CurrentFormatVersion, loaded.FormatVersion);
        Assert.Equal(SimulationCommandSerializer.ProtocolVersion, loaded.ProtocolVersion);
        Assert.Equal(SimulationStateHasher.AlgorithmVersion, loaded.AlgorithmVersion);
        Assert.Equal(captured.DurationTicks, loaded.DurationTicks);
        Assert.Equal(captured.FinalStateHash, loaded.FinalStateHash);
        Assert.Equal(captured.ContentManifest, loaded.ContentManifest);
        Assert.Equal(captured.SessionManifest, loaded.SessionManifest);
        Assert.Equal(captured.Seed, loaded.Seed);
        Assert.Equal(captured.Commands.Count, loaded.Commands.Count);
        Assert.Equal(captured.Commands[0].Kind, loaded.Commands[0].Kind);
        Assert.Equal(captured.Commands[0].Sequence, loaded.Commands[0].Sequence);
    }

    [Fact]
    public void CaptureAndPlay_ReproducesIdenticalStateHash()
    {
        var (live, hash) = RunScriptedSession(seed: 77);
        var document = ReplayDocument.Capture(live);

        var replay = ReplayPlayback.CreateReady(GameCreationOptions.Default, document);
        var replayedHash = ReplayPlayback.PlayToEnd(replay, document.DurationTicks);

        Assert.Equal(hash, document.FinalStateHash);
        Assert.Equal(hash, replayedHash);
        Assert.Equal(document.DurationTicks, replay.Tick);
    }

    [Fact]
    public void Attach_Throws_OnAlgorithmMismatch()
    {
        var (live, _) = RunScriptedSession(seed: 9);
        var document = ReplayDocument.Capture(live) with { AlgorithmVersion = SimulationStateHasher.AlgorithmVersion - 1 };
        var fresh = GameSimulation.CreateNewGame(randomSeed: 9);

        Assert.Throws<ReplayCompatibilityException>(() => ReplayPlayback.Attach(fresh, document));
    }

    [Fact]
    public void Attach_Throws_OnProtocolMismatch()
    {
        var (live, _) = RunScriptedSession(seed: 9);
        var document = ReplayDocument.Capture(live) with { ProtocolVersion = SimulationCommandSerializer.ProtocolVersion + 1 };
        var fresh = GameSimulation.CreateNewGame(randomSeed: 9);

        Assert.Throws<ReplayCompatibilityException>(() => ReplayPlayback.Attach(fresh, document));
    }

    [Fact]
    public void Attach_Throws_OnContentManifestMismatch()
    {
        var (live, _) = RunScriptedSession(seed: 9);
        var document = ReplayDocument.Capture(live) with { ContentManifest = new string('a', 64) };
        var fresh = GameSimulation.CreateNewGame(randomSeed: 9);

        Assert.Throws<ContentManifestMismatchException>(() => ReplayPlayback.Attach(fresh, document));
    }

    [Fact]
    public void Attach_Throws_OnSeedMismatch()
    {
        var (live, _) = RunScriptedSession(seed: 9);
        var document = ReplayDocument.Capture(live);
        var fresh = GameSimulation.CreateNewGame(randomSeed: 10);

        var ex = Assert.Throws<ReplayCompatibilityException>(() => ReplayPlayback.Attach(fresh, document));
        Assert.Contains("Seed mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RejectsNonPositiveTicksPerSecond()
    {
        var (live, _) = RunScriptedSession(seed: 9);
        var document = ReplayDocument.Capture(live) with { TicksPerSecond = 0 };
        var json = ReplayDocumentSerializer.Serialize(document);

        var ex = Assert.Throws<InvalidOperationException>(() => ReplayDocumentSerializer.Deserialize(json));
        Assert.Contains("ticksPerSecond", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_RejectsDurationAboveMax()
    {
        var (live, _) = RunScriptedSession(seed: 9);
        var document = ReplayDocument.Capture(live) with { DurationTicks = ReplayDocument.MaxDurationTicks + 1 };
        var json = ReplayDocumentSerializer.Serialize(document);

        var ex = Assert.Throws<InvalidOperationException>(() => ReplayDocumentSerializer.Deserialize(json));
        Assert.Contains("durationTicks", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayToEnd_StopsWhenMatchLeavesInProgress_EvenIfDurationHigher()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 3);
        var enemy = simulation.World.Entities.First(
            entity => entity.OwnerId == new PlayerId(2) && entity.Kind == EntityKind.Commander);
        simulation.DamageEntity(enemy.Id, enemy.Health);
        Assert.Equal(GameStatus.PlayerWon, simulation.Status);

        var before = simulation.Tick;
        var hash = ReplayPlayback.PlayToEnd(simulation, durationTicks: before + 50_000);

        Assert.Equal(before, simulation.Tick);
        Assert.Equal(simulation.ComputeStateHash(), hash);
    }

    [Fact]
    public void Deserialize_RejectsMissingCommandsArray()
    {
        const string json = """
            {
              "formatVersion": 1,
              "protocolVersion": 3,
              "algorithmVersion": 10,
              "inputDelayTicks": 1,
              "durationTicks": 0,
              "finalStateHash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
              "contentManifest": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
              "sessionManifest": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
              "seed": 1,
              "ticksPerSecond": 30,
              "profileId": "mvp-b",
              "mapId": "default"
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReplayDocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void NullPlayerCommandSink_DoesNotEnqueue()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 1);
        var sink = new NullPlayerCommandSink();
        var player = new PlayerId(1);
        var commander = CommanderOf(simulation, player);

        sink.Enqueue(new IssueMoveCommand(player, 0, commander.Id, new TilePosition(12, 10)));

        Assert.Empty(simulation.PendingCommands);
        Assert.Empty(simulation.RecordedCommands);
        Assert.Empty(sink.CommandLog);
    }

    private static (GameSimulation Simulation, string Hash) RunScriptedSession(int seed)
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
                case 40:
                    sink.Enqueue(new StopCommanderCommand(player, 0, commander.Id));
                    break;
            }

            simulation.AdvanceTick();
        }

        return (simulation, simulation.ComputeStateHash());
    }

    private static WorldEntity CommanderOf(GameSimulation simulation, PlayerId player) =>
        simulation.World.Entities.First(entity => entity.OwnerId == player && entity.Kind == EntityKind.Commander);
}
