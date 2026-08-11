namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R34: cross-catalog references must resolve at load time and every loader must share one
/// schema-version policy, so incompatible or inconsistent content fails fast instead of being
/// silently (partially) applied.
/// </summary>
public sealed class ContentCrossValidationTests
{
    private static ResearchCatalog Research() => MvpResearchCatalog.CreateEmbedded();

    [Fact]
    public void ConsistentCatalogs_PassCrossValidation()
    {
        // The embedded catalogs ship together and must be mutually consistent.
        ContentCrossValidator.Validate(
            Research(),
            MvpBuildCostCatalog.Embedded,
            EntityCatalog.Empty,
            TileCatalog.Empty);
    }

    [Fact]
    public void EmptyCatalogs_ContributeNoReferences_AndPass()
    {
        ContentCrossValidator.Validate(
            Research(),
            BuildCostCatalog.Empty,
            EntityCatalog.Empty,
            TileCatalog.Empty);
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
            ContentCrossValidator.Validate(Research(), buildCosts, EntityCatalog.Empty, TileCatalog.Empty));
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
            ContentCrossValidator.Validate(Research(), BuildCostCatalog.Empty, entities, TileCatalog.Empty));
        Assert.Contains("NotARealEntityKind", ex.Message);
    }

    [Fact]
    public void SchemaPolicy_AcceptsCurrentVersion_RejectsOutOfRange()
    {
        // Current is valid; zero/negative and above-current are not.
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
}
