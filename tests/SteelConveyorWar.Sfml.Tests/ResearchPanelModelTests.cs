using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

public sealed class ResearchPanelModelTests
{
    [Fact]
    public void FromSnapshot_PagesAvailableTechnologiesAndShowsTracks()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var snapshot = simulation.GetResearchSnapshot(new PlayerId(1));
        var panel = ResearchPanelModel.FromSnapshot(snapshot, pageIndex: 0, pageSize: 4);

        Assert.Equal(ResearchProfileIds.MvpB, panel.ProfileId);
        Assert.Equal(ResearchTierIds.T1, panel.CurrentTierId);
        Assert.NotEmpty(panel.TrackLines);
        Assert.NotEmpty(panel.GateLines);
        Assert.True(panel.PageCount >= 1);
        Assert.True(panel.PageEntries.Count <= 4);
        Assert.False(panel.SupportsAllocationToggle);

        var lines = panel.ToHudLines().ToList();
        Assert.Contains(lines, line => line.Contains("Research", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("tier=", StringComparison.Ordinal));
    }

    [Fact]
    public void FromSnapshot_ProfileC_ExposesAllocationToggleAndExclusivePreview()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpC, MvpResearchCatalog.CreateEmbedded()));
        var snapshot = simulation.GetResearchSnapshot(new PlayerId(1));
        var panel = ResearchPanelModel.FromSnapshot(snapshot, pageIndex: 0);

        Assert.True(panel.SupportsAllocationToggle);
        Assert.Equal(2, snapshot.Tracks.Count);
        Assert.Contains(panel.ToHudLines(), line => line.Contains("Alloc:", StringComparison.Ordinal));
        Assert.DoesNotContain(panel.ToHudLines(), line => line.Contains("[T]", StringComparison.Ordinal));
    }

    [Fact]
    public void FromSnapshot_ClampsPageIndex()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var snapshot = simulation.GetResearchSnapshot(new PlayerId(1));
        var panel = ResearchPanelModel.FromSnapshot(snapshot, pageIndex: 99, pageSize: 3);

        Assert.Equal(panel.PageCount - 1, panel.PageIndex);
    }
}
