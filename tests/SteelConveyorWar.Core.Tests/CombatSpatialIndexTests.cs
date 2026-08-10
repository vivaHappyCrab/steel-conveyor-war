namespace SteelConveyorWar.Core.Tests;

public sealed class CombatSpatialIndexTests
{
    [Fact]
    public void QueryByPositionInEuclideanRange_MatchesFullEntityScanOrdering()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var p1 = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var p2 = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.TryTeleportEntityForTests(p1.Id, new TilePosition(40, 40)));
        Assert.True(simulation.TryTeleportEntityForTests(p2.Id, new TilePosition(43, 40)));

        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.BasicTank,
            new TilePosition(41, 42),
            new PlayerId(1),
            out var tankId));

        var center = new TilePosition(40, 40);
        const int radius = 5;
        var spatial = CombatSpatialIndex.Build(simulation.World.Entities);

        var fromIndex = spatial.QueryByPositionInEuclideanRange(center, radius)
            .Where(entity => entity.IsAlive && !entity.IsGarrisoned && entity.OwnerId is not null)
            .OrderBy(entity => center.EuclideanDistanceSquared(entity.Position))
            .ThenBy(entity => entity.Id)
            .Select(entity => entity.Id)
            .ToList();

        var fromFullScan = simulation.World.Entities
            .Where(entity =>
                entity.IsAlive
                && !entity.IsGarrisoned
                && entity.OwnerId is not null
                && center.IsWithinEuclideanRange(entity.Position, radius))
            .OrderBy(entity => center.EuclideanDistanceSquared(entity.Position))
            .ThenBy(entity => entity.Id)
            .Select(entity => entity.Id)
            .ToList();

        Assert.Equal(fromFullScan, fromIndex);
        Assert.Contains(p1.Id, fromIndex);
        Assert.Contains(p2.Id, fromIndex);
        Assert.Contains(tankId, fromIndex);
    }

    [Fact]
    public void HasAlliedWallAt_DetectsWallOnFootprintTile()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var wallTile = new TilePosition(12, 12);
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, wallTile, new PlayerId(1), out _));

        var spatial = CombatSpatialIndex.Build(simulation.World.Entities);
        Assert.True(spatial.HasAlliedWallAt(wallTile, new PlayerId(1), simulation.AreAllied));
        Assert.False(spatial.HasAlliedWallAt(wallTile, new PlayerId(2), simulation.AreAllied));
        Assert.False(spatial.HasAlliedWallAt(new TilePosition(13, 12), new PlayerId(1), simulation.AreAllied));
    }
}
