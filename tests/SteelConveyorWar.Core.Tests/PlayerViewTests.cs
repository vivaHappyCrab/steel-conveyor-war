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
        Assert.False(fair.IsEntityVisible(redCommander.Id));
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
        Assert.True(cheat.IsEntityVisible(redCommander.Id));
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
        Assert.True(fair.IsEntityVisible(redCommander.Id));
        Assert.NotNull(fair.GetVisibleEntity(redCommander.Id));
        Assert.Contains(fair.GetVisibleEntities(), entity => entity.Id == redCommander.Id);
    }

    [Fact]
    public void RetainedEnemySnapshot_DoesNotReflectLaterChanges_WhenEntityReentersFog()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        var blueCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == blue);
        var redCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == red);

        // Bring the enemy into view next to the blue commander so a fair snapshot can be captured.
        Assert.True(simulation.TryTeleportEntityForTests(redCommander.Id, new TilePosition(blueCommander.Position.X + 1, blueCommander.Position.Y)));
        simulation.AdvanceTick();

        var fair = simulation.CreatePlayerView(blue, PlayerObservationMode.Fair);
        var retained = fair.GetVisibleEntity(redCommander.Id);
        Assert.NotNull(retained);
        var frozenPosition = retained!.Position;
        var frozenTick = retained.ObservationTick;

        // Move the enemy far away (back into fog) and advance the simulation.
        Assert.True(simulation.TryTeleportEntityForTests(redCommander.Id, redCommander.Position));
        var farAway = new TilePosition(
            Math.Min(simulation.World.Size.Width - 1, blueCommander.Position.X + 20),
            blueCommander.Position.Y);
        Assert.True(simulation.TryTeleportEntityForTests(redCommander.Id, farAway));
        simulation.AdvanceTick();

        // The retained snapshot must not reflect the new position or tick — values are frozen at capture.
        Assert.Equal(frozenPosition, retained.Position);
        Assert.Equal(frozenTick, retained.ObservationTick);
        Assert.NotEqual(farAway, retained.Position);
    }

    [Fact]
    public void EnemySnapshot_DoesNotExposeOwnerOnlyDetail()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        var blueCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == blue);
        var redCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == red);

        Assert.True(simulation.TryTeleportEntityForTests(redCommander.Id, new TilePosition(blueCommander.Position.X + 1, blueCommander.Position.Y)));
        simulation.AdvanceTick();

        var fair = simulation.CreatePlayerView(blue, PlayerObservationMode.Fair);
        var enemy = fair.GetVisibleEntity(redCommander.Id);
        Assert.NotNull(enemy);
        Assert.False(enemy!.IsOwn);
        Assert.Null(enemy.Own);
    }

    [Fact]
    public void OwnSnapshot_ExposesEconomyDetail()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var blueCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == blue);

        var fair = simulation.CreatePlayerView(blue, PlayerObservationMode.Fair);
        var own = fair.GetVisibleEntity(blueCommander.Id);
        Assert.NotNull(own);
        Assert.True(own!.IsOwn);
        Assert.NotNull(own.Own);
        Assert.NotNull(own.Own!.Inventory);
    }

    [Fact]
    public void CaptureSnapshot_FreezesVisibleEntitiesAtCurrentTick()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);

        var fair = simulation.CreatePlayerView(blue, PlayerObservationMode.Fair);
        var snapshot = fair.CaptureSnapshot();

        Assert.Equal(blue, snapshot.ObserverId);
        Assert.Equal(PlayerObservationMode.Fair, snapshot.Mode);
        Assert.Equal(simulation.Tick, snapshot.ObservationTick);
        Assert.All(snapshot.VisibleEntities, entity => Assert.Equal(snapshot.ObservationTick, entity.ObservationTick));

        // Advancing the simulation must not mutate the retained snapshot's entity list.
        var capturedCount = snapshot.VisibleEntities.Count;
        simulation.AdvanceTick();
        Assert.Equal(capturedCount, snapshot.VisibleEntities.Count);
    }
}
