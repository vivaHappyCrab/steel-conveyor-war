using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class ConfigContentLoaderTests
{
    [Fact]
    public void GameSettings_ParsesRepoGameJson()
    {
        var settings = GameSettingsLoader.Parse(File.ReadAllText(FindConfigPath("game.json")));
        Assert.Equal("steel-conveyor-war", settings.GameId);
        Assert.Equal(42, settings.DefaultRandomSeed);
        Assert.Equal(30, settings.TicksPerSecond);
        Assert.Equal(GameSimulation.TicksPerSecond, settings.TicksPerSecond);
        Assert.Equal("research.json", settings.ResearchContentFile);
        Assert.Equal(ResearchProfileIds.MvpB, settings.ResearchProfileId);
        Assert.Equal("maps/default.json", settings.MapContentFile);
        Assert.Equal("Steel Conveyor War", settings.DisplayName);
    }

    [Fact]
    public void GameSettings_RejectsMissingGameId()
    {
        Assert.Throws<InvalidOperationException>(() => GameSettingsLoader.Parse("""{"schemaVersion":1,"gameId":""}"""));
    }

    [Fact]
    public void GameSettings_RejectsNegativeTicksPerSecond()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "gameId": "steel-conveyor-war",
              "simulation": { "ticksPerSecond": -1, "defaultRandomSeed": 42 }
            }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => GameSettingsLoader.Parse(json));
        Assert.Contains("ticksPerSecond", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameSettings_MissingTicksPerSecond_FallsBackToDefault()
    {
        var settings = GameSettingsLoader.Parse("""{"schemaVersion":1,"gameId":"steel-conveyor-war"}""");
        Assert.Equal(GameSettings.Default.TicksPerSecond, settings.TicksPerSecond);
    }

    [Fact]
    public void Tiles_ParseHappyPath()
    {
        var catalog = TileContentLoader.Parse(File.ReadAllText(FindConfigPath("tiles.json")));
        Assert.Contains("terrain.grass", catalog.Tiles.Keys);
        Assert.Contains("resource.iron_ore", catalog.Tiles.Keys);
        Assert.True(catalog.Tiles["terrain.grass"].Walkable);
    }

    [Fact]
    public void Tiles_RejectDuplicateIds()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "tiles": [
                { "id": "terrain.grass", "name": "A", "walkable": true },
                { "id": "terrain.grass", "name": "B", "walkable": true }
              ]
            }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => TileContentLoader.Parse(json));
        Assert.Contains("Duplicate tile id", ex.Message);
    }

    [Fact]
    public void Tiles_RejectEmptyCatalog()
    {
        Assert.Throws<InvalidOperationException>(() => TileContentLoader.Parse("""{"schemaVersion":1,"tiles":[]}"""));
    }

    [Fact]
    public void Entities_ParseHappyPath()
    {
        var catalog = EntityContentLoader.Parse(File.ReadAllText(FindConfigPath("entities.json")));
        Assert.True(catalog.Entities.ContainsKey("unit.commander"));
        Assert.Equal("Commander", catalog.Entities["unit.commander"].Kind);
        Assert.True(catalog.Entities["unit.commander"].BuildsStructures);
    }

    [Fact]
    public void Entities_RejectMissingKind()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "entities": [
                { "id": "unit.commander", "name": "Commander", "kind": "" }
              ]
            }
            """;
        Assert.Throws<InvalidOperationException>(() => EntityContentLoader.Parse(json));
    }

    [Fact]
    public void Research_RejectsInvalidJson()
    {
        Assert.ThrowsAny<Exception>(() => ResearchContentLoader.Parse("{ not-json"));
    }

    [Fact]
    public void CreateNewGame_StoresLoadedCatalogs()
    {
        var tiles = TileContentLoader.Parse(File.ReadAllText(FindConfigPath("tiles.json")));
        var entities = EntityContentLoader.Parse(File.ReadAllText(FindConfigPath("entities.json")));
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(
            9,
            ResearchProfileIds.MvpB,
            MvpResearchCatalog.CreateEmbedded(),
            tiles,
            entities));
        Assert.Equal(tiles.Tiles.Count, simulation.TileCatalog.Tiles.Count);
        Assert.Equal(entities.Entities.Count, simulation.EntityCatalog.Entities.Count);
        Assert.Contains("unit.commander", simulation.EntityCatalog.Entities.Keys);
    }

    private static string FindConfigPath(string fileName)
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", fileName)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config", fileName)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "config", fileName))
        };
        return candidates.First(File.Exists);
    }
}
