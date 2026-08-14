using System.Collections.Immutable;

namespace SteelConveyorWar.Core;

public readonly record struct PlayerId(int Value);

public readonly record struct TilePosition(int X, int Y)
{
    public TilePosition Offset(Direction direction, int distance = 1)
    {
        if (distance == 0)
        {
            return this;
        }

        var step = distance < 0 ? -distance : distance;
        var dir = distance < 0 ? Opposite(direction) : direction;
        return dir switch
        {
            Direction.North => this with { Y = Y - step },
            Direction.East => this with { X = X + step },
            Direction.South => this with { Y = Y + step },
            Direction.West => this with { X = X - step },
            _ => this
        };
    }

    private static Direction Opposite(Direction direction) => direction switch
    {
        Direction.North => Direction.South,
        Direction.East => Direction.West,
        Direction.South => Direction.North,
        Direction.West => Direction.East,
        _ => direction
    };

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

public readonly record struct WorldPosition(long X, long Y)
{
    public static WorldPosition FromTileCenter(TilePosition tile)
    {
        return new WorldPosition(WorldUnits.TileCenterMilli(tile.X), WorldUnits.TileCenterMilli(tile.Y));
    }

    public TilePosition ToTilePosition()
    {
        return new TilePosition(WorldUnits.MilliToTile(X), WorldUnits.MilliToTile(Y));
    }

    /// <summary>
    /// Euclidean length in millitiles via integer sqrt. Prefer <see cref="DistanceSquaredTo"/> for
    /// ordering / radius checks (ADR 0001).
    /// </summary>
    public long DistanceTo(WorldPosition other)
    {
        return WorldUnits.IntegerSqrt(DistanceSquaredTo(other));
    }

    public long DistanceSquaredTo(WorldPosition other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Presentation helper: millitiles → tile-space float.</summary>
    public double ToTileSpaceX() => X / (double)WorldUnits.MilliPerTile;

    /// <summary>Presentation helper: millitiles → tile-space float.</summary>
    public double ToTileSpaceY() => Y / (double)WorldUnits.MilliPerTile;
}

public readonly record struct CollisionSize(long RadiusMilli);

public sealed record BastionOrder
{
    /// <summary>
    /// R12 / M01: caller-owned <paramref name="Waypoints"/> are deep-copied into an immutable snapshot,
    /// so mutating the source list after constructing (or enqueuing) an order cannot alter it.
    /// <see cref="WaypointList"/> is get-only (no init) so <c>with { WaypointList = ... }</c> cannot
    /// substitute a mutable collection; use <see cref="WithWaypointIndex"/> for scalar updates.
    /// Parameter names are preserved for existing positional/named call sites.
    /// </summary>
    public BastionOrder(
        BastionOrderKind Kind,
        TilePosition? Target = null,
        IReadOnlyList<TilePosition>? Waypoints = null,
        int WaypointIndex = 0)
    {
        this.Kind = Kind;
        this.Target = Target;
        WaypointList = Waypoints is null
            ? ImmutableArray<TilePosition>.Empty
            : Waypoints.ToImmutableArray();
        this.WaypointIndex = WaypointIndex;
    }

    public BastionOrderKind Kind { get; init; }

    public TilePosition? Target { get; init; }

    /// <summary>
    /// Immutable snapshot of the ordered waypoints (empty when none were supplied). Exposed as
    /// <see cref="IReadOnlyList{T}"/> so existing <c>.Count</c> call sites keep compiling while the
    /// backing store is an <see cref="ImmutableArray{T}"/>. Get-only to close the record <c>with</c>
    /// mutable-collection bypass (M01).
    /// </summary>
    public IReadOnlyList<TilePosition> WaypointList { get; }

    public int WaypointIndex { get; init; }

    /// <summary>Scalar waypoint-index update that preserves the frozen waypoint snapshot.</summary>
    public BastionOrder WithWaypointIndex(int waypointIndex)
        => this with { WaypointIndex = waypointIndex };
}

public sealed record TechSignatureHotspot(int ZoneX, int ZoneY, int Intensity);

public sealed record CommanderBuildOrder(
    EntityKind TargetKind,
    TilePosition TargetPosition,
    Direction Direction = Direction.East,
    ItemRecipeId? SelectedItemRecipe = null,
    bool InserterLongReach = false);

public sealed record CommanderDemolishOrder(int TargetEntityId);

public sealed record EntityStats(
    int MaxHealth,
    int AttackDamage = 0,
    int AttackRange = 0,
    int AttackCooldownTicks = 30,
    int MoveEveryTicks = 10,
    int VisionRadius = 4,
    int Armor = 0,
    ProjectileKind ProjectileKind = ProjectileKind.GroundToGround,
    int SplashRadius = 0,
    MovementType MovementType = MovementType.Ground);

public sealed record ProductionRecipe(IReadOnlyDictionary<ItemId, int> Inputs, EntityKind OutputKind, int WorkTicks, TechnologyId? RequiredTechnology = null)
{
    // H07: nested recipe input maps must not remain cast-mutable after construction / `with`.
    public IReadOnlyDictionary<ItemId, int> Inputs
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(Inputs);
}

public sealed record ItemRecipeDefinition(ItemRecipeId Id, IReadOnlyDictionary<ItemId, int> Inputs, ItemId OutputItem, int OutputAmount, int WorkTicks)
{
    public IReadOnlyDictionary<ItemId, int> Inputs
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(Inputs);
}
