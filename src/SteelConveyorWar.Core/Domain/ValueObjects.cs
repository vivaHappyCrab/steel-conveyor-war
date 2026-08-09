namespace SteelConveyorWar.Core;

public readonly record struct PlayerId(int Value);

public readonly record struct TilePosition(int X, int Y)
{
    public TilePosition Offset(Direction direction)
    {
        return direction switch
        {
            Direction.North => this with { Y = Y - 1 },
            Direction.East => this with { X = X + 1 },
            Direction.South => this with { Y = Y + 1 },
            Direction.West => this with { X = X - 1 },
            _ => this
        };
    }

    public int ManhattanDistance(TilePosition other)
    {
        return Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    }

    public int EuclideanDistanceSquared(TilePosition other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    public bool IsWithinEuclideanRange(TilePosition other, int radius)
    {
        return radius >= 0 && EuclideanDistanceSquared(other) <= radius * radius;
    }
}

public readonly record struct WorldSize(int Width, int Height);

public readonly record struct WorldPosition(double X, double Y)
{
    public static WorldPosition FromTileCenter(TilePosition tile)
    {
        return new WorldPosition(tile.X + 0.5, tile.Y + 0.5);
    }

    public TilePosition ToTilePosition()
    {
        return new TilePosition((int)Math.Floor(X), (int)Math.Floor(Y));
    }

    public double DistanceTo(WorldPosition other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public readonly record struct CollisionSize(double Radius);

public sealed record BastionOrder(
    BastionOrderKind Kind,
    TilePosition? Target = null,
    IReadOnlyList<TilePosition>? Waypoints = null,
    int WaypointIndex = 0)
{
    public IReadOnlyList<TilePosition> WaypointList => Waypoints ?? Array.Empty<TilePosition>();
}

public sealed record TechSignatureHotspot(int ZoneX, int ZoneY, int Intensity);

public sealed record CommanderBuildOrder(
    EntityKind TargetKind,
    TilePosition TargetPosition,
    Direction Direction = Direction.East,
    ItemRecipeId? SelectedItemRecipe = null);

public sealed record EntityStats(
    int MaxHealth,
    int AttackDamage = 0,
    int AttackRange = 0,
    int AttackCooldownTicks = 30,
    int MoveEveryTicks = 10,
    int VisionRadius = 4,
    int Armor = 0,
    ProjectileKind ProjectileKind = ProjectileKind.GroundToGround,
    int SplashRadius = 0);

public sealed record ProductionRecipe(IReadOnlyDictionary<ItemId, int> Inputs, EntityKind OutputKind, int WorkTicks, TechnologyId? RequiredTechnology = null);

public sealed record ItemRecipeDefinition(ItemRecipeId Id, IReadOnlyDictionary<ItemId, int> Inputs, ItemId OutputItem, int OutputAmount, int WorkTicks);
