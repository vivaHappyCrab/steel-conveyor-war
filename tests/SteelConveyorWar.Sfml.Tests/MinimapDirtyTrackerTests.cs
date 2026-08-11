using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

/// <summary>
/// R33: MinimapDirtyTracker decides CachedOnly / PatchTiles / FullRebuild without an SFML window.
/// </summary>
public sealed class MinimapDirtyTrackerTests
{
    private static readonly PlayerId P1 = new(1);
    private static readonly PlayerId P2 = new(2);

    [Fact]
    public void NeedsFullRebuild_WhenNoCache_OrSizePlayerMapChanges()
    {
        Assert.True(MinimapDirtyTracker.NeedsFullRebuild(
            hasCache: false,
            cacheSize: 140,
            currentSize: 140,
            cacheMapWidth: 10,
            cacheMapHeight: 10,
            mapWidth: 10,
            mapHeight: 10,
            cachePlayer: P1,
            currentPlayer: P1,
            framesSinceFullRebuild: 0));

        Assert.True(MinimapDirtyTracker.NeedsFullRebuild(
            hasCache: true,
            cacheSize: 140,
            currentSize: 160,
            cacheMapWidth: 10,
            cacheMapHeight: 10,
            mapWidth: 10,
            mapHeight: 10,
            cachePlayer: P1,
            currentPlayer: P1,
            framesSinceFullRebuild: 0));

        Assert.True(MinimapDirtyTracker.NeedsFullRebuild(
            hasCache: true,
            cacheSize: 140,
            currentSize: 140,
            cacheMapWidth: 10,
            cacheMapHeight: 10,
            mapWidth: 12,
            mapHeight: 10,
            cachePlayer: P1,
            currentPlayer: P1,
            framesSinceFullRebuild: 0));

        Assert.True(MinimapDirtyTracker.NeedsFullRebuild(
            hasCache: true,
            cacheSize: 140,
            currentSize: 140,
            cacheMapWidth: 10,
            cacheMapHeight: 10,
            mapWidth: 10,
            mapHeight: 10,
            cachePlayer: P1,
            currentPlayer: P2,
            framesSinceFullRebuild: 0));
    }

    [Fact]
    public void NeedsFullRebuild_WhenIntervalElapsed_OtherwiseFalse()
    {
        Assert.False(MinimapDirtyTracker.NeedsFullRebuild(
            hasCache: true,
            cacheSize: 140,
            currentSize: 140,
            cacheMapWidth: 10,
            cacheMapHeight: 10,
            mapWidth: 10,
            mapHeight: 10,
            cachePlayer: P1,
            currentPlayer: P1,
            framesSinceFullRebuild: MinimapDirtyTracker.DefaultFullRebuildInterval - 1));

        Assert.True(MinimapDirtyTracker.NeedsFullRebuild(
            hasCache: true,
            cacheSize: 140,
            currentSize: 140,
            cacheMapWidth: 10,
            cacheMapHeight: 10,
            mapWidth: 10,
            mapHeight: 10,
            cachePlayer: P1,
            currentPlayer: P1,
            framesSinceFullRebuild: MinimapDirtyTracker.DefaultFullRebuildInterval));
    }

    [Fact]
    public void Plan_CachedOnly_WhenFogEmpty_AndEntitiesUnchanged()
    {
        var entities = new[]
        {
            new MinimapEntitySnapshot(1, 2, 3, 1, 1, 0)
        };

        var plan = MinimapDirtyTracker.Plan(
            needsFullRebuild: false,
            fogDirtyTiles: Array.Empty<TilePosition>(),
            previousEntities: entities,
            currentEntities: entities);

        Assert.Equal(MinimapRedrawKind.CachedOnly, plan.Kind);
        Assert.Empty(plan.TilesToRedraw);
    }

    [Fact]
    public void Plan_FullRebuild_IgnoresDirtyInputs()
    {
        var plan = MinimapDirtyTracker.Plan(
            needsFullRebuild: true,
            fogDirtyTiles: new[] { new TilePosition(1, 1) },
            previousEntities: Array.Empty<MinimapEntitySnapshot>(),
            currentEntities: new[] { new MinimapEntitySnapshot(1, 0, 0, 1, 1, 0) });

        Assert.Equal(MinimapRedrawKind.FullRebuild, plan.Kind);
        Assert.Empty(plan.TilesToRedraw);
    }

    [Fact]
    public void ComputeDirtyTiles_IncludesFogDirty_AndMovedEntityFootprints()
    {
        var fog = new[] { new TilePosition(5, 5), new TilePosition(6, 5) };
        var previous = new[]
        {
            new MinimapEntitySnapshot(10, 0, 0, 2, 1, 0)
        };
        var current = new[]
        {
            new MinimapEntitySnapshot(10, 3, 1, 2, 1, 0)
        };

        var tiles = MinimapDirtyTracker.ComputeDirtyTiles(fog, previous, current);
        var set = tiles.Select(t => (t.X, t.Y)).ToHashSet();

        Assert.Contains((5, 5), set);
        Assert.Contains((6, 5), set);
        // Previous footprint 2x1 at (0,0)
        Assert.Contains((0, 0), set);
        Assert.Contains((1, 0), set);
        // Current footprint 2x1 at (3,1)
        Assert.Contains((3, 1), set);
        Assert.Contains((4, 1), set);
        Assert.Equal(6, set.Count);
    }

    [Fact]
    public void ComputeDirtyTiles_IncludesRemovedAndAppearedEntities()
    {
        var previous = new[]
        {
            new MinimapEntitySnapshot(1, 1, 1, 1, 1, 2)
        };
        var current = new[]
        {
            new MinimapEntitySnapshot(2, 4, 4, 1, 1, 0)
        };

        var tiles = MinimapDirtyTracker.ComputeDirtyTiles(
            Array.Empty<TilePosition>(),
            previous,
            current);
        var set = tiles.Select(t => (t.X, t.Y)).ToHashSet();

        Assert.Contains((1, 1), set);
        Assert.Contains((4, 4), set);
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void Plan_PatchTiles_WhenOnlyFogDirty()
    {
        var entities = new[]
        {
            new MinimapEntitySnapshot(1, 0, 0, 1, 1, 0)
        };
        var fog = new[] { new TilePosition(7, 8) };

        var plan = MinimapDirtyTracker.Plan(
            needsFullRebuild: false,
            fogDirtyTiles: fog,
            previousEntities: entities,
            currentEntities: entities);

        Assert.Equal(MinimapRedrawKind.PatchTiles, plan.Kind);
        Assert.Equal(new TilePosition(7, 8), Assert.Single(plan.TilesToRedraw));
    }

    [Fact]
    public void EntitiesEqual_DetectsMarkerKindChange()
    {
        var a = new[] { new MinimapEntitySnapshot(1, 0, 0, 1, 1, 0) };
        var b = new[] { new MinimapEntitySnapshot(1, 0, 0, 1, 1, 2) };

        Assert.False(MinimapDirtyTracker.EntitiesEqual(a, b));
    }
}
