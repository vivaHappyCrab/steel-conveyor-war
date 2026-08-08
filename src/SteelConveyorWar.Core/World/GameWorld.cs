namespace SteelConveyorWar.Core;

public sealed class GameWorld
{
    private readonly TerrainType[,] _terrain;
    private readonly List<WorldEntity> _entities = new();

    public GameWorld(WorldSize size, TerrainType[,] terrain, IEnumerable<WorldEntity> entities)
    {
        Size = size;
        _terrain = terrain;
        _entities.AddRange(entities);
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
        return _entities.FirstOrDefault(entity => entity.Id == id);
    }

    public WorldEntity? GetTopEntityAt(TilePosition position)
    {
        return _entities
            .Where(entity => entity.IsAlive && !entity.IsGarrisoned && ContainsTile(entity, position))
            .OrderByDescending(entity => entity.Id)
            .FirstOrDefault();
    }

    public IEnumerable<WorldEntity> GetEntitiesAt(TilePosition position)
    {
        return _entities.Where(entity => entity.IsAlive && !entity.IsGarrisoned && ContainsTile(entity, position));
    }

    public static bool ContainsTile(WorldEntity entity, TilePosition position)
    {
        var footprintKind = entity.Kind == EntityKind.GhostBuild && entity.BuildTargetKind is not null
            ? entity.BuildTargetKind.Value
            : entity.Kind;
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

    internal void AddEntity(WorldEntity entity)
    {
        _entities.Add(entity);
    }

    internal void RemoveDead()
    {
        _entities.RemoveAll(entity => !entity.IsAlive && entity.Kind != EntityKind.Commander);
    }
}
