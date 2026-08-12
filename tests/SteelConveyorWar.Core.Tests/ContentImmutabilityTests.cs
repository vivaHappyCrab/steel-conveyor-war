using System.Collections.Frozen;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// H07: authoritative content catalogs must deep-freeze public dictionary/list graphs so callers
/// cannot cast-mutate after load (or via Empty / Embedded / record <c>with</c>).
/// </summary>
public sealed class ContentImmutabilityTests
{
    [Fact]
    public void EntityCatalog_NotCastMutable()
    {
        var catalog = EntityContentLoader.Parse("""
            {
              "schemaVersion": 1,
              "entities": [
                { "id": "unit.commander", "name": "Commander", "kind": "Commander", "buildsStructures": false, "lossCondition": "Defeat" }
              ]
            }
            """);

        Assert.IsAssignableFrom<FrozenDictionary<string, EntityDefinition>>(catalog.Entities);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, EntityDefinition>)catalog.Entities).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, EntityDefinition>)catalog.Entities)["unit.commander"] =
                catalog.Entities["unit.commander"] with { LossCondition = null });
    }

    [Fact]
    public void TileCatalog_NotCastMutable()
    {
        var catalog = TileContentLoader.Parse("""
            {
              "schemaVersion": 1,
              "tiles": [
                { "id": "terrain.grass", "name": "Grass", "walkable": true }
              ]
            }
            """);

        Assert.IsAssignableFrom<FrozenDictionary<string, TileDefinition>>(catalog.Tiles);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, TileDefinition>)catalog.Tiles).Clear());
    }

    [Fact]
    public void BuildCostNestedCosts_NotCastMutable()
    {
        var catalog = MvpBuildCostCatalog.Embedded;

        Assert.IsAssignableFrom<FrozenDictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>>(catalog.Costs);
        Assert.IsAssignableFrom<FrozenDictionary<ItemId, int>>(catalog.Costs[EntityKind.Mine]);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>)catalog.Costs).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)catalog.Costs[EntityKind.Mine])[ItemId.IronPlate] = 1);
    }

    [Fact]
    public void GameplayRecipeInputs_NotCastMutable()
    {
        var catalog = GameplayTablesCatalog.Embedded;
        var recipe = catalog.ProductionRecipes[EntityKind.BasicTank];
        var itemRecipe = catalog.ItemRecipes.Values.First();

        Assert.IsAssignableFrom<FrozenDictionary<EntityKind, ProductionRecipe>>(catalog.ProductionRecipes);
        Assert.IsAssignableFrom<FrozenDictionary<ItemId, int>>(recipe.Inputs);
        Assert.IsAssignableFrom<FrozenDictionary<ItemId, int>>(itemRecipe.Inputs);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)recipe.Inputs)[ItemId.IronPlate] = 1);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)itemRecipe.Inputs).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ResistanceEntry>)catalog.Resistances).Clear());
    }

    [Fact]
    public void ResearchCatalog_NotCastMutable()
    {
        var catalog = MvpResearchCatalog.CreateEmbedded();
        var tech = catalog.Technologies.Values.First();
        var profile = catalog.Profiles.Values.First();

        Assert.IsAssignableFrom<FrozenDictionary<TechnologyId, TechnologyDefinition>>(catalog.Technologies);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<TechnologyId, TechnologyDefinition>)catalog.Technologies).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)tech.Tags).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, TierGateDefinition>)profile.GatesByFromTier).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, int>)profile.Schedule.Budget.DefaultAllocations).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)catalog.BaselineCapabilities).Add("x"));
    }

    [Fact]
    public void MapSettings_Players_NotCastMutable()
    {
        var map = MapSettings.Default1v1;
        Assert.Throws<NotSupportedException>(() =>
            ((IList<MapPlayerDefinition>)map.Players).Clear());
    }

    [Fact]
    public void Manifest_RemainsStable_WhenCallerAttemptsMutation()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var before = SimulationContentManifest.Compute(simulation);

        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>)simulation.BuildCostCatalog.Costs).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)simulation.BuildCostCatalog.Costs[EntityKind.Mine])[ItemId.IronPlate] = 1);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<EntityKind, int>)simulation.GameplayTables.PowerDemand).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)simulation.GameplayTables.ProductionRecipes[EntityKind.BasicTank].Inputs)
                [ItemId.IronPlate] = 1);

        Assert.Equal(before, SimulationContentManifest.Compute(simulation));
    }

    [Fact]
    public void WithPlusNewDictionary_StillProducesDistinctImmutableInstance()
    {
        var baseline = MvpBuildCostCatalog.Embedded;
        var costs = baseline.Costs.ToDictionary(pair => pair.Key, pair => pair.Value);
        costs[EntityKind.Mine] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 99 };
        var overridden = baseline with { Costs = costs };

        Assert.IsAssignableFrom<FrozenDictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>>(overridden.Costs);
        Assert.IsAssignableFrom<FrozenDictionary<ItemId, int>>(overridden.Costs[EntityKind.Mine]);
        Assert.Equal(99, overridden.Costs[EntityKind.Mine][ItemId.IronPlate]);
        Assert.NotEqual(
            SimulationContentManifest.ComputeBuildCostIdentity(baseline),
            SimulationContentManifest.ComputeBuildCostIdentity(overridden));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)overridden.Costs[EntityKind.Mine])[ItemId.IronPlate] = 1);
    }
}
