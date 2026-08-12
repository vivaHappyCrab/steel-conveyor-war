namespace SteelConveyorWar.Core.Tests;

public sealed class SpatialQueryIndexTests
{
    [Fact]
    public void QueryByPositionInEuclideanRange_YieldsAscendingIdWithinTiles()
    {
        var a = new WorldEntity(3, EntityKind.LightBot, new TilePosition(5, 5), new PlayerId(1));
        var b = new WorldEntity(1, EntityKind.LightBot, new TilePosition(5, 5), new PlayerId(1));
        var c = new WorldEntity(2, EntityKind.LightBot, new TilePosition(6, 5), new PlayerId(2));
        var index = SpatialQueryIndex.Build([a, b, c]);

        var ids = index.QueryByPositionInEuclideanRange(new TilePosition(5, 5), 1)
            .Select(entity => entity.Id)
            .ToArray();

        Assert.Equal([1, 3, 2], ids);
    }

    [Fact]
    public void Relocate_MovesEntityBetweenPositionBuckets()
    {
        var unit = new WorldEntity(10, EntityKind.BasicTank, new TilePosition(4, 4), new PlayerId(1));
        var index = SpatialQueryIndex.Build([unit]);

        Assert.Contains(index.QueryByPositionInEuclideanRange(new TilePosition(4, 4), 0), entity => entity.Id == 10);

        index.Relocate(unit, from: new TilePosition(4, 4), to: new TilePosition(7, 7));
        unit.Position = new TilePosition(7, 7);

        Assert.DoesNotContain(index.QueryByPositionInEuclideanRange(new TilePosition(4, 4), 0), entity => entity.Id == 10);
        Assert.Contains(index.QueryByPositionInEuclideanRange(new TilePosition(7, 7), 0), entity => entity.Id == 10);
    }

    [Fact]
    public void Rebuild_ClearsStaleEntries()
    {
        var first = new WorldEntity(1, EntityKind.Wall, new TilePosition(2, 2), new PlayerId(1));
        var index = SpatialQueryIndex.Build([first]);
        var second = new WorldEntity(2, EntityKind.Wall, new TilePosition(3, 3), new PlayerId(1));
        index.Rebuild([second], GameplayTablesCatalog.Embedded);

        Assert.Empty(index.QueryByPositionInEuclideanRange(new TilePosition(2, 2), 0));
        Assert.Contains(index.QueryByPositionInEuclideanRange(new TilePosition(3, 3), 0), entity => entity.Id == 2);
    }
}
