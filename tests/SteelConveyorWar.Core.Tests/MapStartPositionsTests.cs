namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R31: starting geometry is data-driven from map roster seats (commander/bastion/hub tiles).
/// </summary>
public sealed class MapStartPositionsTests
{
    [Fact]
    public void DefaultMapJson_EncodesClassic1v1Geometry()
    {
        var map = MapSettingsLoader.Parse(File.ReadAllText(FindConfigPath("maps/default.json")));
        Assert.Equal(new TilePosition(4, 56), map.Players[0].StartCommander);
        Assert.Equal(new TilePosition(1, 56), map.Players[0].StartBastion);
        Assert.Equal(new TilePosition(5, 58), map.Players[0].StartHub);
        Assert.Equal("#46BAFF", map.Players[0].Color);
        Assert.Equal(new TilePosition(187, 56), map.Players[1].StartCommander);
        Assert.Equal(new TilePosition(188, 56), map.Players[1].StartBastion);
        Assert.Equal(new TilePosition(186, 58), map.Players[1].StartHub);
        Assert.Equal("#DC4646", map.Players[1].Color);
    }

    [Fact]
    public void CreateNewGame_DefaultMap_SpawnsCommandersAtConfiguredTiles()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var midY = MapPlayerDefinition.DefaultWorldHeight / 2;
        var width = MapPlayerDefinition.DefaultWorldWidth;

        AssertCommanderAt(simulation, new PlayerId(1), new TilePosition(4, midY));
        AssertCommanderAt(simulation, new PlayerId(2), new TilePosition(width - 5, midY));
        Assert.Equal(MapPlayerDefinition.DefaultColorPlayerOne, simulation.GetPlayer(new PlayerId(1)).Color);
        Assert.Equal(MapPlayerDefinition.DefaultColorPlayerTwo, simulation.GetPlayer(new PlayerId(2)).Color);
    }

    [Fact]
    public void CreateNewGame_NPlayerMap_CreatesNCommandersAtConfiguredTiles()
    {
        var map = new MapSettings(
            1,
            "3seat",
            [
                new MapPlayerDefinition(1, "Blue", 1, new(4, 56), new(1, 56), new(5, 58), "#46BAFF"),
                new MapPlayerDefinition(2, "Red", 2, new(187, 56), new(188, 56), new(186, 58), "#DC4646"),
                new MapPlayerDefinition(3, "Green", 3, new(90, 10), new(87, 10), new(91, 12), "#46DC46")
            ]);

        var simulation = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, Map = map });
        Assert.Equal(3, simulation.Players.Count);
        AssertCommanderAt(simulation, new PlayerId(1), new TilePosition(4, 56));
        AssertCommanderAt(simulation, new PlayerId(2), new TilePosition(187, 56));
        AssertCommanderAt(simulation, new PlayerId(3), new TilePosition(90, 10));
        Assert.Equal(3, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.Commander && entity.IsAlive));
    }

    [Fact]
    public void DefaultMap_DualRun_StateHashesMatch()
    {
        static string Run()
        {
            var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
            for (var i = 0; i < 90; i++)
            {
                simulation.AdvanceTick();
            }

            return simulation.ComputeStateHash();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void MapSettings_OmittingStarts_UsesClassic1v1DefaultsForSeatsOneAndTwo()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "mapId": "legacy",
              "players": [
                { "id": 1, "name": "Blue", "teamId": 1 },
                { "id": 2, "name": "Red", "teamId": 2 }
              ]
            }
            """;
        var map = MapSettingsLoader.Parse(json);
        Assert.Equal(new TilePosition(4, 56), map.Players[0].StartCommander);
        Assert.Equal(new TilePosition(187, 56), map.Players[1].StartCommander);
        Assert.Equal(MapPlayerDefinition.DefaultColorPlayerOne, map.Players[0].Color);
        Assert.Equal(MapPlayerDefinition.DefaultColorPlayerTwo, map.Players[1].Color);
    }

    private static void AssertCommanderAt(GameSimulation simulation, PlayerId owner, TilePosition expected)
    {
        var commander = Assert.Single(
            simulation.World.Entities,
            entity => entity.Kind == EntityKind.Commander && entity.OwnerId == owner && entity.IsAlive);
        Assert.Equal(expected, commander.Position);
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
