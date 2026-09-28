using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class T2ResearchPaceTests
{
    [Fact]
    public void T2Effort_PillarsTakeThreeMinutesAndTheRestTwo()
    {
        var catalog = MvpResearchCatalog.CreateEmbedded();
        var tier = catalog.Technologies.Values.Where(tech => tech.TierId == ResearchTierIds.T2).ToList();
        Assert.Equal(18, tier.Count);

        foreach (var tech in tier)
        {
            var expected = tech.Id == TechnologyId.MetallurgyII
                || tech.Id == TechnologyId.SupplyII
                || tech.Id == TechnologyId.CommandII
                ? 180
                : 120;
            Assert.Equal(expected, tech.Cost.EffortUnits);
        }

        Assert.Equal(180, catalog.Technologies[TechnologyId.ProductionI].Cost.EffortUnits);
        Assert.Equal(90, catalog.Technologies[new TechnologyId("technology.t1.power-reserve")].Cost.EffortUnits);
    }

    [Fact]
    public void T2PillarEffects_StayOnTheirOriginalStats()
    {
        var catalog = MvpResearchCatalog.CreateEmbedded();

        var metallurgy = Effects(catalog, TechnologyId.MetallurgyII);
        Assert.Single(metallurgy);
        Assert.Equal(ResearchStatIds.SmelterWorkTicks, metallurgy[0].StatId);
        Assert.Equal(ModifierOperation.Multiply, metallurgy[0].Operation);
        Assert.Equal(9_000, metallurgy[0].ValueBasisPoints);

        var supply = Effects(catalog, TechnologyId.SupplyII);
        Assert.Single(supply);
        Assert.Equal(ResearchStatIds.ConveyorMoveTicks, supply[0].StatId);
        Assert.Equal(ModifierOperation.Multiply, supply[0].Operation);
        Assert.Equal(8_500, supply[0].ValueBasisPoints);

        var command = Effects(catalog, TechnologyId.CommandII);
        Assert.Equal(2, command.Count);
        Assert.Contains(command, effect =>
            effect.StatId == ResearchStatIds.ConstructionTicks
            && effect.Operation == ModifierOperation.Multiply
            && effect.ValueBasisPoints == 9_000);
        Assert.Contains(command, effect =>
            effect.StatId == ResearchStatIds.BastionTemplateCapacity
            && effect.Operation == ModifierOperation.Add
            && effect.ValueBasisPoints == 60_000);
    }

    [Fact]
    public void ResearchJson_MatchesEmbeddedT2Effort()
    {
        var embedded = MvpResearchCatalog.CreateEmbedded();
        var loaded = ResearchContentLoader.Parse(File.ReadAllText(FindConfigPath("research.json")));

        foreach (var tech in embedded.Technologies.Values.Where(tech => tech.TierId == ResearchTierIds.T2))
        {
            Assert.Equal(tech.Cost.EffortUnits, loaded.Technologies[tech.Id].Cost.EffortUnits);
            Assert.Equal(tech.Effects.Count, loaded.Technologies[tech.Id].Effects.Count);
        }
    }

    private static List<AddModifierEffect> Effects(ResearchCatalog catalog, TechnologyId id) =>
        catalog.Technologies[id].Effects.OfType<AddModifierEffect>().ToList();

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
