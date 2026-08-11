using System.Text.Json.Nodes;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

// R28: dangerous research-content values must be rejected at load/validation time,
// and the inventory must never grow when handed a negative amount.
public sealed class ResearchContentValidationTests
{
    private static JsonObject EmbeddedAsJson()
    {
        var json = ResearchContentLoader.Serialize(MvpResearchCatalog.CreateEmbedded());
        return (JsonObject)JsonNode.Parse(json)!;
    }

    private static JsonObject FirstTechnology(JsonObject root)
    {
        return (JsonObject)((JsonArray)root["technologies"]!)[0]!;
    }

    [Fact]
    public void Baseline_EmbeddedCatalog_Parses()
    {
        // Sanity: the unmutated embedded catalog validates. Guards the negative tests below.
        var root = EmbeddedAsJson();
        var parsed = ResearchContentLoader.Parse(root.ToJsonString());
        Assert.NotEmpty(parsed.Technologies);
    }

    [Fact]
    public void SciencePack_NonPositiveAmount_IsRejected()
    {
        var root = EmbeddedAsJson();
        var pack = (JsonObject)((JsonArray)FirstTechnology(root)["cost"]!["sciencePacks"]!)[0]!;
        pack["amount"] = 0;

        var ex = Assert.Throws<InvalidOperationException>(() => ResearchContentLoader.Parse(root.ToJsonString()));
        Assert.Contains("positive amount", ex.Message);
    }

    [Fact]
    public void SciencePack_DuplicateItem_IsRejected()
    {
        var root = EmbeddedAsJson();
        var packs = (JsonArray)FirstTechnology(root)["cost"]!["sciencePacks"]!;
        var first = (JsonObject)packs[0]!;
        packs.Add(new JsonObject
        {
            ["item"] = first["item"]!.GetValue<string>(),
            ["amount"] = 1
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ResearchContentLoader.Parse(root.ToJsonString()));
        Assert.Contains("duplicate science pack", ex.Message);
    }

    [Fact]
    public void Profile_NegativeDefaultAllocation_IsRejected()
    {
        var root = EmbeddedAsJson();
        var profile = (JsonObject)((JsonArray)root["profiles"]!)[0]!;
        var allocations = (JsonObject)profile["schedule"]!["budget"]!["defaultAllocations"]!;
        var firstKey = allocations.First().Key;
        allocations[firstKey] = -1;

        var ex = Assert.Throws<InvalidOperationException>(() => ResearchContentLoader.Parse(root.ToJsonString()));
        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public void Gate_UnknownTargetTier_IsRejected()
    {
        var root = EmbeddedAsJson();
        JsonObject? mutatedGate = null;
        foreach (var profileNode in (JsonArray)root["profiles"]!)
        {
            var gates = profileNode!["gates"] as JsonArray;
            if (gates is { Count: > 0 })
            {
                mutatedGate = (JsonObject)gates[0]!;
                break;
            }
        }

        Assert.NotNull(mutatedGate);
        mutatedGate!["targetTierId"] = "tier.does-not-exist";

        var ex = Assert.Throws<InvalidOperationException>(() => ResearchContentLoader.Parse(root.ToJsonString()));
        Assert.Contains("unknown targetTier", ex.Message);
    }

    [Fact]
    public void Inventory_TryRemove_NegativeAmount_DoesNotIncreaseCount()
    {
        var inventory = new Inventory();
        inventory.Add(ItemId.IronPlate, 10);

        var removed = inventory.TryRemove(ItemId.IronPlate, -5);

        Assert.False(removed);
        Assert.Equal(10, inventory.Count(ItemId.IronPlate));
    }

    [Fact]
    public void Inventory_TryRemoveAll_IsAtomic_NoPartialConsumption()
    {
        // Models the duplicate/aggregate cost path: if any line is unaffordable, nothing is consumed.
        var inventory = new Inventory();
        inventory.Add(ItemId.IronPlate, 5);
        inventory.Add(ItemId.CopperPlate, 1);

        var costs = new Dictionary<ItemId, int>
        {
            [ItemId.IronPlate] = 3,
            [ItemId.CopperPlate] = 2 // more than available -> whole removal must fail
        };

        var removed = inventory.TryRemoveAll(costs);

        Assert.False(removed);
        Assert.Equal(5, inventory.Count(ItemId.IronPlate));
        Assert.Equal(1, inventory.Count(ItemId.CopperPlate));
    }
}
