namespace SteelConveyorWar.Core.Tests;

public sealed class GameWorldSpatialIndexTests
{
    [Fact]
    public void GetEntity_UsesIdIndex()
    {
        var world = CreateWorld(
            new WorldEntity(10, EntityKind.Wall, new TilePosition(1, 1)),
            new WorldEntity(20, EntityKind.Wall, new TilePosition(2, 2)));

        Assert.Equal(10, world.GetEntity(10)!.Id);
        Assert.Equal(20, world.GetEntity(20)!.Id);
        Assert.Null(world.GetEntity(99));
    }

    [Fact]
    public void GetTopEntityAt_PrefersHighestId()
    {
        var lower = new WorldEntity(1, EntityKind.Wall, new TilePosition(3, 3));
        var higher = new WorldEntity(5, EntityKind.Wall, new TilePosition(3, 3));
        var world = CreateWorld(lower, higher);

        Assert.Same(higher, world.GetTopEntityAt(new TilePosition(3, 3)));
    }

    [Fact]
    public void GetEntitiesAt_ReturnsStableAscendingIdOrder()
    {
        var first = new WorldEntity(1, EntityKind.Wall, new TilePosition(4, 4));
        var second = new WorldEntity(2, EntityKind.Wall, new TilePosition(4, 4));
        var third = new WorldEntity(3, EntityKind.Wall, new TilePosition(4, 4));
        // Constructor order intentionally not sorted by Id appearance in the list of matches alone.
        var world = CreateWorld(second, third, first);

        Assert.Equal([1, 2, 3], world.GetEntitiesAt(new TilePosition(4, 4)).Select(entity => entity.Id).ToArray());
    }

    [Fact]
    public void GetEntitiesAt_SkipsDeadAndGarrisoned()
    {
        var alive = new WorldEntity(1, EntityKind.Wall, new TilePosition(5, 5));
        var dead = new WorldEntity(2, EntityKind.Wall, new TilePosition(5, 5)) { Health = 0 };
        var garrisoned = new WorldEntity(3, EntityKind.BasicTank, new TilePosition(5, 5))
        {
            IsGarrisoned = true
        };
        var world = CreateWorld(alive, dead, garrisoned);

        Assert.Equal([1], world.GetEntitiesAt(new TilePosition(5, 5)).Select(entity => entity.Id).ToArray());
        Assert.Same(alive, world.GetTopEntityAt(new TilePosition(5, 5)));
    }

    [Fact]
    public void MultiTileFootprint_IndexesEveryOccupiedTile()
    {
        var hub = new WorldEntity(7, EntityKind.Hub, new TilePosition(1, 1));
        var world = CreateWorld(hub);

        foreach (var tile in GameWorld.GetFootprintTiles(EntityKind.Hub, hub.Position))
        {
            Assert.Contains(world.GetEntitiesAt(tile), entity => entity.Id == hub.Id);
        }

        Assert.Empty(world.GetEntitiesAt(new TilePosition(0, 0)));
        Assert.Empty(world.GetEntitiesAt(new TilePosition(3, 3)));
    }

    [Fact]
    public void GhostBuild_UsesBuildTargetFootprintForOccupancy()
    {
        var ghost = new WorldEntity(8, EntityKind.GhostBuild, new TilePosition(2, 2))
        {
            BuildTargetKind = EntityKind.Hub
        };
        var world = CreateWorld(ghost);

        foreach (var tile in GameWorld.GetFootprintTiles(EntityKind.Hub, ghost.Position))
        {
            Assert.Contains(world.GetEntitiesAt(tile), entity => entity.Id == ghost.Id);
        }
    }

    [Fact]
    public void TryTeleportEntityForTests_UpdatesOccupancyOnMove()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var commander = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var from = commander.Position;
        var to = new TilePosition(12, 12);

        Assert.Contains(simulation.World.GetEntitiesAt(from), entity => entity.Id == commander.Id);

        Assert.True(simulation.TryTeleportEntityForTests(commander.Id, to));

        Assert.DoesNotContain(simulation.World.GetEntitiesAt(from), entity => entity.Id == commander.Id);
        Assert.Contains(simulation.World.GetEntitiesAt(to), entity => entity.Id == commander.Id);
        Assert.Same(commander, simulation.World.GetTopEntityAt(to));
    }

    [Fact]
    public void RemoveDead_DropsEntityFromIdAndTileIndexes()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var wallPosition = new TilePosition(15, simulation.World.Size.Height / 2);
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, wallPosition, new PlayerId(1), out var wallId));
        Assert.Contains(simulation.World.GetEntitiesAt(wallPosition), entity => entity.Id == wallId);

        Assert.True(simulation.TrySetEntityHealthForTests(wallId, 0));
        simulation.AdvanceTick();

        Assert.Null(simulation.World.GetEntity(wallId));
        Assert.DoesNotContain(simulation.World.GetEntitiesAt(wallPosition), entity => entity.Id == wallId);
    }

    private static GameWorld CreateWorld(params WorldEntity[] entities)
    {
        const int width = 16;
        const int height = 16;
        var terrain = new TerrainType[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                terrain[x, y] = TerrainType.Grass;
            }
        }

        return new GameWorld(new WorldSize(width, height), terrain, entities);
    }
}
