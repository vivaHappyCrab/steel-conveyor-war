namespace SteelConveyorWar.Core.Tests;

public sealed class AllianceMinimapTests
{
    [Fact]
    public void MapSettings_ParsesDefaultMapJson()
    {
        var map = MapSettingsLoader.Parse(File.ReadAllText(FindConfigPath("maps/default.json")));
        Assert.Equal("default", map.MapId);
        Assert.Equal(2, map.Players.Count);
        Assert.Equal(1, map.Players[0].TeamId);
        Assert.Equal(2, map.Players[1].TeamId);
        Assert.Equal("Blue", map.Players[0].Name);
        Assert.Equal("Red", map.Players[1].Name);
    }

    [Fact]
    public void MapSettings_RejectsDuplicatePlayerIds()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "mapId": "bad",
              "players": [
                { "id": 1, "name": "A", "teamId": 1 },
                { "id": 1, "name": "B", "teamId": 2 }
              ]
            }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => MapSettingsLoader.Parse(json));
        Assert.Contains("Duplicate map player id", ex.Message);
    }

    [Fact]
    public void CreateNewGame_DefaultMap_UsesOpposingTeams()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.Equal(1, simulation.GetPlayer(new PlayerId(1)).TeamId);
        Assert.Equal(2, simulation.GetPlayer(new PlayerId(2)).TeamId);
        Assert.False(simulation.AreAllied(new PlayerId(1), new PlayerId(2)));
    }

    [Fact]
    public void ProcessCombat_AlliedPlayers_DoNotFriendlyFire()
    {
        var simulation = CreateAlliedTwoPlayerGame();
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var ally = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.AreAllied(new PlayerId(1), new PlayerId(2)));
        Assert.True(PlaceAdjacent(simulation, attacker, ally.Position));

        var healthBefore = ally.Health;
        simulation.AdvanceTick();
        Assert.Equal(healthBefore, ally.Health);
    }

    [Fact]
    public void UpdateFogOfWar_SharedVision_AcrossAlliedPlayers()
    {
        var simulation = CreateAlliedTwoPlayerGame();
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        var redCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == red);

        Assert.True(simulation.TryTeleportEntityForTests(redCommander.Id, new TilePosition(90, 50)));
        simulation.AdvanceTick();

        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(blue, redCommander.Position));
        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(red, redCommander.Position));
    }

    [Fact]
    public void UpdateFogOfWar_LeavesExplored_WhenVisionSourceLeaves()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerOne = new PlayerId(1);
        var blueCommander = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == playerOne);

        // Move into empty space so only this commander paints the visited tile.
        Assert.True(simulation.TryTeleportEntityForTests(blueCommander.Id, new TilePosition(90, 50)));
        simulation.AdvanceTick();
        var visited = blueCommander.Position;
        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(playerOne, visited));

        Assert.True(simulation.TryTeleportEntityForTests(blueCommander.Id, new TilePosition(10, 10)));
        simulation.AdvanceTick();

        Assert.Equal(VisibilityState.Explored, simulation.GetVisibility(playerOne, visited));
        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(playerOne, blueCommander.Position));
    }

    [Fact]
    public void UpdateFogOfWar_DualRuns_ProduceIdenticalHashes()
    {
        static string Run()
        {
            var simulation = GameSimulation.CreateNewGame(randomSeed: 71);
            var commander = simulation.World.Entities.First(entity =>
                entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
            Assert.True(simulation.TryIssueMoveCommand(commander.Id, new TilePosition(20, 18)));
            for (var i = 0; i < 90; i++)
            {
                simulation.AdvanceTick();
            }

            return simulation.ComputeStateHash();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ProcessCombat_WallOwnedByAlly_BlocksGroundToGround()
    {
        var map = new MapSettings(
            1,
            "ally-wall",
            [
                new MapPlayerDefinition(1, "Blue", 1),
                new MapPlayerDefinition(2, "Red", 2),
                new MapPlayerDefinition(3, "BlueAlly", 1)
            ]);
        var simulation = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, Map = map });
        Assert.Equal(3, simulation.Players.Count);

        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.True(simulation.TryTeleportEntityForTests(attacker.Id, new TilePosition(20, 20)));
        Assert.True(simulation.TryTeleportEntityForTests(defender.Id, new TilePosition(22, 20)));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, new TilePosition(21, 20), new PlayerId(3), out _));

        var healthBefore = defender.Health;
        simulation.AdvanceTick();
        Assert.Equal(healthBefore, defender.Health);
        Assert.True(attacker.AttackCooldownRemaining > 0);
    }

    [Fact]
    public void GameSettings_ParsesMapContentPath()
    {
        var settings = GameSettingsLoader.Parse(File.ReadAllText(FindConfigPath("game.json")));
        Assert.Equal("maps/default.json", settings.MapContentFile);
    }

    private static GameSimulation CreateAlliedTwoPlayerGame()
    {
        var map = new MapSettings(
            1,
            "allied-1v0",
            [
                new MapPlayerDefinition(1, "Blue", 1),
                new MapPlayerDefinition(2, "Red", 1)
            ]);
        return GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, Map = map });
    }

    private static bool PlaceAdjacent(GameSimulation simulation, WorldEntity entity, TilePosition near)
    {
        var candidates = new[]
        {
            new TilePosition(near.X - 1, near.Y),
            new TilePosition(near.X + 1, near.Y),
            new TilePosition(near.X, near.Y - 1),
            new TilePosition(near.X, near.Y + 1)
        };

        var position = candidates.First(candidate => candidate.X >= 0 && candidate.Y >= 0);
        return simulation.TryTeleportEntityForTests(entity.Id, position);
    }

    private static string FindConfigPath(string relativePath)
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", relativePath)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config", relativePath)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "config", relativePath))
        };
        return candidates.First(File.Exists);
    }
}
