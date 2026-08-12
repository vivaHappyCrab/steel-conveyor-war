using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// R33: pure logic for minimap RenderTexture dirty updates — which tiles need redraw given
/// FoW dirty tiles + entity marker snapshots. Unit-testable without an SFML window.
/// </summary>
public enum MinimapRedrawKind
{
    /// <summary>Fog and entities unchanged; blit the cached texture only.</summary>
    CachedOnly,

    /// <summary>Redraw a subset of logical tiles (terrain), then refresh entity markers.</summary>
    PatchTiles,

    /// <summary>Clear and rebuild the entire minimap texture.</summary>
    FullRebuild
}

/// <summary>
/// Stable snapshot of a minimap entity marker (position, footprint, and color category).
/// </summary>
/// <param name="EntityId">World entity id.</param>
/// <param name="X">Tile X of the footprint origin.</param>
/// <param name="Y">Tile Y of the footprint origin.</param>
/// <param name="Width">Footprint width in tiles.</param>
/// <param name="Height">Footprint height in tiles.</param>
/// <param name="MarkerKind">0 = local, 1 = ally, 2 = enemy.</param>
public readonly record struct MinimapEntitySnapshot(
    int EntityId,
    int X,
    int Y,
    int Width,
    int Height,
    byte MarkerKind);

/// <summary>
/// Decision for one minimap frame: redraw kind plus (for <see cref="MinimapRedrawKind.PatchTiles"/>)
/// the logical tiles whose terrain pixels must be refreshed.
/// </summary>
public readonly record struct MinimapUpdatePlan(
    MinimapRedrawKind Kind,
    IReadOnlyList<TilePosition> TilesToRedraw);

/// <summary>
/// Computes minimap dirty-tile plans from FoW dirty sets and entity position snapshots.
/// </summary>
public static class MinimapDirtyTracker
{
    /// <summary>Safety full rebuild interval (frames) so drift cannot accumulate indefinitely.</summary>
    public const int DefaultFullRebuildInterval = 180;

    public static bool NeedsFullRebuild(
        bool hasCache,
        uint cacheSize,
        uint currentSize,
        int cacheMapWidth,
        int cacheMapHeight,
        int mapWidth,
        int mapHeight,
        PlayerId? cachePlayer,
        PlayerId currentPlayer,
        int framesSinceFullRebuild,
        int fullRebuildInterval = DefaultFullRebuildInterval)
    {
        if (!hasCache)
        {
            return true;
        }

        if (cacheSize != currentSize
            || cacheMapWidth != mapWidth
            || cacheMapHeight != mapHeight
            || cachePlayer != currentPlayer)
        {
            return true;
        }

        return fullRebuildInterval > 0 && framesSinceFullRebuild >= fullRebuildInterval;
    }

    public static bool EntitiesEqual(
        IReadOnlyList<MinimapEntitySnapshot> previous,
        IReadOnlyList<MinimapEntitySnapshot> current)
    {
        if (previous.Count != current.Count)
        {
            return false;
        }

        for (var i = 0; i < previous.Count; i++)
        {
            if (previous[i] != current[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Union of fog-dirty tiles and footprints of entities that appeared, disappeared, moved,
    /// resized, or changed marker kind since the previous snapshot.
    /// </summary>
    public static List<TilePosition> ComputeDirtyTiles(
        IReadOnlyList<TilePosition> fogDirtyTiles,
        IReadOnlyList<MinimapEntitySnapshot> previousEntities,
        IReadOnlyList<MinimapEntitySnapshot> currentEntities)
    {
        var set = new HashSet<(int X, int Y)>();

        for (var i = 0; i < fogDirtyTiles.Count; i++)
        {
            var tile = fogDirtyTiles[i];
            set.Add((tile.X, tile.Y));
        }

        var previousById = new Dictionary<int, MinimapEntitySnapshot>(previousEntities.Count);
        for (var i = 0; i < previousEntities.Count; i++)
        {
            previousById[previousEntities[i].EntityId] = previousEntities[i];
        }

        var currentById = new Dictionary<int, MinimapEntitySnapshot>(currentEntities.Count);
        for (var i = 0; i < currentEntities.Count; i++)
        {
            currentById[currentEntities[i].EntityId] = currentEntities[i];
        }

        for (var i = 0; i < previousEntities.Count; i++)
        {
            var previous = previousEntities[i];
            if (!currentById.TryGetValue(previous.EntityId, out var current) || current != previous)
            {
                AddFootprint(set, previous);
            }
        }

        for (var i = 0; i < currentEntities.Count; i++)
        {
            var current = currentEntities[i];
            if (!previousById.TryGetValue(current.EntityId, out var previous) || previous != current)
            {
                AddFootprint(set, current);
            }
        }

        var result = new List<TilePosition>(set.Count);
        foreach (var (x, y) in set)
        {
            result.Add(new TilePosition(x, y));
        }

        return result;
    }

    public static MinimapUpdatePlan Plan(
        bool needsFullRebuild,
        IReadOnlyList<TilePosition> fogDirtyTiles,
        IReadOnlyList<MinimapEntitySnapshot> previousEntities,
        IReadOnlyList<MinimapEntitySnapshot> currentEntities)
    {
        if (needsFullRebuild)
        {
            return new MinimapUpdatePlan(MinimapRedrawKind.FullRebuild, Array.Empty<TilePosition>());
        }

        var entitiesUnchanged = EntitiesEqual(previousEntities, currentEntities);
        if (fogDirtyTiles.Count == 0 && entitiesUnchanged)
        {
            return new MinimapUpdatePlan(MinimapRedrawKind.CachedOnly, Array.Empty<TilePosition>());
        }

        var tiles = ComputeDirtyTiles(fogDirtyTiles, previousEntities, currentEntities);
        return new MinimapUpdatePlan(MinimapRedrawKind.PatchTiles, tiles);
    }

    /// <summary>
    /// M07: union FoW dirty tiles from one simulation tick into a frame-scoped set so multi-tick
    /// catch-up (R23, ≤ <see cref="FixedStepPacer.MaxTicksPerFrame"/>) does not drop intermediate dirty.
    /// </summary>
    public static void AccumulateFogDirty(
        HashSet<(int X, int Y)> frameFogDirty,
        IReadOnlyList<TilePosition> tickFogDirty)
    {
        ArgumentNullException.ThrowIfNull(frameFogDirty);
        ArgumentNullException.ThrowIfNull(tickFogDirty);
        for (var i = 0; i < tickFogDirty.Count; i++)
        {
            var tile = tickFogDirty[i];
            frameFogDirty.Add((tile.X, tile.Y));
        }
    }

    /// <summary>M07: materialize a frame fog-dirty set into a tile list for <see cref="Plan"/>.</summary>
    public static void CopyFogDirty(
        HashSet<(int X, int Y)> frameFogDirty,
        List<TilePosition> destination)
    {
        ArgumentNullException.ThrowIfNull(frameFogDirty);
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        foreach (var (x, y) in frameFogDirty)
        {
            destination.Add(new TilePosition(x, y));
        }
    }

    private static void AddFootprint(HashSet<(int X, int Y)> set, MinimapEntitySnapshot entity)
    {
        var width = Math.Max(1, entity.Width);
        var height = Math.Max(1, entity.Height);
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                set.Add((entity.X + dx, entity.Y + dy));
            }
        }
    }
}
