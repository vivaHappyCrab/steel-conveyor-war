using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class FieldArtilleryTests
{
    [Fact]
    public void Cannon_RequiresFieldArtilleryResearch()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var tile = GrassNear(simulation, 3, 1);
        Assert.False(simulation.TryPlaceGhostBuild(player, EntityKind.CannonTurret, tile, out _));

        Assert.True(simulation.TryForceCompleteResearch(player, TechnologyId.FieldArtillery, confirmExclusive: true));
        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.CannonTurret, tile, out _));
        Assert.Equal(20, simulation.GameplayTables.GetStats(EntityKind.CannonTurret).AttackDamage);
        Assert.Equal(28, simulation.GameplayTables.GetStats(EntityKind.CannonTurret).AttackCooldownTicks);
    }

    [Fact]
    public void FieldArtilleryRecipe_IsLockedUntilResearch()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.TankFactory, GrassNear(simulation, 6, 0), player, out var factoryId));
        Assert.False(simulation.TrySetFactoryProduction(factoryId, player, EntityKind.FieldArtillery));

        Assert.True(simulation.TryForceCompleteResearch(player, TechnologyId.FieldArtillery, confirmExclusive: true));
        Assert.True(simulation.TrySetFactoryProduction(factoryId, player, EntityKind.FieldArtillery));
    }

    [Fact]
    public void Shell_MissesLightBotThatLeavesSplash_AndHitsTankAndWall()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 11);
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        Assert.True(TryFindClearRow(simulation, 14, out var botRow));
        Assert.True(TryFindClearRow(simulation, 14, out var tankRow, apartFromY: botRow.Y));
        Assert.True(TryFindClearRow(simulation, 14, out var wallRow, apartFromY: botRow.Y, alsoApartFromY: tankRow.Y));

        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.FieldArtillery, botRow, blue, out var botGunId));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.LightBot, new TilePosition(botRow.X + 6, botRow.Y), red, out var botId));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.FieldArtillery, tankRow, blue, out _));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.BasicTank, new TilePosition(tankRow.X + 6, tankRow.Y), red, out var tankId));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.FieldArtillery, wallRow, blue, out _));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, new TilePosition(wallRow.X + 6, wallRow.Y), red, out var wallId));

        var bot = simulation.World.GetEntity(botId)!;
        var tank = simulation.World.GetEntity(tankId)!;
        bot.Order = new BastionOrder(BastionOrderKind.AttackArea, new TilePosition(botRow.X, botRow.Y));
        tank.Order = new BastionOrder(BastionOrderKind.AttackArea, new TilePosition(tankRow.X, tankRow.Y));
        bot.IsGarrisoned = false;
        tank.IsGarrisoned = false;

        simulation.AdvanceTick();
        Assert.Equal(3, simulation.ArtilleryShots.Count);
        Assert.Equal(35, bot.Health);
        Assert.Equal(90, tank.Health);
        Assert.Equal(180, simulation.World.GetEntity(wallId)!.Health);

        var flight = simulation.GameplayTables.GetStats(EntityKind.FieldArtillery).ProjectileFlightTicks;
        for (var i = 0; i < flight; i++)
        {
            simulation.AdvanceTick();
        }

        Assert.Empty(simulation.ArtilleryShots);
        Assert.Equal(35, simulation.World.GetEntity(botId)!.Health);
        Assert.Equal(90 - 19, simulation.World.GetEntity(tankId)!.Health);
        Assert.Equal(180 - 18, simulation.World.GetEntity(wallId)!.Health);
        Assert.True(simulation.World.GetEntity(botGunId)!.IsAlive);
    }

    [Fact]
    public void FieldArtillery_DoesNotFireInsideMinimumRange()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 11);
        Assert.True(TryFindClearRow(simulation, 8, out var row));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.FieldArtillery, row, new PlayerId(1), out _));
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.LightBot, new TilePosition(row.X + 2, row.Y), new PlayerId(2), out var botId));

        for (var i = 0; i < 5; i++)
        {
            simulation.AdvanceTick();
        }

        Assert.Empty(simulation.ArtilleryShots);
        Assert.Equal(35, simulation.World.GetEntity(botId)!.Health);
    }

    [Fact]
    public void FieldArtillery_KeepsMovingWhenOnlyEnemyIsInsideMinimumRange()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 11);
        Assert.True(TryFindClearRow(simulation, 12, out var row));
        var gunTile = row;
        var destination = new TilePosition(row.X + 8, row.Y);
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.FieldArtillery, gunTile, new PlayerId(1), out var gunId));
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.LightBot, new TilePosition(row.X + 2, row.Y), new PlayerId(2), out _));

        var gun = simulation.World.GetEntity(gunId)!;
        gun.Order = new BastionOrder(BastionOrderKind.AttackArea, destination);
        gun.IsGarrisoned = false;

        for (var i = 0; i < 40; i++)
        {
            simulation.AdvanceTick();
        }

        Assert.Empty(simulation.ArtilleryShots);
        Assert.NotEqual(gunTile, simulation.World.GetEntity(gunId)!.Position);
    }

    private static TilePosition GrassNear(GameSimulation simulation, int dx, int dy)
    {
        var commander = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        return new TilePosition(commander.Position.X + dx, commander.Position.Y + dy);
    }

    private static bool TryFindClearRow(
        GameSimulation simulation,
        int length,
        out TilePosition start,
        int? apartFromY = null,
        int? alsoApartFromY = null)
    {
        var world = simulation.World;
        for (var y = 2; y < world.Size.Height - 2; y++)
        {
            if (apartFromY is int first && Math.Abs(y - first) < 8)
            {
                continue;
            }

            if (alsoApartFromY is int second && Math.Abs(y - second) < 8)
            {
                continue;
            }

            for (var x = 8; x < world.Size.Width / 2 - length; x++)
            {
                var clear = true;
                for (var i = 0; i < length; i++)
                {
                    var tile = new TilePosition(x + i, y);
                    if (!world.IsInside(tile)
                        || world.GetTerrain(tile) != TerrainType.Grass
                        || world.GetEntitiesAt(tile).Any(entity => entity.IsAlive))
                    {
                        clear = false;
                        break;
                    }
                }

                if (clear)
                {
                    start = new TilePosition(x, y);
                    return true;
                }
            }
        }

        start = default;
        return false;
    }
}
