namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R34 / M03: cross-catalog references must resolve at load time and every loader must share one
/// schema-version policy, so incompatible or inconsistent content fails fast instead of being
/// silently (partially) applied.
/// </summary>
public sealed class ContentCrossValidationTests
{
    private static ResearchCatalog Research() => MvpResearchCatalog.CreateEmbedded();

    [Fact]
    public void ConsistentCatalogs_PassCrossValidation()
    {
        ContentCrossValidator.Validate(
            Research(),
            MvpBuildCostCatalog.Embedded,
            EntityCatalog.Empty,
            TileCatalog.Empty,
            GameplayTablesCatalog.Embedded);
    }

    [Fact]
    public void EmptyCatalogs_ContributeNoReferences_AndPass()
    {
        ContentCrossValidator.Validate(
            Research(),
            BuildCostCatalog.Empty,
            EntityCatalog.Empty,
            TileCatalog.Empty,
            GameplayTablesCatalog.Empty);
    }

    [Fact]
    public void BuildCost_ReferencingUnknownTechnology_IsRejected()
    {
        var buildCosts = new BuildCostCatalog(
            SchemaVersion: 1,
            Costs: new Dictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>
            {
                [EntityKind.Mine] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 5 },
            },
            BuildTicks: new Dictionary<EntityKind, int> { [EntityKind.Mine] = 30 },
            Requirements: new Dictionary<EntityKind, TechnologyId>
            {
                [EntityKind.Mine] = new TechnologyId("technology.does-not-exist"),
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                Research(),
                buildCosts,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                GameplayTablesCatalog.Empty));
        Assert.Contains("technology.does-not-exist", ex.Message);
    }

    [Fact]
    public void ProductionRecipe_ReferencingUnknownTechnology_IsRejected()
    {
        var tables = GameplayTablesCatalog.Embedded with
        {
            ProductionRecipes = new Dictionary<EntityKind, ProductionRecipe>
            {
                [EntityKind.LightBot] = new(
                    new Dictionary<ItemId, int> { [ItemId.IronPlate] = 5 },
                    EntityKind.LightBot,
                    60,
                    new TechnologyId("technology.does-not-exist"))
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                Research(),
                BuildCostCatalog.Empty,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                tables));
        Assert.Contains("technology.does-not-exist", ex.Message);
    }

    [Fact]
    public void Entity_WithDefeatLossButUnknownKind_IsRejected()
    {
        var entities = new EntityCatalog(
            SchemaVersion: 1,
            Entities: new Dictionary<string, EntityDefinition>
            {
                ["ghost"] = new EntityDefinition("ghost", "Ghost", "NotARealEntityKind", false, "Defeat"),
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                Research(),
                BuildCostCatalog.Empty,
                entities,
                TileCatalog.Empty,
                GameplayTablesCatalog.Empty));
        Assert.Contains("NotARealEntityKind", ex.Message);
    }

    [Fact]
    public void ResearchUnlock_UnknownContentKind_IsRejected()
    {
        var techId = new TechnologyId("technology.test.bad-unlock");
        var research = Research() with
        {
            Technologies = new Dictionary<TechnologyId, TechnologyDefinition>(Research().Technologies)
            {
                [techId] = new TechnologyDefinition(
                    techId,
                    ResearchTierIds.T1,
                    new ResearchCostDefinition(10, [new SciencePackCost(ItemId.SciencePackT1, 1)]),
                    [new UnlockContentEffect("spaceship", "WarpDrive")],
                    ["test"],
                    "Bad Unlock",
                    "test")
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                research,
                BuildCostCatalog.Empty,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                GameplayTablesCatalog.Embedded));
        Assert.Contains("spaceship", ex.Message);
    }

    [Fact]
    public void ResearchUnlock_UnknownEntity_IsRejected()
    {
        var techId = new TechnologyId("technology.test.bad-entity");
        var research = Research() with
        {
            Technologies = new Dictionary<TechnologyId, TechnologyDefinition>(Research().Technologies)
            {
                [techId] = new TechnologyDefinition(
                    techId,
                    ResearchTierIds.T1,
                    new ResearchCostDefinition(10, [new SciencePackCost(ItemId.SciencePackT1, 1)]),
                    [new UnlockContentEffect("entity", "DefinitelyNotAnEntity")],
                    ["test"],
                    "Bad Entity",
                    "test")
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                research,
                BuildCostCatalog.Empty,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                GameplayTablesCatalog.Embedded));
        Assert.Contains("DefinitelyNotAnEntity", ex.Message);
    }

    [Fact]
    public void ResearchUnlock_UnknownItemRecipe_IsRejected()
    {
        var techId = new TechnologyId("technology.test.bad-item-recipe");
        var research = Research() with
        {
            Technologies = new Dictionary<TechnologyId, TechnologyDefinition>(Research().Technologies)
            {
                [techId] = new TechnologyDefinition(
                    techId,
                    ResearchTierIds.T1,
                    new ResearchCostDefinition(10, [new SciencePackCost(ItemId.SciencePackT1, 1)]),
                    [new UnlockContentEffect("item-recipe", "NotARealRecipe")],
                    ["test"],
                    "Bad Item Recipe",
                    "test")
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                research,
                BuildCostCatalog.Empty,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                GameplayTablesCatalog.Embedded));
        Assert.Contains("NotARealRecipe", ex.Message);
    }

    [Fact]
    public void ResearchUnlock_RecipeMissingFromGameplayTables_IsRejected()
    {
        var techId = new TechnologyId("technology.test.missing-recipe");
        var research = Research() with
        {
            Technologies = new Dictionary<TechnologyId, TechnologyDefinition>(Research().Technologies)
            {
                [techId] = new TechnologyDefinition(
                    techId,
                    ResearchTierIds.T1,
                    new ResearchCostDefinition(10, [new SciencePackCost(ItemId.SciencePackT1, 1)]),
                    [UnlockContentEffect.Recipe(EntityKind.Wall.ToString())],
                    ["test"],
                    "Missing Recipe",
                    "test")
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                research,
                BuildCostCatalog.Empty,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                GameplayTablesCatalog.Embedded));
        Assert.Contains("Wall", ex.Message);
        Assert.Contains("productionRecipes", ex.Message);
    }

    [Fact]
    public void BuildCost_MissingEntityStats_IsRejected()
    {
        var buildCosts = new BuildCostCatalog(
            SchemaVersion: 1,
            Costs: new Dictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>
            {
                [EntityKind.Wall] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 1 },
            },
            BuildTicks: new Dictionary<EntityKind, int> { [EntityKind.Wall] = 1 },
            Requirements: new Dictionary<EntityKind, TechnologyId>());

        var tables = GameplayTablesCatalog.Embedded with
        {
            EntityStats = GameplayTablesCatalog.Embedded.EntityStats
                .Where(pair => pair.Key != EntityKind.Wall)
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                Research(),
                buildCosts,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                tables));
        Assert.Contains("Wall", ex.Message);
        Assert.Contains("entityStats", ex.Message);
    }

    [Fact]
    public void MapStarts_OutsideWorld_AreRejected()
    {
        var map = new MapSettings(
            1,
            "bad",
            [
                new MapPlayerDefinition(
                    1,
                    "Blue",
                    1,
                    new TilePosition(500, 500),
                    new TilePosition(501, 500),
                    new TilePosition(502, 500))
            ]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                Research(),
                BuildCostCatalog.Empty,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                GameplayTablesCatalog.Empty,
                map));
        Assert.Contains("outside world", ex.Message);
    }

    [Fact]
    public void MapStarts_OverlappingFootprints_AreRejected()
    {
        var map = new MapSettings(
            1,
            "overlap",
            [
                new MapPlayerDefinition(
                    1,
                    "Blue",
                    1,
                    new TilePosition(10, 10),
                    new TilePosition(10, 10),
                    new TilePosition(10, 10))
            ]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContentCrossValidator.Validate(
                Research(),
                BuildCostCatalog.Empty,
                EntityCatalog.Empty,
                TileCatalog.Empty,
                GameplayTablesCatalog.Embedded,
                map));
        Assert.Contains("overlap", ex.Message);
    }

    [Fact]
    public void SchemaPolicy_AcceptsCurrentVersion_RejectsOutOfRange()
    {
        ContentSchema.RequireSupportedVersion("test", ContentSchema.CurrentVersion);

        Assert.Throws<InvalidOperationException>(
            () => ContentSchema.RequireSupportedVersion("test", 0));
        Assert.Throws<InvalidOperationException>(
            () => ContentSchema.RequireSupportedVersion("test", ContentSchema.CurrentVersion + 1));
    }

    [Fact]
    public void Loader_RejectsIncompatibleSchemaVersion_ViaSharedPolicy()
    {
        var json = $$"""
        {
            "schemaVersion": {{ContentSchema.CurrentVersion + 1}},
            "builds": [ { "kind": "Mine", "cost": { "IronPlate": 5 }, "buildTicks": 30 } ]
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() => BuildCostContentLoader.Parse(json));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Fact]
    public void ResearchLoader_RejectsMissingOrTooNewSchemaVersion()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ResearchContentLoader.Parse("""{"technologies":[],"tiers":[],"profiles":[]}"""));

        var tooNew = $$"""
        {
          "schemaVersion": {{ContentSchema.CurrentVersion + 1}},
          "technologies": [],
          "tiers": [],
          "profiles": []
        }
        """;
        var ex = Assert.Throws<InvalidOperationException>(() => ResearchContentLoader.Parse(tooNew));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Fact]
    public void Loader_RejectsUnknownJsonProperty()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "builds": [ { "kind": "Mine", "cost": { "IronPlate": 5 }, "buildTicks": 30 } ],
          "unexpectedField": true
        }
        """;

        Assert.ThrowsAny<Exception>(() => BuildCostContentLoader.Parse(json));
    }

    [Fact]
    public void Loader_RejectsUndefinedNumericEnumKind()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "builds": [ { "kind": "99999", "cost": { "IronPlate": 5 }, "buildTicks": 30 } ]
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() => BuildCostContentLoader.Parse(json));
        Assert.Contains("99999", ex.Message);
    }

    [Fact]
    public void GameplayTables_RejectNegativeCooldown()
    {
        var embedded = File.ReadAllText(FindConfigPath("gameplay-tables.json"));
        var corrupted = embedded.Replace(
            "\"attackCooldownTicks\": 25",
            "\"attackCooldownTicks\": -1",
            StringComparison.Ordinal);

        var ex = Assert.Throws<InvalidOperationException>(() => GameplayTablesLoader.Parse(corrupted));
        Assert.Contains(">= 0", ex.Message);
    }

    private static string FindConfigPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config", fileName),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", fileName)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config", fileName))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate config/{fileName}.");
    }
}
