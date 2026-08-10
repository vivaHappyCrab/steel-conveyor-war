namespace SteelConveyorWar.Core;

/// <summary>
/// Transient combat-only spatial helpers. Built once per <c>ProcessCombat</c> pass so target
/// acquisition and splash avoid O(attackers × entities) full-list scans. Keyed by entity
/// <see cref="WorldEntity.Position"/> (anchor), matching existing Euclidean combat range rules.
/// Dead / garrisoned entities may remain in the index; callers must re-check liveness.
/// </summary>
internal sealed class CombatSpatialIndex
{
    private readonly Dictionary<long, List<WorldEntity>> _entitiesByPosition = new();
    private readonly Dictionary<long, List<WorldEntity>> _wallsByTile = new();

    private CombatSpatialIndex()
    {
    }

    public static CombatSpatialIndex Build(IReadOnlyList<WorldEntity> entities)
    {
        var index = new CombatSpatialIndex();
        foreach (var entity in entities)
        {
            if (!entity.IsAlive || entity.IsGarrisoned || entity.OwnerId is null)
            {
                continue;
            }

            Add(index._entitiesByPosition, entity.Position, entity);

            if (!MvpDefinitions.IsWallKind(entity.Kind))
            {
                continue;
            }

            foreach (var tile in GameWorld.GetFootprintTiles(entity.Kind, entity.Position))
            {
                Add(index._wallsByTile, tile, entity);
            }
        }

        return index;
    }

    /// <summary>
    /// Yields indexed entities whose Position lies within Euclidean <paramref name="radius"/> of
    /// <paramref name="center"/>. Iteration order is undefined; callers must OrderBy for determinism.
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

    private static void Add(Dictionary<long, List<WorldEntity>> map, TilePosition tile, WorldEntity entity)
    {
        var key = Pack(tile);
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<WorldEntity>(1);
            map[key] = list;
        }

        list.Add(entity);
    }

    private static long Pack(TilePosition tile)
    {
        return ((long)tile.X << 32) | (uint)tile.Y;
    }
}
