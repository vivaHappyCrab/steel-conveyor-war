namespace SteelConveyorWar.Core;

public sealed class WorldEntity
{
    private readonly List<ConveyorItem> _conveyorItems = new();
    private readonly List<TilePosition> _movementPath = new();
    private readonly Dictionary<EntityKind, int> _bastionTemplate = new();

    public WorldEntity(int id, EntityKind kind, TilePosition position, PlayerId? ownerId = null)
    {
        Id = id;
        Kind = kind;
        Position = position;
        WorldPosition = WorldPosition.FromTileCenter(position);
        OwnerId = ownerId;
        var stats = MvpDefinitions.GetStats(kind);
        MaxHealth = stats.MaxHealth;
        Health = stats.MaxHealth;
    }

    public int Id { get; }

    public EntityKind Kind { get; internal set; }

    public PlayerId? OwnerId { get; internal set; }

    public TilePosition Position { get; internal set; }

    public WorldPosition WorldPosition { get; internal set; }

    public Direction Direction { get; internal set; } = Direction.East;

    public int MaxHealth { get; internal set; }

    public int Health { get; internal set; }

    public bool IsAlive => Health > 0;

    public Inventory Inventory { get; } = new();

    public Inventory InputBuffer { get; } = new();

    public Inventory OutputBuffer { get; } = new();

    public IReadOnlyList<ConveyorItem> ConveyorItems => _conveyorItems.AsReadOnly();

    internal List<ConveyorItem> ConveyorItemsMutable => _conveyorItems;

    public ItemId? HeldItem { get; internal set; }

    public int HeldTransferTicksRemaining { get; internal set; }

    public EntityKind? BuildTargetKind { get; internal set; }

    public int ConstructionTicksRemaining { get; internal set; }

    public EntityKind? ProductionTargetKind { get; internal set; }

    /// <summary>
    /// True when <see cref="ProductionTargetKind"/> was set by the player (continuous recipe).
    /// Autofill clears the target after each spawn; manual production keeps it.
    /// </summary>
    public bool IsManualProductionTarget { get; internal set; }

    public ItemId? PendingOutputItem { get; internal set; }

    public int PendingOutputAmount { get; internal set; } = 1;

    public int WorkTicksRemaining { get; internal set; }

    /// <summary>Total work ticks for the active craft cycle (progress UI). 0 when idle.</summary>
    public int WorkTicksTotal { get; internal set; }

    public int AttackCooldownRemaining { get; internal set; }

    /// <summary>Stored energy available for production drain.</summary>
    public int EnergyBuffer { get; internal set; }

    /// <summary>Max energy buffer; 0 when the entity has no <see cref="MvpDefinitions.PowerDemand"/>.</summary>
    public int EnergyBufferCapacity { get; internal set; }

    /// <summary>Last smelter recipe used; kept when input empties until a different valid input arrives.</summary>
    public SmeltRecipeId? ActiveSmeltRecipe { get; internal set; }

    public ItemId? FilterItem { get; internal set; }

    public int? AssignedBastionId { get; internal set; }

    public bool IsGarrisoned { get; internal set; }

    public CommanderBuildOrder? QueuedBuildOrder { get; internal set; }

    public TilePosition? MoveTarget { get; internal set; }

    public IReadOnlyList<TilePosition> MovementPath => _movementPath.AsReadOnly();

    internal List<TilePosition> MovementPathMutable => _movementPath;

    public TilePosition? CurrentWaypoint { get; internal set; }

    public ItemRecipeId? SelectedItemRecipe { get; internal set; }

    public IReadOnlyDictionary<EntityKind, int> BastionTemplate => _bastionTemplate.AsReadOnly();

    internal Dictionary<EntityKind, int> BastionTemplateMutable => _bastionTemplate;

    public BastionOrder Order { get; internal set; } = new(BastionOrderKind.Defend);
}
