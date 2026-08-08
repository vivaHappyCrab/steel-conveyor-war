namespace SteelConveyorWar.Core;

public sealed class WorldEntity
{
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

    public EntityKind Kind { get; set; }

    public PlayerId? OwnerId { get; set; }

    public TilePosition Position { get; set; }

    public WorldPosition WorldPosition { get; set; }

    public Direction Direction { get; set; } = Direction.East;

    public int MaxHealth { get; set; }

    public int Health { get; set; }

    public bool IsAlive => Health > 0;

    public Inventory Inventory { get; } = new();

    public Inventory InputBuffer { get; } = new();

    public Inventory OutputBuffer { get; } = new();

    public List<ConveyorItem> ConveyorItems { get; } = new();

    public ItemId? HeldItem { get; set; }

    public int HeldTransferTicksRemaining { get; set; }

    public EntityKind? BuildTargetKind { get; set; }

    public int ConstructionTicksRemaining { get; set; }

    public EntityKind? ProductionTargetKind { get; set; }

    public ItemId? PendingOutputItem { get; set; }

    public int PendingOutputAmount { get; set; } = 1;

    public int WorkTicksRemaining { get; set; }

    public int AttackCooldownRemaining { get; set; }

    public ItemId? FilterItem { get; set; }

    public int? AssignedBastionId { get; set; }

    public bool IsGarrisoned { get; set; }

    public CommanderBuildOrder? QueuedBuildOrder { get; set; }

    public TilePosition? MoveTarget { get; set; }

    public List<TilePosition> MovementPath { get; } = new();

    public TilePosition? CurrentWaypoint { get; set; }

    public ItemRecipeId? SelectedItemRecipe { get; set; }

    public Dictionary<EntityKind, int> BastionTemplate { get; } = new();

    public BastionOrder Order { get; set; } = new(BastionOrderKind.Defend);
}
