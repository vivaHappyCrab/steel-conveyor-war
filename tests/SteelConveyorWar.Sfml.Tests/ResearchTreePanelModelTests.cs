using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

public sealed class ResearchTreePanelModelTests
{
    [Fact]
    public void FromSnapshot_BuildsColoredNodesAndDetails()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var snapshot = simulation.GetResearchSnapshot(new PlayerId(1));
        var bounds = new FloatRect(new Vector2f(10f, 40f), new Vector2f(800f, 500f));
        var tree = ResearchTreePanelModel.FromSnapshot(snapshot, bounds, selectedId: null);

        Assert.NotEmpty(tree.Nodes);
        Assert.Contains(tree.Nodes, node => node.Status == ResearchTreeNodeStatus.Available);
        Assert.False(tree.CanStartSelected);
        Assert.False(tree.CanCancelSelected);

        var pick = tree.Nodes[0];
        var withSelection = ResearchTreePanelModel.FromSnapshot(snapshot, bounds, pick.Id);
        Assert.Equal(pick.Id, withSelection.SelectedId);
        Assert.Contains(withSelection.ToDetailLines(), line => line.Contains("Tech:", StringComparison.Ordinal));
        Assert.Contains(withSelection.ToDetailLines(), line => line.Contains("Effort:", StringComparison.Ordinal));
    }

    [Fact]
    public void FromSnapshot_ActiveTechEnablesCancelAction()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var player = new PlayerId(1);
        Assert.True(simulation.TryStartResearch(player, TechnologyId.LightBot));

        var snapshot = simulation.GetResearchSnapshot(player);
        var bounds = new FloatRect(new Vector2f(10f, 40f), new Vector2f(800f, 500f));
        var tree = ResearchTreePanelModel.FromSnapshot(snapshot, bounds, TechnologyId.LightBot);

        Assert.True(tree.CanCancelSelected);
        Assert.False(tree.CanStartSelected);
        Assert.Equal("Cancel research", tree.ActionButtonLabel);
    }
}
