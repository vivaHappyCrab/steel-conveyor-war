namespace SteelConveyorWar.Core.Tests;

public sealed class CombatSpatialIndexTests
{
    [Fact]
    public void ProcessCombat_SplashDamagesNearbyHostile_NotFriendly()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var enemyCommander = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.TryTeleportEntityForTests(enemyCommander.Id, new TilePosition(2, 2)));

        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.MediumTank,
            new TilePosition(40, 40),
            new PlayerId(1),
            out var tankId));
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.LightBot,
            new TilePosition(41, 40),
            new PlayerId(2),
            out var primaryId));
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.LightBot,
            new TilePosition(42, 40),
            new PlayerId(2),
            out var splashHostileId));
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.Wall,
            new TilePosition(41, 41),
            new PlayerId(1),
            out var friendlyId));

        var primary = simulation.World.GetEntity(primaryId)!;
        var splashHostile = simulation.World.GetEntity(splashHostileId)!;
        var friendly = simulation.World.GetEntity(friendlyId)!;
        var primaryBefore = primary.Health;
        var splashBefore = splashHostile.Health;
        var friendlyBefore = friendly.Health;

        var expectedPrimary = simulation.ComputeCombatDamageForTests(tankId, primaryId);
        var expectedSplash = simulation.ComputeCombatDamageForTests(tankId, splashHostileId);
        Assert.True(expectedPrimary > 0);
        Assert.True(expectedSplash > 0);
        Assert.True(MvpDefinitions.GetStats(EntityKind.MediumTank).SplashRadius > 0);

        simulation.AdvanceTick();

        Assert.Equal(primaryBefore - expectedPrimary, primary.Health);
        Assert.Equal(splashBefore - expectedSplash, splashHostile.Health);
        Assert.Equal(friendlyBefore, friendly.Health);
    }

    [Fact]
    public void ProcessCombat_WallBlocksGroundToGround_UsesSpatialWallLookup()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));

        Assert.True(simulation.TryTeleportEntityForTests(attacker.Id, new TilePosition(50, 50)));
        Assert.True(simulation.TryTeleportEntityForTests(defender.Id, new TilePosition(52, 50)));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, new TilePosition(51, 50), new PlayerId(1), out _));

        var healthBefore = defender.Health;
        simulation.AdvanceTick();

        Assert.Equal(healthBefore, defender.Health);
        Assert.True(attacker.AttackCooldownRemaining > 0);
    }
}
