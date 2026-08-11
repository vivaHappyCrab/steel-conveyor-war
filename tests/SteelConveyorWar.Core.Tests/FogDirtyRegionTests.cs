namespace SteelConveyorWar.Core.Tests;

public sealed class FogDirtyRegionTests
{
    [Fact]
    public void IdleTicks_ProduceNoFogDirtyTiles_AfterInitialPaint()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerOne = new PlayerId(1);

        // CreateNewGame paints FoW once; the next idle ticks should skip repaint.
        for (var i = 0; i < 5; i++)
        {
            simulation.AdvanceTick();
            Assert.Empty(simulation.GetFogDirtyTiles(playerOne));
        }
    }

    [Fact]
    public void Movement_MarksLeftAndEnteredTilesDirty()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerOne = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == playerOne);

        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, new TilePosition(90, 50)));
        simulation.AdvanceTick();
        var visited = commander.Position;
        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(playerOne, visited));

        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, new TilePosition(10, 10)));
        simulation.AdvanceTick();

        var dirty = simulation.GetFogDirtyTiles(playerOne);
        Assert.Contains(visited, dirty);
        Assert.Contains(commander.Position, dirty);
        Assert.Equal(VisibilityState.Explored, simulation.GetVisibility(playerOne, visited));
        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(playerOne, commander.Position));
    }

    [Fact]
    public void DualRuns_AfterMoveAndBuild_ProduceIdenticalHashAndVisibility()
    {
        static (string Hash, string Visibility) Run()
        {
            var simulation = GameSimulation.CreateNewGame(randomSeed: 88);
            var playerOne = new PlayerId(1);
            var commander = simulation.World.Entities.First(entity =>
                entity.OwnerId == playerOne && entity.Kind == EntityKind.Commander);

            Assert.True(simulation.TryIssueMoveCommand(commander.Id, playerOne, new TilePosition(24, 20)));
            Assert.True(simulation.TryPlaceGhostBuild(playerOne, EntityKind.Smelter, Near(simulation, playerOne, 6, -2), out _));

            for (var i = 0; i < 90; i++)
            {
                simulation.AdvanceTick();
            }

            return (simulation.ComputeStateHash(), CaptureVisibilityFingerprint(simulation, playerOne));
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(a.Visibility, b.Visibility);
    }

    [Fact]
    public void DualRuns_IdleThenMove_ProduceIdenticalFogDirtyCounts()
    {
        static List<int> Run()
        {
            var simulation = GameSimulation.CreateNewGame(randomSeed: 55);
            var playerOne = new PlayerId(1);
            var commander = simulation.World.Entities.First(entity =>
                entity.OwnerId == playerOne && entity.Kind == EntityKind.Commander);

            var dirtyCounts = new List<int>();
            for (var i = 0; i < 10; i++)
            {
                simulation.AdvanceTick();
                dirtyCounts.Add(simulation.GetFogDirtyTiles(playerOne).Count);
            }

            Assert.True(simulation.TryTeleportEntityForTests(commander.Id, new TilePosition(40, 40)));
            simulation.AdvanceTick();
            dirtyCounts.Add(simulation.GetFogDirtyTiles(playerOne).Count);

            simulation.AdvanceTick();
            dirtyCounts.Add(simulation.GetFogDirtyTiles(playerOne).Count);
            return dirtyCounts;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void TechSignatures_MatchAcrossDualRuns_AfterEnemyBuild()
    {
        static string Run()
        {
            var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
            var playerOne = new PlayerId(1);
            var playerTwo = new PlayerId(2);
            var midY = simulation.World.Size.Height / 2;
            // Mirror NearBlue(..., playerId: 2) placement used by FogOfWarAndTechSignatures tests.
            var smelterTile = new TilePosition(simulation.World.Size.Width - 1 - 8, midY);
            Assert.True(simulation.TryPlaceGhostBuild(playerTwo, EntityKind.Smelter, smelterTile, out _));
            for (var i = 0; i < 40; i++)
            {
                simulation.AdvanceTick();
            }

            var hotspots = simulation.GetTechSignatureHotspots(playerOne);
            return string.Join(';', hotspots.Select(h => $"{h.ZoneX},{h.ZoneY},{h.Intensity}"));
        }

        Assert.Equal(Run(), Run());
        Assert.False(string.IsNullOrEmpty(Run()));
    }

    private static string CaptureVisibilityFingerprint(GameSimulation simulation, PlayerId playerId)
    {
        var player = simulation.GetPlayer(playerId);
        var width = simulation.World.Size.Width;
        var height = simulation.World.Size.Height;
        var chars = new char[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                chars[y * width + x] = player.GetVisibility(new TilePosition(x, y)) switch
                {
                    VisibilityState.Unknown => 'U',
                    VisibilityState.Explored => 'E',
                    VisibilityState.Visible => 'V',
                    _ => '?'
                };
            }
        }

        return new string(chars);
    }

    private static TilePosition Near(GameSimulation simulation, PlayerId playerId, int x, int yOffsetFromMid)
    {
        var midY = simulation.World.Size.Height / 2;
        if (playerId.Value == 1)
        {
            return new TilePosition(x, midY + yOffsetFromMid);
        }

        return new TilePosition(simulation.World.Size.Width - 1 - x, midY + yOffsetFromMid);
    }
}
