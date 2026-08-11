using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

/// <summary>
/// R21: SessionState transitions without an SFML window.
/// </summary>
public sealed class SessionStateTests
{
    [Fact]
    public void ResearchOverlay_OpenClose_ClearsSelectionAndScroll()
    {
        var state = new SessionState();
        state.OpenResearchOverlay();
        state.SetResearchSelectedId(TechnologyId.LightBot);
        state.RecordResearchClick(TechnologyId.LightBot, nowSeconds: 1f);
        state.SetResearchScrollY(40f);

        Assert.True(state.IsResearchOverlayOpen);
        Assert.Equal(TechnologyId.LightBot, state.ResearchSelectedId);

        state.CloseResearchOverlay();

        Assert.False(state.IsResearchOverlayOpen);
        Assert.Null(state.ResearchSelectedId);
        Assert.Null(state.ResearchLastClickId);
        Assert.Equal(0f, state.ResearchScrollY);
    }

    [Fact]
    public void ToggleResearchOverlay_OpensThenCloses()
    {
        var state = new SessionState();
        state.ToggleResearchOverlay();
        Assert.True(state.IsResearchOverlayOpen);

        state.ToggleResearchOverlay();
        Assert.False(state.IsResearchOverlayOpen);
    }

    [Fact]
    public void ToggleBuildMenu_OpensWithDefaultKind_AndClosesClearingPending()
    {
        var state = new SessionState(initialSelectedEntityId: 7);
        var defaultKind = EntityKind.Conveyor;

        state.ToggleBuildMenu(defaultKind);
        Assert.True(state.IsBuildMenuOpen);
        Assert.Equal(defaultKind, state.PendingBuildKind);
        Assert.Equal(Direction.East, state.PendingDirection);
        Assert.Null(state.PendingRecipe);
        Assert.Equal(0, state.RecipePage);

        state.ToggleBuildMenu(defaultKind);
        Assert.False(state.IsBuildMenuOpen);
        Assert.Null(state.PendingBuildKind);
    }

    [Fact]
    public void DemolishHold_BeginAdvanceCommit_AndClear()
    {
        var state = new SessionState();
        state.ToggleBuildMenu(EntityKind.Conveyor);
        state.BeginDemolishHold(targetEntityId: 42);

        Assert.Equal(42, state.DemolishHoldEntityId);
        Assert.Equal(0f, state.DemolishHoldElapsed);
        Assert.False(state.DemolishHoldCommitted);

        state.AdvanceDemolishHold(0.25f);
        Assert.Equal(0.25f, state.DemolishHoldElapsed);

        state.MarkDemolishHoldCommitted();
        Assert.True(state.DemolishHoldCommitted);

        state.ClearDemolishHold();
        Assert.Null(state.DemolishHoldEntityId);
        Assert.Equal(0f, state.DemolishHoldElapsed);
        Assert.False(state.DemolishHoldCommitted);
    }

    [Fact]
    public void ToggleBuildMenu_ClearsActiveDemolishHold()
    {
        var state = new SessionState();
        state.ToggleBuildMenu(EntityKind.Conveyor);
        state.BeginDemolishHold(99);
        state.AdvanceDemolishHold(0.1f);

        state.ToggleBuildMenu(EntityKind.Conveyor);

        Assert.False(state.IsBuildMenuOpen);
        Assert.Null(state.DemolishHoldEntityId);
        Assert.Equal(0f, state.DemolishHoldElapsed);
    }

    [Fact]
    public void PrepareOpenResearchExclusive_ClosesEnergyAndBastionComposition()
    {
        var state = new SessionState();
        state.OpenEnergyOverlay();
        state.ToggleBastionComposition();
        Assert.True(state.IsEnergyOverlayOpen);
        Assert.True(state.IsBastionCompositionOpen);

        state.PrepareOpenResearchExclusive();
        state.OpenResearchOverlay();

        Assert.True(state.IsResearchOverlayOpen);
        Assert.False(state.IsEnergyOverlayOpen);
        Assert.False(state.IsBastionCompositionOpen);
    }

    [Fact]
    public void ApplyPlayfieldSelection_ClearsBuildDemolishAndBastionPending_ClosesCompositionOnChange()
    {
        var state = new SessionState(initialSelectedEntityId: 1);
        state.ToggleBuildMenu(EntityKind.Assembler);
        state.BeginDemolishHold(99);
        state.SetBastionPendingMode(BastionPendingInputMode.AttackTarget);
        state.PatrolWaypoints.Add(new TilePosition(1, 1));
        state.ToggleBastionComposition();

        state.ApplyPlayfieldSelection(1);
        Assert.True(state.IsBastionCompositionOpen);

        state.ApplyPlayfieldSelection(2);
        Assert.Equal(2, state.SelectedEntityId);
        Assert.False(state.IsBuildMenuOpen);
        Assert.Null(state.PendingBuildKind);
        Assert.Null(state.DemolishHoldEntityId);
        Assert.Equal(BastionPendingInputMode.None, state.BastionPendingMode);
        Assert.Empty(state.PatrolWaypoints);
        Assert.False(state.IsBastionCompositionOpen);
    }

    [Fact]
    public void ClearTransientUiKeepingSelection_PreservesSelection()
    {
        var state = new SessionState(initialSelectedEntityId: 7);
        state.ToggleBuildMenu(EntityKind.Inserter);
        state.BeginDemolishHold(3);
        state.ToggleBastionComposition();
        state.SetBastionPendingMode(BastionPendingInputMode.PatrolWaypoints);

        state.ClearTransientUiKeepingSelection();

        Assert.Equal(7, state.SelectedEntityId);
        Assert.False(state.IsBuildMenuOpen);
        Assert.Null(state.DemolishHoldEntityId);
        Assert.False(state.IsBastionCompositionOpen);
        Assert.Equal(BastionPendingInputMode.None, state.BastionPendingMode);
    }
}
