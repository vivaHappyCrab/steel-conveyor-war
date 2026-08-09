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
        Assert.NotEmpty(tree.Edges);
        Assert.Contains(tree.Nodes, node => node.Status == ResearchTreeNodeStatus.Available);
        Assert.Contains(tree.Nodes, node => node.IsMandatory);
        Assert.Contains(tree.Nodes, node => !string.IsNullOrWhiteSpace(node.DisplayName) && !node.DisplayName.Contains("technology.", StringComparison.Ordinal));
        Assert.False(tree.CanStartSelected);
        Assert.False(tree.CanCancelSelected);
        Assert.True(tree.ContentHeight > 0f);
        Assert.True(tree.ContentViewport.Height > 0f);

        var pick = tree.Nodes[0];
        var withSelection = ResearchTreePanelModel.FromSnapshot(snapshot, bounds, pick.Id);
        Assert.Equal(pick.Id, withSelection.SelectedId);
        Assert.Contains(withSelection.ToDetailLines(), line => line == pick.DisplayName);
        Assert.Contains(withSelection.ToDetailLines(), line => line.StartsWith("Прогресс:", StringComparison.Ordinal));
        Assert.Contains(withSelection.ToDetailLines(), line => line.Contains(pick.Description, StringComparison.Ordinal));
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

    [Fact]
    public void TryPickNode_IgnoresClicksOutsideContentViewport()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var snapshot = simulation.GetResearchSnapshot(new PlayerId(1));
        var bounds = new FloatRect(new Vector2f(10f, 40f), new Vector2f(400f, 280f));
        var tree = ResearchTreePanelModel.FromSnapshot(snapshot, bounds, selectedId: null);
        Assert.NotEmpty(tree.Nodes);

        var outside = new Vector2i(
            (int)(tree.ContentViewport.Left + 4),
            (int)(tree.OverlayBounds.Top + 4));
        Assert.False(tree.TryPickNode(outside, scrollOffsetY: 0f, out _));

        var first = tree.Nodes[0];
        var inside = new Vector2i(
            (int)(tree.ContentViewport.Left + first.Bounds.Left + first.Bounds.Width / 2f),
            (int)(tree.ContentViewport.Top + first.Bounds.Top + first.Bounds.Height / 2f));
        Assert.True(tree.TryPickNode(inside, scrollOffsetY: 0f, out var picked));
        Assert.Equal(first.Id, picked);
    }

    [Fact]
    public void ClampScroll_LimitsToContentOverflow()
    {
        Assert.Equal(0f, ResearchTreePanelModel.ClampScroll(-20f, contentHeight: 1000f, viewportHeight: 200f));
        Assert.Equal(0f, ResearchTreePanelModel.ClampScroll(50f, contentHeight: 100f, viewportHeight: 200f));
        Assert.Equal(800f, ResearchTreePanelModel.ClampScroll(9999f, contentHeight: 1000f, viewportHeight: 200f));
        Assert.Equal(120f, ResearchTreePanelModel.ClampScroll(120f, contentHeight: 1000f, viewportHeight: 200f));
    }

    [Fact]
    public void TryPickNode_AppliesScrollOffset()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var snapshot = simulation.GetResearchSnapshot(new PlayerId(1));
        var bounds = new FloatRect(new Vector2f(10f, 40f), new Vector2f(400f, 220f));
        var tree = ResearchTreePanelModel.FromSnapshot(snapshot, bounds, selectedId: null);
        Assert.True(tree.ContentHeight > tree.ContentViewport.Height);

        var deep = tree.Nodes.OrderByDescending(node => node.Bounds.Top).First();
        var scroll = ResearchTreePanelModel.ClampScroll(
            deep.Bounds.Top,
            tree.ContentHeight,
            tree.ContentViewport.Height);
        var screen = new Vector2i(
            (int)(tree.ContentViewport.Left + deep.Bounds.Left + deep.Bounds.Width / 2f),
            (int)(tree.ContentViewport.Top + (deep.Bounds.Top - scroll) + deep.Bounds.Height / 2f));

        Assert.True(tree.TryPickNode(screen, scroll, out var picked));
        Assert.Equal(deep.Id, picked);
        Assert.False(tree.TryPickNode(screen, scrollOffsetY: 0f, out _));
    }
}
