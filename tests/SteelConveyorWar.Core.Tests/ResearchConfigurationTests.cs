using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class ResearchConfigurationTests
{
    [Fact]
    public void EmbeddedCatalog_ValidatesAndContainsAllProfiles()
    {
        var catalog = MvpResearchCatalog.CreateEmbedded();
        Assert.False(string.IsNullOrWhiteSpace(catalog.ContentHash));
        Assert.Contains(ResearchProfileIds.MvpA, catalog.Profiles.Keys);
        Assert.Contains(ResearchProfileIds.MvpB, catalog.Profiles.Keys);
        Assert.Contains(ResearchProfileIds.MvpC, catalog.Profiles.Keys);
        Assert.Contains(ResearchProfileIds.HybridAC, catalog.Profiles.Keys);
        Assert.True(catalog.Technologies.ContainsKey(TechnologyId.LightBot));
        Assert.True(catalog.Technologies.ContainsKey(TechnologyId.ProductionI));
    }

    [Fact]
    public void SerializeRoundTrip_PreservesTechnologyAndProfileCounts()
    {
        var embedded = MvpResearchCatalog.CreateEmbedded();
        var json = ResearchContentLoader.Serialize(embedded);
        var parsed = ResearchContentLoader.Parse(json);
        Assert.Equal(embedded.Technologies.Count, parsed.Technologies.Count);
        Assert.Equal(embedded.Profiles.Count, parsed.Profiles.Count);
        Assert.Contains(ResearchProfileIds.HybridAC, parsed.Profiles.Keys);
    }

    [Fact]
    public void ResearchJson_ParsesWhenPresent()
    {
        var path = FindConfigPath("research.json");
        Assert.True(File.Exists(path), $"Missing {path}");
        var embedded = MvpResearchCatalog.CreateEmbedded();
        var catalog = ResearchContentLoader.Parse(File.ReadAllText(path));
        Assert.Contains(ResearchProfileIds.MvpB, catalog.Profiles.Keys);
        Assert.Contains(ResearchProfileIds.MvpA, catalog.Profiles.Keys);
        Assert.Contains(ResearchProfileIds.MvpC, catalog.Profiles.Keys);
        Assert.Contains(ResearchProfileIds.HybridAC, catalog.Profiles.Keys);
        Assert.Equal(embedded.Technologies.Count, catalog.Technologies.Count);
        Assert.False(string.IsNullOrWhiteSpace(catalog.ContentHash));
    }

    [Fact]
    public void CreateNewGame_UsesSelectedProfile()
    {
        var catalog = MvpResearchCatalog.CreateEmbedded();
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(7, ResearchProfileIds.MvpC, catalog));
        Assert.Equal(ResearchProfileIds.MvpC, simulation.ResearchProfile.Id);
        Assert.Equal(2, simulation.GetResearchSnapshot(new PlayerId(1)).Tracks.Count);
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
