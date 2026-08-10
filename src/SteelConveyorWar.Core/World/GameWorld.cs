namespace SteelConveyorWar.Core;

public sealed class GameWorld
{
    private readonly TerrainType[,] _terrain;
    private readonly List<WorldEntity> _entities = new();
    private readonly Dictionary<int, WorldEntity> _byId = new();
    private readonly Dictionary<TilePosition, List<WorldEntity>> _occupancy = new();

    public GameWorld(WorldSize size, TerrainType[,] terrain, IEnumerable<WorldEntity> entities)
    {
        Size = size;
        _terrain = terrain;
        foreach (var entity in entities)
        {
            AddEntity(entity);
        }
    }

    public WorldSize Size { get; }

    public IReadOnlyList<WorldEntity> Entities => _entities;

    public TerrainType GetTerrain(TilePosition position)
    {
        if (!IsInside(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Tile position is outside the world.");
        }

        return _terrain[position.X, position.Y];
    }

    public bool IsInside(TilePosition position)
    {
        return position.X >= 0 && position.X < Size.Width && position.Y >= 0 && position.Y < Size.Height;
    }

    public WorldEntity? GetEntity(int id)
    {
        return _byId.TryGetValue(id, out var entity) ? entity : null;
    }

    public WorldEntity? GetTopEntityAt(TilePosition position)
    {
        if (!_occupancy.TryGetValue(position, out var bucket))
        {
            return null;
        }

        WorldEntity? top = null;
        foreach (var entity in bucket)
        {
            if (!entity.IsAlive || entity.IsGarrisoned)
            {
                continue;
            }

            if (top is null || entity.Id > top.Id)
            {
                top = entity;
            }
        }

        return top;
    }

    public IEnumerable<WorldEntity> GetEntitiesAt(TilePosition position)
    {
        if (!_occupancy.TryGetValue(position, out var bucket) || bucket.Count == 0)
        {
            return [];
        }

        // Match prior list-scan order: entities stay in ascending Id / insertion order after RemoveDead.
        List<WorldEntity>? matches = null;
        foreach (var entity in bucket)
        {
            if (!entity.IsAlive || entity.IsGarrisoned)
            {
                continue;
            }

            matches ??= new List<WorldEntity>();
            matches.Add(entity);
        }

        if (matches is null)
        {
            return [];
        }

        if (matches.Count > 1)
        {
            matches.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        }

        return matches;
    }

    public static bool ContainsTile(WorldEntity entity, TilePosition position)
    {
        var footprintKind = ResolveFootprintKind(entity);
        var footprint = MvpDefinitions.GetFootprint(footprintKind);
        return position.X >= entity.Position.X
            && position.X < entity.Position.X + footprint.Width
            && position.Y >= entity.Position.Y
            && position.Y < entity.Position.Y + footprint.Height;
    }

    public static IEnumerable<TilePosition> GetFootprintTiles(EntityKind kind, TilePosition anchor)
    {
        var footprint = MvpDefinitions.GetFootprint(kind);
        for (var y = 0; y < footprint.Height; y++)
        {
            for (var x = 0; x < footprint.Width; x++)
            {
                yield return new TilePosition(anchor.X + x, anchor.Y + y);
            }
        }
    }

    /// <summary>
    /// Moves an entity's tile anchor and refreshes occupancy. No-op when the tile is unchanged.
    /// </summary>
    internal void RelocateEntity(WorldEntity entity, TilePosition position)
    {
        if (entity.Position == position)
        {
            return;
        }

        UnindexEntity(entity);
        entity.Position = position;
        IndexEntity(entity);
    }

    internal void AddEntity(WorldEntity entity)
    {
        _entities.Add(entity);
        _byId[entity.Id] = entity;
        IndexEntity(entity);
    }

    /// <summary>
    /// Purges all non-alive entities, including defeated commanders.
    /// Call only after victory evaluation for the tick — <see cref="GameSimulation"/> runs
    /// <c>CheckVictory</c> before this so commander-kill win conditions stay correct.
    /// </summary>
    internal void RemoveDead()
    {
        for (var i = _entities.Count - 1; i >= 0; i--)
        {
            var entity = _entities[i];
            if (entity.IsAlive)
            {
                continue;
            }

            UnindexEntity(entity);
            _byId.Remove(entity.Id);
            _entities.RemoveAt(i);
        }
    }

    private void IndexEntity(WorldEntity entity)
    {
        foreach (var tile in GetOccupiedTiles(entity))
        {
            if (!_occupancy.TryGetValue(tile, out var bucket))
            {
                bucket = new List<WorldEntity>(1);
                _occupancy[tile] = bucket;
            }

            bucket.Add(entity);
        }
    }

    private void UnindexEntity(WorldEntity entity)
    {
        foreach (var tile in GetOccupiedTiles(entity))
        {
            if (!_occupancy.TryGetValue(tile, out var bucket))
            {
                continue;
            }

            bucket.Remove(entity);
            if (bucket.Count == 0)
            {
                _occupancy.Remove(tile);
            }
        }
    }

    private static IEnumerable<TilePosition> GetOccupiedTiles(WorldEntity entity)
    {
        return GetFootprintTiles(ResolveFootprintKind(entity), entity.Position);
    }

    private static EntityKind ResolveFootprintKind(WorldEntity entity)
    {
        return entity.Kind == EntityKind.GhostBuild && entity.BuildTargetKind is not null
            ? entity.BuildTargetKind.Value
            : entity.Kind;
    }
}
