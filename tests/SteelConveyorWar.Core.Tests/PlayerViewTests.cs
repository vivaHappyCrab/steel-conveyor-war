namespace SteelConveyorWar.Core.Tests;

public sealed class PlayerViewTests
{
    [Fact]
    public void FairView_DoesNotExposeHiddenEnemyEntities()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        var blueCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == blue);
        var redCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == red);

        Assert.Equal(VisibilityState.Unknown, simulation.GetVisibility(blue, redCommander.Position));

        var fair = simulation.CreatePlayerView(blue, PlayerObservationMode.Fair);

        Assert.Contains(fair.GetVisibleEntities(), entity => entity.Id == blueCommander.Id);
        Assert.DoesNotContain(fair.GetVisibleEntities(), entity => entity.Id == redCommander.Id);
        Assert.Null(fair.GetVisibleEntity(redCommander.Id));
        Assert.False(fair.IsEntityVisible(redCommander));
    }

    [Fact]
    public void CheatView_ExposesHiddenEnemyEntities()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var redCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));

        Assert.Equal(VisibilityState.Unknown, simulation.GetVisibility(blue, redCommander.Position));

        var cheat = simulation.CreatePlayerView(blue, PlayerObservationMode.Cheat);

        Assert.Contains(cheat.GetVisibleEntities(), entity => entity.Id == redCommander.Id);
        Assert.NotNull(cheat.GetVisibleEntity(redCommander.Id));
        Assert.True(cheat.IsEntityVisible(redCommander));
    }

    [Fact]
    public void FairView_HidesUnknownTerrain_ButExposesVisibleTerrain()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var blueCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == blue);
        var redCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));

        var fair = simulation.CreatePlayerView(blue);

        Assert.True(fair.TryGetTerrain(blueCommander.Position, out var visibleTerrain));
        Assert.Equal(simulation.World.GetTerrain(blueCommander.Position), visibleTerrain);
        Assert.Equal(VisibilityState.Visible, fair.GetVisibility(blueCommander.Position));

        Assert.False(fair.TryGetTerrain(redCommander.Position, out _));
        Assert.Equal(VisibilityState.Unknown, fair.GetVisibility(redCommander.Position));
    }

    [Fact]
    public void FairView_ExposesEnemyEntity_WhenTileIsVisible()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        var blueCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == blue);
        var redCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == red);

        Assert.True(simulation.TryTeleportEntityForTests(redCommander.Id, new TilePosition(blueCommander.Position.X + 1, blueCommander.Position.Y)));
        simulation.AdvanceTick();

        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(blue, redCommander.Position));

        var fair = simulation.CreatePlayerView(blue, PlayerObservationMode.Fair);
        Assert.True(fair.IsEntityVisible(redCommander));
        Assert.NotNull(fair.GetVisibleEntity(redCommander.Id));
        Assert.Contains(fair.GetVisibleEntities(), entity => entity.Id == redCommander.Id);
    }
}
