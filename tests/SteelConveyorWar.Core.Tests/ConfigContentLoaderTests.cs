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
        Assert.Equal(GameSimulation.DefaultTicksPerSecond, settings.TicksPerSecond);
        Assert.Equal("research.json", settings.ResearchContentFile);
        Assert.Equal(ResearchProfileIds.MvpB, settings.ResearchProfileId);
        Assert.Equal("maps/default.json", settings.MapContentFile);
        Assert.Equal("build-costs.json", settings.BuildCostsContentFile);
        Assert.Equal("gameplay-tables.json", settings.GameplayTablesContentFile);
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
    public void BuildCosts_ParseHappyPathMatchesEmbedded()
    {
        var loaded = BuildCostContentLoader.Parse(File.ReadAllText(FindConfigPath("build-costs.json")));
        var embedded = MvpBuildCostCatalog.Embedded;

        Assert.Equal(embedded.Costs.Count, loaded.Costs.Count);
        foreach (var (kind, cost) in embedded.Costs)
        {
            Assert.True(loaded.Costs.TryGetValue(kind, out var loadedCost), $"Missing kind {kind}");
            Assert.Equal(cost.Count, loadedCost.Count);
            foreach (var (item, amount) in cost)
            {
                Assert.Equal(amount, loadedCost[item]);
            }

            Assert.Equal(embedded.BuildTicks[kind], loaded.BuildTicks[kind]);
        }

        Assert.Equal(embedded.Requirements.Count, loaded.Requirements.Count);
        foreach (var (kind, tech) in embedded.Requirements)
        {
            Assert.Equal(tech, loaded.Requirements[kind]);
        }
    }

    [Fact]
    public void BuildCosts_RejectDuplicateKind()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "builds": [
                { "kind": "Mine", "cost": { "IronPlate": 20 }, "buildTicks": 30 },
                { "kind": "Mine", "cost": { "IronPlate": 10 }, "buildTicks": 30 }
              ]
            }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => BuildCostContentLoader.Parse(json));
        Assert.Contains("Duplicate build kind", ex.Message);
    }

    [Fact]
    public void BuildCosts_RejectEmptyCatalog()
    {
        Assert.Throws<InvalidOperationException>(() => BuildCostContentLoader.Parse("""{"schemaVersion":1,"builds":[]}"""));
    }

    [Fact]
    public void GameplayTablesLoader_ParsesConfigEqualsEmbedded()
    {
        var loaded = GameplayTablesLoader.Parse(File.ReadAllText(FindConfigPath("gameplay-tables.json")));
        var embedded = GameplayTablesCatalog.Embedded;

        Assert.Equal(embedded.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(embedded.PowerDemand, loaded.PowerDemand);
        Assert.Equal(embedded.PowerProduction, loaded.PowerProduction);
        Assert.Equal(embedded.ItemStackSizes, loaded.ItemStackSizes);
        Assert.Equal(embedded.Footprints, loaded.Footprints);
        Assert.Equal(embedded.CollisionRadius, loaded.CollisionRadius);
        Assert.Equal(embedded.TechSignatureIntensity, loaded.TechSignatureIntensity);
        Assert.Equal(embedded.Resistances, loaded.Resistances);

        Assert.Equal(embedded.EntityStats.Count, loaded.EntityStats.Count);
        foreach (var (kind, stats) in embedded.EntityStats)
        {
            Assert.Equal(stats, loaded.GetStats(kind));
        }

        Assert.Equal(embedded.ProductionRecipes.Count, loaded.ProductionRecipes.Count);
        foreach (var (kind, recipe) in embedded.ProductionRecipes)
        {
            Assert.True(loaded.ProductionRecipes.TryGetValue(kind, out var loadedRecipe));
            Assert.Equal(recipe.OutputKind, loadedRecipe.OutputKind);
            Assert.Equal(recipe.WorkTicks, loadedRecipe.WorkTicks);
            Assert.Equal(recipe.RequiredTechnology, loadedRecipe.RequiredTechnology);
            Assert.Equal(recipe.Inputs, loadedRecipe.Inputs);
        }

        Assert.Equal(embedded.ItemRecipes.Count, loaded.ItemRecipes.Count);
        foreach (var (id, recipe) in embedded.ItemRecipes)
        {
            Assert.True(loaded.ItemRecipes.TryGetValue(id, out var loadedRecipe));
            Assert.Equal(recipe.OutputItem, loadedRecipe.OutputItem);
            Assert.Equal(recipe.OutputAmount, loadedRecipe.OutputAmount);
            Assert.Equal(recipe.WorkTicks, loadedRecipe.WorkTicks);
            Assert.Equal(recipe.Inputs, loadedRecipe.Inputs);
        }

        Assert.Equal(
            SimulationContentManifest.ComputeGameplayTablesIdentity(embedded),
            SimulationContentManifest.ComputeGameplayTablesIdentity(loaded));
    }

    [Fact]
    public void GameplayTables_RejectEmptyEntityStats()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GameplayTablesLoader.Parse("""{"schemaVersion":1,"entityStats":{}}"""));
    }

    [Fact]
    public void GameplayTables_RejectUnknownEntityKind()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "entityStats": {
                "NotARealKind": { "maxHealth": 10 }
              }
            }
            """;
        var ex = Assert.Throws<InvalidOperationException>(() => GameplayTablesLoader.Parse(json));
        Assert.Contains("unknown entity kind", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateNewGame_StoresLoadedCatalogs()
    {
        var tiles = TileContentLoader.Parse(File.ReadAllText(FindConfigPath("tiles.json")));
        var entities = EntityContentLoader.Parse(File.ReadAllText(FindConfigPath("entities.json")));
        var buildCosts = BuildCostContentLoader.Parse(File.ReadAllText(FindConfigPath("build-costs.json")));
        var gameplayTables = GameplayTablesLoader.Parse(File.ReadAllText(FindConfigPath("gameplay-tables.json")));
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(
            9,
            ResearchProfileIds.MvpB,
            MvpResearchCatalog.CreateEmbedded(),
            tiles,
            entities,
            BuildCosts: buildCosts,
            GameplayTables: gameplayTables));
        Assert.Equal(tiles.Tiles.Count, simulation.TileCatalog.Tiles.Count);
        Assert.Equal(entities.Entities.Count, simulation.EntityCatalog.Entities.Count);
        Assert.Contains("unit.commander", simulation.EntityCatalog.Entities.Keys);
        Assert.Equal(buildCosts.Costs.Count, simulation.BuildCostCatalog.Costs.Count);
        Assert.Equal(20, simulation.BuildCostCatalog.Costs[EntityKind.Mine][ItemId.IronPlate]);
        Assert.Equal(gameplayTables.EntityStats.Count, simulation.GameplayTables.EntityStats.Count);
        Assert.Equal(300, simulation.GameplayTables.GetStats(EntityKind.Commander).MaxHealth);
    }

    [Fact]
    public void CreateNewGame_GameplayTablesOverride_IsVisibleOnSimulation()
    {
        var embedded = GameplayTablesCatalog.Embedded;
        var stats = embedded.EntityStats.ToDictionary(pair => pair.Key, pair => pair.Value);
        stats[EntityKind.Commander] = embedded.GetStats(EntityKind.Commander) with { MaxHealth = 999 };
        var overridden = embedded with { EntityStats = stats };

        var simulation = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 11, GameplayTables = overridden });

        Assert.Equal(999, simulation.GameplayTables.GetStats(EntityKind.Commander).MaxHealth);
        // Static MvpDefinitions fallback stays on Embedded (parity for call sites without a match catalog).
        Assert.Equal(300, MvpDefinitions.GetStats(EntityKind.Commander).MaxHealth);
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
