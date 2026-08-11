using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

// R10: the versioned content manifest must fold in every gameplay catalog so peers with divergent
// build costs / entity loss-conditions mismatch at tick 0 instead of silently diverging later.
public sealed class ContentManifestHashTests
{
    private static BuildCostCatalog WithMineCost(int ironPlate)
    {
        var embedded = MvpBuildCostCatalog.Embedded;
        var costs = embedded.Costs.ToDictionary(pair => pair.Key, pair => pair.Value);
        costs[EntityKind.Mine] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = ironPlate };
        return embedded with { Costs = costs };
    }

    private static GameSimulation GameWithBuildCosts(BuildCostCatalog buildCosts)
    {
        return GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, BuildCosts = buildCosts });
    }

    [Fact]
    public void BuildCostIdentity_DiffersWhenCostChanges()
    {
        var a = SimulationContentManifest.ComputeBuildCostIdentity(WithMineCost(20));
        var b = SimulationContentManifest.ComputeBuildCostIdentity(WithMineCost(21));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void BuildCostIdentity_IsStableForEqualContent()
    {
        var a = SimulationContentManifest.ComputeBuildCostIdentity(WithMineCost(20));
        var b = SimulationContentManifest.ComputeBuildCostIdentity(WithMineCost(20));
        Assert.Equal(a, b);
    }

    [Fact]
    public void EntityIdentity_DiffersWhenLossConditionChanges()
    {
        var baseline = new EntityCatalog(1, new Dictionary<string, EntityDefinition>
        {
            ["commander"] = new("commander", "Commander", "Commander", false, "Defeat")
        });
        var changed = new EntityCatalog(1, new Dictionary<string, EntityDefinition>
        {
            ["commander"] = new("commander", "Commander", "Commander", false, null)
        });

        Assert.NotEqual(
            SimulationContentManifest.ComputeEntityIdentity(baseline),
            SimulationContentManifest.ComputeEntityIdentity(changed));
    }

    [Fact]
    public void Manifest_IsEqualForIdenticalContent()
    {
        var a = GameWithBuildCosts(MvpBuildCostCatalog.Embedded);
        var b = GameWithBuildCosts(MvpBuildCostCatalog.Embedded);
        Assert.Equal(SimulationContentManifest.Compute(a), SimulationContentManifest.Compute(b));
    }

    [Fact]
    public void Manifest_DiffersForDivergentBuildCosts()
    {
        var a = GameWithBuildCosts(WithMineCost(20));
        var b = GameWithBuildCosts(WithMineCost(21));
        Assert.NotEqual(SimulationContentManifest.Compute(a), SimulationContentManifest.Compute(b));
    }

    [Fact]
    public void EnsureMatch_Throws_OnDivergentCatalogs()
    {
        var a = GameWithBuildCosts(WithMineCost(20));
        var b = GameWithBuildCosts(WithMineCost(21));
        var ex = Assert.Throws<ContentManifestMismatchException>(() => SimulationContentManifest.EnsureMatch(a, b));
        Assert.NotEqual(ex.LocalHash, ex.RemoteHash);
    }

    [Fact]
    public void EnsureMatch_Passes_ForIdenticalCatalogs()
    {
        var a = GameWithBuildCosts(MvpBuildCostCatalog.Embedded);
        var b = GameWithBuildCosts(MvpBuildCostCatalog.Embedded);
        SimulationContentManifest.EnsureMatch(a, b); // must not throw
    }

    [Fact]
    public void StateHash_DiffersAtTickZero_ForDivergentBuildCosts()
    {
        var a = GameWithBuildCosts(WithMineCost(20));
        var b = GameWithBuildCosts(WithMineCost(21));
        Assert.NotEqual(SimulationStateHasher.Compute(a), SimulationStateHasher.Compute(b));
    }

    [Fact]
    public void GameplayTablesIdentity_DiffersWhenStatsChange()
    {
        var a = SimulationContentManifest.ComputeGameplayTablesIdentity(GameplayTablesCatalog.Embedded);
        var changedStats = GameplayTablesCatalog.Embedded.EntityStats.ToDictionary(pair => pair.Key, pair => pair.Value);
        changedStats[EntityKind.Commander] = GameplayTablesCatalog.Embedded.GetStats(EntityKind.Commander) with { MaxHealth = 301 };
        var b = SimulationContentManifest.ComputeGameplayTablesIdentity(
            GameplayTablesCatalog.Embedded with { EntityStats = changedStats });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Manifest_DiffersForDivergentGameplayTables()
    {
        var changedStats = GameplayTablesCatalog.Embedded.EntityStats.ToDictionary(pair => pair.Key, pair => pair.Value);
        changedStats[EntityKind.Commander] = GameplayTablesCatalog.Embedded.GetStats(EntityKind.Commander) with { MaxHealth = 301 };
        var overridden = GameplayTablesCatalog.Embedded with { EntityStats = changedStats };

        var a = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42 });
        var b = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 42, GameplayTables = overridden });
        Assert.NotEqual(SimulationContentManifest.Compute(a), SimulationContentManifest.Compute(b));
    }
}
