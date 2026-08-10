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
        Assert.Equal("research.json", settings.ResearchContentFile);
        Assert.Equal(ResearchProfileIds.MvpB, settings.ResearchProfileId);
        Assert.Equal("maps/default.json", settings.MapContentFile);
        Assert.True(settings.Window.Width > 0);
        Assert.True(settings.Window.Height > 0);
        Assert.False(string.IsNullOrWhiteSpace(settings.Window.Title));
    }

    [Fact]
    public void GameSettings_RejectsMissingGameId()
    {
        Assert.Throws<InvalidOperationException>(() => GameSettingsLoader.Parse("""{"schemaVersion":1,"gameId":""}"""));
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
        Assert.Equal("Defeat", catalog.Entities["unit.commander"].LossCondition);
        Assert.Equal(new HashSet<EntityKind> { EntityKind.Commander }, catalog.GetDefeatLossKinds());
    }

    [Fact]
    public void Entities_RejectUnknownKind()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "entities": [
                { "id": "unit.mystery", "name": "Mystery", "kind": "NotARealKind" }
              ]
            }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => EntityContentLoader.Parse(json));
        Assert.Contains("unknown kind", ex.Message);
    }

    [Fact]
    public void Entities_RejectUnsupportedLossCondition()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "entities": [
                { "id": "unit.commander", "name": "Commander", "kind": "Commander", "lossCondition": "Resign" }
              ]
            }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => EntityContentLoader.Parse(json));
        Assert.Contains("unsupported lossCondition", ex.Message);
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

    [Fact]
    public void LossCondition_FromEntityCatalog_DrivesVictory()
    {
        var entities = EntityContentLoader.Parse(File.ReadAllText(FindConfigPath("entities.json")));
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(
            42,
            ResearchProfileIds.MvpB,
            MvpResearchCatalog.CreateEmbedded(),
            TileCatalog.Empty,
            entities));
        var enemyCommander = simulation.World.Entities.Single(
            entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));

        simulation.DamageEntity(enemyCommander.Id, enemyCommander.Health);

        Assert.Equal(GameStatus.PlayerWon, simulation.Status);
        Assert.Equal(new PlayerId(1), simulation.WinnerId);
    }

    [Fact]
    public void LossCondition_AbsentInCatalog_DoesNotDefeatOnCommanderDeath()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "entities": [
                {
                  "id": "unit.commander",
                  "name": "Armored Mobile Commander",
                  "kind": "Commander",
                  "buildsStructures": true,
                  "lossCondition": null
                }
              ]
            }
            """;
        var entities = EntityContentLoader.Parse(json);
        Assert.Empty(entities.GetDefeatLossKinds());

        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(
            42,
            ResearchProfileIds.MvpB,
            MvpResearchCatalog.CreateEmbedded(),
            TileCatalog.Empty,
            entities));
        var enemyCommander = simulation.World.Entities.Single(
            entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));

        simulation.DamageEntity(enemyCommander.Id, enemyCommander.Health);

        Assert.Equal(GameStatus.InProgress, simulation.Status);
        Assert.Null(simulation.WinnerId);
        Assert.False(simulation.Players.Single(player => player.Id == new PlayerId(2)).IsDefeated);
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
