using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;
using SteelConveyorWar.Core.Replay;
using SteelConveyorWar.Hosting;

namespace SteelConveyorWar.Hosting.Tests;

public sealed class ReplayFileStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsDocument()
    {
        var live = RunShortSession();
        var document = ReplayDocument.Capture(live);
        var path = Path.Combine(Path.GetTempPath(), "scw-replay-" + Guid.NewGuid().ToString("N") + ReplayFileStore.FileExtension);

        try
        {
            ReplayFileStore.Save(path, document);
            var loaded = ReplayFileStore.Load(path);

            Assert.Equal(document.FinalStateHash, loaded.FinalStateHash);
            Assert.Equal(document.DurationTicks, loaded.DurationTicks);
            Assert.Equal(document.Commands.Count, loaded.Commands.Count);

            var replay = ReplayPlayback.CreateReady(GameCreationOptions.Default, loaded);
            var hash = ReplayPlayback.PlayToEnd(replay, loaded.DurationTicks);
            Assert.Equal(document.FinalStateHash, hash);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "scw-no-such-replay-" + Guid.NewGuid().ToString("N") + ReplayFileStore.FileExtension);
        Assert.Throws<FileNotFoundException>(() => ReplayFileStore.Load(missing));
    }

    [Fact]
    public void Parse_ResolvesReplayAndRecordFlags()
    {
        var options = ReplayHostOptions.Parse(["--ticks", "90", "--replay", "a.scwreplay", "--record", "b.scwreplay"]);
        Assert.Equal("a.scwreplay", options.ReplayPath);
        Assert.Equal("b.scwreplay", options.RecordPath);
        Assert.False(options.ShouldRecord);
    }

    [Fact]
    public void Parse_NoRecord_DisablesAutoSave()
    {
        var options = ReplayHostOptions.Parse(["--no-record"]);
        Assert.True(options.NoRecord);
        Assert.False(options.ShouldRecord);
    }

    [Fact]
    public void Parse_ReplayWithoutPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => ReplayHostOptions.Parse(["--replay"]));
    }

    [Fact]
    public void ResolveRecordPath_UsesExplicitOverride()
    {
        var document = ReplayDocument.Capture(GameSimulation.CreateNewGame(randomSeed: 1));
        var options = new ReplayHostOptions(null, @"C:\tmp\match.scwreplay", false);
        Assert.Equal(@"C:\tmp\match.scwreplay", options.ResolveRecordPath(document));
    }

    [Fact]
    public void DefaultFileName_IncludesSeedAndTick()
    {
        var document = ReplayDocument.Capture(GameSimulation.CreateNewGame(randomSeed: 42));
        var name = ReplayFileStore.DefaultFileName(document, new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal("20260910-120000-seed42-t0.scwreplay", name);
    }

    private static GameSimulation RunShortSession()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 4);
        var sink = new DeferredCommandSink(simulation);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == player && entity.Kind == EntityKind.Commander);
        sink.Enqueue(new IssueMoveCommand(player, 0, commander.Id, new TilePosition(14, 11)));
        for (var i = 0; i < 30; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation;
    }
}
