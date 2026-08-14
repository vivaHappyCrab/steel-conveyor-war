using System.Collections.Frozen;

namespace SteelConveyorWar.Core;

public static class MvpDefinitions
{
    public const int CommanderBuildRadius = 12;
    public const int CommanderInteractRadius = 2;
    public const int ConveyorMoveTicks = 10;
    public const int InserterTransferTicks = 12;
    public const int ConveyorMaxItemsPerTile = 2;
    /// <summary>Oil well mine cycle (unchanged baseline).</summary>
    public const int MineWorkTicks = 15;
    /// <summary>Iron/copper ore mine cycle (2× baseline).</summary>
    public const int OreMineWorkTicks = 30;
    /// <summary>Coal mine cycle (3× baseline).</summary>
    public const int CoalMineWorkTicks = 45;
    public const int HubStorageStacks = 20;
    public const long MobileMoveWorldUnitsPerTick = WorldUnits.MobileMoveMilliPerTick;
    public const int BaseBastionTemplateCapacity = 10;
    public const int BaseMaxBastions = 3;
    public const int MaxBastionsAfterUnlock = 4;

    // R05: FrozenSet cannot be mutated by callers (previously a public mutable HashSet).
    public static readonly FrozenSet<EntityKind> UnitKinds = new[]
    {
        EntityKind.LightBot,
        EntityKind.BasicTank,
        EntityKind.Scout,
        EntityKind.MediumBot,
        EntityKind.MediumTank,
        EntityKind.AntiAirBot,
        EntityKind.RocketLauncher
    }.ToFrozenSet();

    public static readonly FrozenSet<EntityKind> FactoryKinds = new[]
    {
        EntityKind.TankFactory,
        EntityKind.DroneCenter
    }.ToFrozenSet();

    /// <summary>
    /// Embedded construction costs (parity with <c>config/build-costs.json</c>). Prefer
    /// <see cref="GameSimulation.BuildCostCatalog"/> when a match catalog is available.
    /// </summary>
    public static IReadOnlyDictionary<EntityKind, IReadOnlyDictionary<ItemId, int>> BuildCosts =>
        MvpBuildCostCatalog.Embedded.Costs;

    public static IReadOnlyDictionary<EntityKind, int> BuildTicks =>
        MvpBuildCostCatalog.Embedded.BuildTicks;

    public static IReadOnlyDictionary<EntityKind, TechnologyId> BuildRequirements =>
        MvpBuildCostCatalog.Embedded.Requirements;

    /// <summary>R18: delegates to <see cref="GameplayTablesCatalog.Embedded"/>.</summary>
    public static IReadOnlyDictionary<EntityKind, int> PowerDemand =>
        GameplayTablesCatalog.Embedded.PowerDemand;

    public static IReadOnlyDictionary<EntityKind, int> PowerProduction =>
        GameplayTablesCatalog.Embedded.PowerProduction;

    /// <summary>Per-building energy buffer capacity = demand × this factor (ticks of full drain).</summary>
    public const int EnergyBufferCapacityFactor = 100;

    public static int GetEnergyBufferCapacity(EntityKind kind)
    {
        var demand = PowerDemand.GetValueOrDefault(kind);
        return demand <= 0 ? 0 : demand * EnergyBufferCapacityFactor;
    }

    public static int GetPowerDemand(EntityKind kind) => GameplayTablesCatalog.Embedded.GetPowerDemand(kind);

    public static IReadOnlyDictionary<ItemId, int> ItemStackSizes =>
        GameplayTablesCatalog.Embedded.ItemStackSizes;

    public static int GetMaxStackSize(ItemId item) => GameplayTablesCatalog.Embedded.GetMaxStackSize(item);

    public static WorldSize GetFootprint(EntityKind kind) => GameplayTablesCatalog.Embedded.GetFootprint(kind);

    /// <summary>Player-facing storage UI / inventory rules: only Commander and Hub.</summary>
    public static bool HasPlayerInventory(EntityKind kind) =>
        kind is EntityKind.Commander or EntityKind.Hub;

    public static CollisionSize GetCollisionSize(EntityKind kind) =>
        GameplayTablesCatalog.Embedded.GetCollisionSize(kind);

    public static bool IsPassableLogistic(EntityKind kind)
    {
        return kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter;
    }

    public static bool IsBuildingKind(EntityKind kind) =>
        kind != EntityKind.Commander && kind != EntityKind.GhostBuild && !UnitKinds.Contains(kind);

    public static bool BlocksGroundMovement(EntityKind kind)
    {
        return !UnitKinds.Contains(kind) && kind != EntityKind.Commander && !IsPassableLogistic(kind);
    }

    public static bool IsWallKind(EntityKind kind) => kind is EntityKind.Wall or EntityKind.SteelWall;

    /// <summary>
    /// Ground units that can receive Wall/SteelWall G2G cover. Flying units (Scout) are not covered.
    /// Allied wall cover uses same <see cref="PlayerState.TeamId"/> as the target (map-config alliances).
    /// </summary>
    public static bool IsGroundUnitForWallCover(EntityKind kind, MovementType movementType)
    {
        if (movementType != MovementType.Ground || IsWallKind(kind))
        {
            return false;
        }

        return kind == EntityKind.Commander || UnitKinds.Contains(kind);
    }

    public static bool IsGroundUnitForWallCover(EntityKind kind) =>
        IsGroundUnitForWallCover(kind, GameplayTablesCatalog.Embedded.GetStats(kind).MovementType);

    /// <summary>
    /// Commander and ground army units (not Scout, not buildings/turrets). Used by research selectors
    /// and combat targeting helpers.
    /// </summary>
    public static bool IsGroundCombatUnit(EntityKind kind) =>
        IsGroundCombatUnit(kind, GameplayTablesCatalog.Embedded.GetStats(kind).MovementType);

    public static bool IsGroundCombatUnit(EntityKind kind, MovementType movementType)
    {
        if (movementType != MovementType.Ground)
        {
            return false;
        }

        return kind == EntityKind.Commander || UnitKinds.Contains(kind);
    }

    public static int InserterReachTiles(bool longReach) => longReach ? 2 : 1;

    public static TilePosition InserterPickupTile(TilePosition position, Direction direction, bool longReach)
        => position.Offset(OppositeDirection(direction), InserterReachTiles(longReach));

    public static TilePosition InserterDropTile(TilePosition position, Direction direction, bool longReach)
        => position.Offset(direction, InserterReachTiles(longReach));

    public static Direction OppositeDirection(Direction direction) => direction switch
    {
        Direction.North => Direction.South,
        Direction.East => Direction.West,
        Direction.South => Direction.North,
        Direction.West => Direction.East,
        _ => direction
    };

    public static bool IsInserterKind(EntityKind kind) => kind == EntityKind.Inserter;

    public static bool IsInserterOrGhost(WorldEntity entity)
        => entity.Kind == EntityKind.Inserter
           || (entity.Kind == EntityKind.GhostBuild && entity.BuildTargetKind == EntityKind.Inserter);

    public static CombatTargetCategory GetCombatTargetCategory(EntityKind kind)
    {
        if (IsWallKind(kind))
        {
            return CombatTargetCategory.Wall;
        }

        if (kind == EntityKind.Commander || UnitKinds.Contains(kind))
        {
            return CombatTargetCategory.Unit;
        }

        return CombatTargetCategory.Building;
    }

    /// <summary>
    /// Resistance multipliers in basis points (10_000 = 1.0) for ProjectileKind × target category.
    /// </summary>
    public static int GetResistanceBasisPoints(ProjectileKind projectileKind, CombatTargetCategory targetCategory) =>
        GameplayTablesCatalog.Embedded.GetResistanceBasisPoints(projectileKind, targetCategory);

    public static IReadOnlyDictionary<EntityKind, ProductionRecipe> ProductionRecipes =>
        GameplayTablesCatalog.Embedded.ProductionRecipes;

    public static IReadOnlyDictionary<ItemRecipeId, ItemRecipeDefinition> ItemRecipes =>
        GameplayTablesCatalog.Embedded.ItemRecipes;

    // Research catalog moved to Research/MvpResearchCatalog.cs

    public static IReadOnlyDictionary<EntityKind, int> TechSignatureIntensity =>
        GameplayTablesCatalog.Embedded.TechSignatureIntensity;

    public static EntityStats GetStats(EntityKind kind) => GameplayTablesCatalog.Embedded.GetStats(kind);
}
