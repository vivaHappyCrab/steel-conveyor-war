using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class CommanderAirTargetTests
{
    [Fact]
    public void Commander_DoesNotShootFlyingScout()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        var commander = simulation.World.Entities.Single(e => e.Kind == EntityKind.Commander && e.OwnerId == blue);
        Assert.Equal(600, commander.MaxHealth);
        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, new TilePosition(50, 50)));

        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Scout, new TilePosition(51, 50), red, out var scoutId));
        var scout = simulation.World.GetEntity(scoutId)!;
        var healthBefore = scout.Health;
        var cooldownBefore = commander.AttackCooldownRemaining;

        AdvanceTicks(simulation, MvpDefinitions.GetStats(EntityKind.Commander).AttackCooldownTicks + 2);

        scout = simulation.World.GetEntity(scoutId)!;
        commander = simulation.World.GetEntity(commander.Id)!;
        Assert.Equal(healthBefore, scout.Health);
        Assert.Equal(0, commander.AttackCooldownRemaining);
        Assert.True(cooldownBefore == 0 || commander.AttackCooldownRemaining <= cooldownBefore);
    }

    [Fact]
    public void Wall_IsBuildableWithoutResearch()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var tile = new TilePosition(8, simulation.World.Size.Height / 2);
        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.Wall, tile, out _));
    }

    [Fact]
    public void ConcreteWallsResearch_AddsBastionCap()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var before = simulation.GetMaxBastionCount(player);
        Assert.Equal(3, before);
        Assert.True(simulation.TryForceCompleteResearch(player, TechnologyId.ConcreteWalls, confirmExclusive: true));
        Assert.Equal(before + 1, simulation.GetMaxBastionCount(player));
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }
}
