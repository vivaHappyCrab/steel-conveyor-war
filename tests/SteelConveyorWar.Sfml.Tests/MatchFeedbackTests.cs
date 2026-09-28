using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

public sealed class MatchFeedbackTests
{
    [Fact]
    public void GhostPreviewTone_FollowsPlacement()
    {
        Assert.Equal(GhostPreviewTone.Place, GhostPreviewStyle.From(new BuildPlacementReport(true, false, BuildPlacementBlock.None)));
        Assert.Equal(GhostPreviewTone.Walk, GhostPreviewStyle.From(new BuildPlacementReport(false, true, BuildPlacementBlock.OutOfRange)));
        Assert.Equal(GhostPreviewTone.Deny, GhostPreviewStyle.From(new BuildPlacementReport(false, false, BuildPlacementBlock.Occupied)));
    }

    [Fact]
    public void PlaceLabel_NamesTheBlock()
    {
        Assert.Equal("Place: ok", GhostPreviewStyle.PlaceLabel(new BuildPlacementReport(true, false, BuildPlacementBlock.None)));
        Assert.Equal(
            "Place: cannot afford",
            GhostPreviewStyle.PlaceLabel(new BuildPlacementReport(false, false, BuildPlacementBlock.Unaffordable)));
    }

    [Fact]
    public void MatchOutcome_UsesTheLocalTeam()
    {
        Assert.Null(MatchOutcome.Label(GameStatus.InProgress, 1, 1));
        Assert.Equal("Draw", MatchOutcome.Label(GameStatus.Draw, null, 1));
        Assert.Equal("Victory", MatchOutcome.Label(GameStatus.PlayerWon, 1, 1));
        Assert.Equal("Defeat", MatchOutcome.Label(GameStatus.PlayerWon, 2, 1));
    }

    [Fact]
    public void ProductionStallLines_NamePowerOutputAndInput()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == player);
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.Smelter,
            new TilePosition(commander.Position.X + 3, commander.Position.Y),
            player,
            out var smelterId));
        var smelter = simulation.World.GetEntity(smelterId)!;
        smelter.EnergyBuffer = 0;
        Assert.Contains("No power", HudOverlay.ProductionStallLines(smelter, simulation, player));

        simulation.TrySetEnergyBufferForTests(smelterId, smelter.EnergyBufferCapacity);
        smelter.PendingOutputItem = ItemId.IronPlate;
        smelter.WorkTicksRemaining = 0;
        Assert.Contains("Output full", HudOverlay.ProductionStallLines(smelter, simulation, player));

        smelter.PendingOutputItem = null;
        smelter.ActiveSmeltRecipe = SmeltRecipeId.IronPlate;
        Assert.Contains("Input empty", HudOverlay.ProductionStallLines(smelter, simulation, player));
    }
}
