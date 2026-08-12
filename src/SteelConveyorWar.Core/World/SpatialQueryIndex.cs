namespace SteelConveyorWar.Core;

/// <summary>
/// Transient spatial helpers for combat, movement collision, and threat search.
/// Call <see cref="Rebuild"/> once per spatial phase (M06: post-commands and post-factory) so range
/// queries avoid O(callers × entities) scans. Mid-pass movers use <see cref="Relocate"/>; mid-pass
/// spawns use <see cref="InsertAlive"/>.
/// Keyed by entity <see cref="WorldEntity.Position"/> (anchor), matching Euclidean range rules.
/// Dead / garrisoned entities may remain stale until the next rebuild; callers must re-check liveness.
/// </summary>
internal sealed class SpatialQueryIndex
{
    /// <summary>
    /// Tile radius large enough that any entity whose footprint can intersect a unit collision
    /// circle is included when querying from the mover's tile (max footprint 3 + max radius &lt; 1).
    /// </summary>
    public const int CollisionNeighborhoodRadiusTiles = 4;

    private readonly Dictionary<long, List<WorldEntity>> _entitiesByPosition = new();
    private readonly Dictionary<long, List<WorldEntity>> _wallsByTile = new();
    private readonly Stack<List<WorldEntity>> _listPool = new();

    public void Rebuild(IReadOnlyList<WorldEntity> entities, GameplayTablesCatalog tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        Clear();
        foreach (var entity in entities)
        {
            if (!entity.IsAlive || entity.IsGarrisoned)
            {
                continue;
            }

            Add(_entitiesByPosition, entity.Position, entity);

            if (entity.OwnerId is null || !MvpDefinitions.IsWallKind(entity.Kind))
            {
                continue;
            }

            foreach (var tile in GameWorld.GetFootprintTiles(entity.Kind, entity.Position, tables))
            {
                Add(_wallsByTile, tile, entity);
            }
        }

        SortBuckets(_entitiesByPosition);
        SortBuckets(_wallsByTile);
    }

    public static SpatialQueryIndex Build(IReadOnlyList<WorldEntity> entities, GameplayTablesCatalog? tables = null)
    {
        var index = new SpatialQueryIndex();
        index.Rebuild(entities, tables ?? GameplayTablesCatalog.Embedded);
        return index;
    }

    public void Clear()
    {
        ReturnLists(_entitiesByPosition);
        ReturnLists(_wallsByTile);
    }

    /// <summary>
    /// Yields indexed entities whose Position lies within Euclidean <paramref name="radius"/> of
    /// <paramref name="center"/>. Buckets are sorted by Id; tile scan is Y then X for stable order.
    /// </summary>
    public IEnumerable<WorldEntity> QueryByPositionInEuclideanRange(TilePosition center, int radius)
    {
        if (radius < 0)
        {
            yield break;
        }

        var minX = center.X - radius;
        var maxX = center.X + radius;
        var minY = center.Y - radius;
        var maxY = center.Y + radius;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var tile = new TilePosition(x, y);
                if (!center.IsWithinEuclideanRange(tile, radius))
                {
                    continue;
                }

                if (!_entitiesByPosition.TryGetValue(Pack(tile), out var atTile))
                {
                    continue;
                }

                foreach (var entity in atTile)
                {
                    yield return entity;
                }
            }
        }
    }

    public bool HasAlliedWallAt(TilePosition tile, PlayerId alliedToOwner, Func<PlayerId?, PlayerId?, bool> areAllied)
    {
        if (!_wallsByTile.TryGetValue(Pack(tile), out var walls))
        {
            return false;
        }

        foreach (var wall in walls)
        {
            if (wall.IsAlive && areAllied(wall.OwnerId, alliedToOwner))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps the position index in sync when a mobile entity relocates mid-pass.
    /// Wall footprint tiles are not updated (walls do not move during movement).
    /// </summary>
    public void Relocate(WorldEntity entity, TilePosition from, TilePosition to)
    {
        if (from == to)
        {
            return;
        }

        RemoveFromBucket(_entitiesByPosition, from, entity);
        Add(_entitiesByPosition, to, entity);
        // Keep Id order inside the destination bucket for stable neighbor iteration.
        if (_entitiesByPosition.TryGetValue(Pack(to), out var atTo) && atTo.Count > 1)
        {
            atTo.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        }
    }

    /// <summary>
    /// Indexes a newly spawned alive entity mid-pass without a full rebuild (M06).
    /// </summary>
    public void InsertAlive(WorldEntity entity, GameplayTablesCatalog tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (!entity.IsAlive || entity.IsGarrisoned)
        {
            return;
        }

        Add(_entitiesByPosition, entity.Position, entity);
        if (_entitiesByPosition.TryGetValue(Pack(entity.Position), out var atPos) && atPos.Count > 1)
        {
            atPos.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        }

        if (entity.OwnerId is null || !MvpDefinitions.IsWallKind(entity.Kind))
        {
            return;
        }

        foreach (var tile in GameWorld.GetFootprintTiles(entity.Kind, entity.Position, tables))
        {
            Add(_wallsByTile, tile, entity);
            if (_wallsByTile.TryGetValue(Pack(tile), out var walls) && walls.Count > 1)
            {
                walls.Sort(static (left, right) => left.Id.CompareTo(right.Id));
            }
        }
    }

    private void Add(Dictionary<long, List<WorldEntity>> map, TilePosition tile, WorldEntity entity)
    {
        var key = Pack(tile);
        if (!map.TryGetValue(key, out var list))
        {
            list = _listPool.Count > 0 ? _listPool.Pop() : new List<WorldEntity>(1);
            map[key] = list;
        }

        list.Add(entity);
    }

    private void RemoveFromBucket(Dictionary<long, List<WorldEntity>> map, TilePosition tile, WorldEntity entity)
    {
        var key = Pack(tile);
        if (!map.TryGetValue(key, out var list))
        {
            return;
        }

        list.Remove(entity);
        if (list.Count == 0)
        {
            map.Remove(key);
            list.Clear();
            _listPool.Push(list);
        }
    }

    private void ReturnLists(Dictionary<long, List<WorldEntity>> map)
    {
        foreach (var list in map.Values)
        {
            list.Clear();
            _listPool.Push(list);
        }

        map.Clear();
    }

    private static void SortBuckets(Dictionary<long, List<WorldEntity>> map)
    {
        foreach (var list in map.Values)
        {
            if (list.Count > 1)
            {
                list.Sort(static (left, right) => left.Id.CompareTo(right.Id));
            }
        }
    }

    private static long Pack(TilePosition tile)
    {
        return ((long)tile.X << 32) | (uint)tile.Y;
    }
}
