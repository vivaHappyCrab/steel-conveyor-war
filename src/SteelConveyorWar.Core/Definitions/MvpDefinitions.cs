namespace SteelConveyorWar.Core;

public static class MvpDefinitions
{
    public const int CommanderBuildRadius = 12;
    public const int CommanderInteractRadius = 2;
    public const int ConveyorMoveTicks = 10;
    public const int InserterTransferTicks = 12;
    public const int ConveyorMaxItemsPerTile = 2;
    public const int MineWorkTicks = 15;
    public const int HubStorageStacks = 20;
    public const double MobileMoveWorldUnitsPerTick = 0.125;
    public const int BaseBastionTemplateCapacity = 10;
    public const int BaseMaxBastions = 1;
    public const int MaxBastionsAfterUnlock = 4;

    public static readonly HashSet<EntityKind> UnitKinds =
    [
        EntityKind.LightBot,
        EntityKind.BasicTank,
        EntityKind.Scout,
        EntityKind.MediumBot,
        EntityKind.MediumTank,
        EntityKind.AntiAirBot,
        EntityKind.RocketLauncher
    ];

    public static readonly HashSet<EntityKind> FactoryKinds =
    [
        EntityKind.TankFactory,
        EntityKind.DroneCenter
    ];

    public static readonly IReadOnlyDictionary<EntityKind, IReadOnlyDictionary<ItemId, int>> BuildCosts =
        new Dictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>
        {
            [EntityKind.Mine] = Cost((ItemId.IronPlate, 20)),
            [EntityKind.CoalMine] = Cost((ItemId.IronPlate, 25)),
            [EntityKind.OilWell] = Cost((ItemId.IronPlate, 30), (ItemId.CopperPlate, 10)),
            [EntityKind.Smelter] = Cost((ItemId.IronPlate, 15)),
            [EntityKind.Refinery] = Cost((ItemId.IronPlate, 40), (ItemId.CopperPlate, 20)),
            [EntityKind.SolarPanel] = Cost((ItemId.IronPlate, 10), (ItemId.CopperPlate, 10)),
            [EntityKind.CoalPlant] = Cost((ItemId.IronPlate, 25), (ItemId.CopperPlate, 10)),
            [EntityKind.Assembler] = Cost((ItemId.IronPlate, 30), (ItemId.CopperPlate, 15)),
            [EntityKind.Hub] = Cost((ItemId.IronPlate, 20)),
            [EntityKind.Conveyor] = Cost((ItemId.IronPlate, 1)),
            [EntityKind.UndergroundConveyor] = Cost((ItemId.IronPlate, 4), (ItemId.CopperPlate, 2)),
            [EntityKind.Inserter] = Cost((ItemId.IronPlate, 2), (ItemId.CopperPlate, 1)),
            [EntityKind.TankFactory] = Cost((ItemId.IronPlate, 40), (ItemId.CopperPlate, 20)),
            [EntityKind.DroneCenter] = Cost((ItemId.IronPlate, 35), (ItemId.CopperPlate, 25)),
            [EntityKind.Laboratory] = Cost((ItemId.IronPlate, 25), (ItemId.CopperPlate, 20)),
            [EntityKind.Wall] = Cost((ItemId.IronPlate, 2)),
            [EntityKind.SteelWall] = Cost((ItemId.Steel, 2)),
            [EntityKind.MachineGunTurret] = Cost((ItemId.IronPlate, 20), (ItemId.CopperPlate, 10)),
            [EntityKind.CannonTurret] = Cost((ItemId.Steel, 15), (ItemId.CopperPlate, 10)),
            [EntityKind.AntiAirTurret] = Cost((ItemId.Steel, 12), (ItemId.CopperPlate, 15)),
            [EntityKind.Bastion] = Cost((ItemId.IronPlate, 60), (ItemId.CopperPlate, 30), (ItemId.Steel, 10))
        };

    public static readonly IReadOnlyDictionary<EntityKind, int> BuildTicks =
        BuildCosts.Keys.ToDictionary(kind => kind, _ => 30);

    public static readonly IReadOnlyDictionary<EntityKind, TechnologyId> BuildRequirements =
        new Dictionary<EntityKind, TechnologyId>
        {
            [EntityKind.MachineGunTurret] = TechnologyId.MachineGunTurret,
            [EntityKind.Wall] = TechnologyId.ConcreteWalls,
            [EntityKind.UndergroundConveyor] = TechnologyId.UndergroundConveyors,
            [EntityKind.SteelWall] = TechnologyId.SteelWalls,
            [EntityKind.AntiAirTurret] = TechnologyId.AntiAirTurret
        };

    public static readonly IReadOnlyDictionary<EntityKind, int> PowerDemand =
        new Dictionary<EntityKind, int>
        {
            [EntityKind.Mine] = 2,
            [EntityKind.CoalMine] = 2,
            [EntityKind.OilWell] = 3,
            [EntityKind.Smelter] = 3,
            [EntityKind.Refinery] = 5,
            [EntityKind.Assembler] = 4,
            [EntityKind.TankFactory] = 5,
            [EntityKind.DroneCenter] = 4,
            [EntityKind.Laboratory] = 4,
            [EntityKind.MachineGunTurret] = 1,
            [EntityKind.CannonTurret] = 2,
            [EntityKind.AntiAirTurret] = 2
        };

    public static readonly IReadOnlyDictionary<EntityKind, int> PowerProduction =
        new Dictionary<EntityKind, int>
        {
            [EntityKind.SolarPanel] = 5,
            [EntityKind.CoalPlant] = 20
        };

    /// <summary>Per-building energy buffer capacity = demand × this factor (ticks of full drain).</summary>
    public const int EnergyBufferCapacityFactor = 100;

    public static int GetEnergyBufferCapacity(EntityKind kind)
    {
        var demand = PowerDemand.GetValueOrDefault(kind);
        return demand <= 0 ? 0 : demand * EnergyBufferCapacityFactor;
    }

    public static int GetPowerDemand(EntityKind kind) => PowerDemand.GetValueOrDefault(kind);

    public static readonly IReadOnlyDictionary<ItemId, int> ItemStackSizes =
        new Dictionary<ItemId, int>
        {
            [ItemId.IronOre] = 50,
            [ItemId.CopperOre] = 50,
            [ItemId.Coal] = 50,
            [ItemId.CrudeOil] = 50,
            [ItemId.IronPlate] = 100,
            [ItemId.CopperPlate] = 100,
            [ItemId.Steel] = 50,
            [ItemId.Fuel] = 50,
            [ItemId.IronGear] = 100,
            [ItemId.CopperWire] = 200,
            [ItemId.Circuit] = 100,
            [ItemId.SciencePackT1] = 50,
            [ItemId.SciencePackT2] = 50,
            [ItemId.Ammo] = 100,
            [ItemId.Shell] = 50,
            [ItemId.AntiAirShell] = 50
        };

    public static int GetMaxStackSize(ItemId item)
    {
        return ItemStackSizes.GetValueOrDefault(item, 50);
    }

    public static WorldSize GetFootprint(EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Mine or EntityKind.CoalMine or EntityKind.OilWell or EntityKind.Smelter or EntityKind.Assembler or EntityKind.Laboratory or EntityKind.Hub => new WorldSize(2, 2),
            EntityKind.Bastion or EntityKind.TankFactory or EntityKind.DroneCenter => new WorldSize(3, 3),
            _ => new WorldSize(1, 1)
        };
    }

    /// <summary>Player-facing storage UI / inventory rules: only Commander and Hub.</summary>
    public static bool HasPlayerInventory(EntityKind kind) =>
        kind is EntityKind.Commander or EntityKind.Hub;

    public static CollisionSize GetCollisionSize(EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Commander => new CollisionSize(0.35),
            EntityKind.Scout => new CollisionSize(0.25),
            EntityKind.LightBot or EntityKind.MediumBot or EntityKind.AntiAirBot or EntityKind.RocketLauncher => new CollisionSize(0.3),
            EntityKind.BasicTank or EntityKind.MediumTank => new CollisionSize(0.4),
            _ => new CollisionSize(0)
        };
    }

    public static bool IsPassableLogistic(EntityKind kind)
    {
        return kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter;
    }

    public static bool BlocksGroundMovement(EntityKind kind)
    {
        return !UnitKinds.Contains(kind) && kind != EntityKind.Commander && !IsPassableLogistic(kind);
    }

    public static bool IsWallKind(EntityKind kind) => kind is EntityKind.Wall or EntityKind.SteelWall;

    /// <summary>
    /// Ground units that can receive Wall/SteelWall G2G cover. Scout is treated as air (no cover).
    /// Allied wall cover uses same <see cref="PlayerState.TeamId"/> as the target (map-config alliances).
    /// </summary>
    public static bool IsGroundUnitForWallCover(EntityKind kind)
    {
        if (kind == EntityKind.Scout || IsWallKind(kind))
        {
            return false;
        }

        return kind == EntityKind.Commander || UnitKinds.Contains(kind);
    }

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
    public static int GetResistanceBasisPoints(ProjectileKind projectileKind, CombatTargetCategory targetCategory)
    {
        return (projectileKind, targetCategory) switch
        {
            (ProjectileKind.GroundToGround, CombatTargetCategory.Unit) => 10_000,
            (ProjectileKind.GroundToGround, CombatTargetCategory.Building) => 9_000,
            (ProjectileKind.GroundToGround, CombatTargetCategory.Wall) => 7_000,
            (ProjectileKind.Ballistic, CombatTargetCategory.Unit) => 10_000,
            (ProjectileKind.Ballistic, CombatTargetCategory.Building) => 11_000,
            (ProjectileKind.Ballistic, CombatTargetCategory.Wall) => 13_000,
            (ProjectileKind.AirToGround, CombatTargetCategory.Unit) => 11_000,
            (ProjectileKind.AirToGround, CombatTargetCategory.Building) => 5_000,
            (ProjectileKind.AirToGround, CombatTargetCategory.Wall) => 4_000,
            _ => 10_000
        };
    }

    public static readonly IReadOnlyDictionary<EntityKind, ProductionRecipe> ProductionRecipes =
        new Dictionary<EntityKind, ProductionRecipe>
        {
            [EntityKind.LightBot] = new(Cost((ItemId.IronPlate, 5)), EntityKind.LightBot, 60, TechnologyId.LightBot),
            [EntityKind.BasicTank] = new(Cost((ItemId.IronPlate, 12), (ItemId.CopperPlate, 4)), EntityKind.BasicTank, 105),
            [EntityKind.Scout] = new(Cost((ItemId.CopperPlate, 8)), EntityKind.Scout, 60, TechnologyId.Scout),
            [EntityKind.MediumBot] = new(Cost((ItemId.Steel, 5)), EntityKind.MediumBot, 105, TechnologyId.MediumBot),
            [EntityKind.MediumTank] = new(Cost((ItemId.Steel, 10), (ItemId.Fuel, 3)), EntityKind.MediumTank, 150, TechnologyId.MediumTank),
            [EntityKind.AntiAirBot] = new(Cost((ItemId.Steel, 6), (ItemId.CopperPlate, 8)), EntityKind.AntiAirBot, 120, TechnologyId.AntiAirTurret),
            [EntityKind.RocketLauncher] = new(Cost((ItemId.Steel, 8), (ItemId.Fuel, 5)), EntityKind.RocketLauncher, 165, TechnologyId.RocketLauncher)
        };

    public static readonly IReadOnlyDictionary<ItemRecipeId, ItemRecipeDefinition> ItemRecipes =
        new Dictionary<ItemRecipeId, ItemRecipeDefinition>
        {
            [ItemRecipeId.IronGear] = new(ItemRecipeId.IronGear, Cost((ItemId.IronPlate, 2)), ItemId.IronGear, 1, 20),
            [ItemRecipeId.CopperWire] = new(ItemRecipeId.CopperWire, Cost((ItemId.CopperPlate, 1)), ItemId.CopperWire, 2, 15),
            [ItemRecipeId.Circuit] = new(ItemRecipeId.Circuit, Cost((ItemId.IronPlate, 1), (ItemId.CopperWire, 2)), ItemId.Circuit, 1, 30),
            [ItemRecipeId.SciencePackT1] = new(ItemRecipeId.SciencePackT1, Cost((ItemId.IronGear, 1), (ItemId.CopperPlate, 1)), ItemId.SciencePackT1, 1, 35),
            [ItemRecipeId.SciencePackT2] = new(ItemRecipeId.SciencePackT2, Cost((ItemId.Circuit, 1), (ItemId.Steel, 1), (ItemId.Fuel, 1)), ItemId.SciencePackT2, 1, 45)
        };

    // Research catalog moved to Research/MvpResearchCatalog.cs


    public static readonly IReadOnlyDictionary<EntityKind, int> TechSignatureIntensity =
        new Dictionary<EntityKind, int>
        {
            [EntityKind.Smelter] = 2,
            [EntityKind.TankFactory] = 4,
            [EntityKind.DroneCenter] = 3,
            [EntityKind.Refinery] = 4,
            [EntityKind.Assembler] = 3,
            [EntityKind.Laboratory] = 3,
            [EntityKind.CoalPlant] = 2
        };

    public static EntityStats GetStats(EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Commander => new EntityStats(300, 10, 3, 25, 8, 7, Armor: 2, ProjectileKind: ProjectileKind.GroundToGround),
            EntityKind.Bastion => new EntityStats(450, VisionRadius: 12, Armor: 4),
            EntityKind.Hub => new EntityStats(150, VisionRadius: 4, Armor: 1),
            EntityKind.Mine or EntityKind.CoalMine or EntityKind.OilWell => new EntityStats(120, VisionRadius: 3, Armor: 1),
            EntityKind.Smelter or EntityKind.Refinery => new EntityStats(120, VisionRadius: 3, Armor: 1),
            EntityKind.Assembler => new EntityStats(130, VisionRadius: 3, Armor: 1),
            EntityKind.SolarPanel or EntityKind.CoalPlant => new EntityStats(90, VisionRadius: 3, Armor: 1),
            EntityKind.TankFactory or EntityKind.DroneCenter or EntityKind.Laboratory => new EntityStats(160, VisionRadius: 4, Armor: 2),
            EntityKind.Wall => new EntityStats(180, VisionRadius: 1, Armor: 8),
            EntityKind.SteelWall => new EntityStats(320, VisionRadius: 1, Armor: 14),
            EntityKind.MachineGunTurret => new EntityStats(130, 8, 5, 10, VisionRadius: 6, Armor: 2, ProjectileKind: ProjectileKind.GroundToGround),
            EntityKind.CannonTurret => new EntityStats(170, 24, 6, 25, VisionRadius: 7, Armor: 4, ProjectileKind: ProjectileKind.Ballistic, SplashRadius: 1),
            EntityKind.AntiAirTurret => new EntityStats(140, 14, 6, 15, VisionRadius: 7, Armor: 2, ProjectileKind: ProjectileKind.AirToGround),
            EntityKind.LightBot => new EntityStats(35, 5, 1, 18, 5, 4, Armor: 0, ProjectileKind: ProjectileKind.GroundToGround),
            EntityKind.BasicTank => new EntityStats(90, 14, 3, 24, 9, 5, Armor: 3, ProjectileKind: ProjectileKind.GroundToGround),
            EntityKind.Scout => new EntityStats(25, 0, 0, 30, 3, 10, Armor: 0),
            EntityKind.MediumBot => new EntityStats(60, 10, 1, 16, 4, 5, Armor: 1, ProjectileKind: ProjectileKind.GroundToGround),
            EntityKind.MediumTank => new EntityStats(150, 24, 4, 28, 10, 6, Armor: 5, ProjectileKind: ProjectileKind.Ballistic, SplashRadius: 1),
            EntityKind.AntiAirBot => new EntityStats(70, 10, 4, 16, 6, 6, Armor: 1, ProjectileKind: ProjectileKind.AirToGround),
            EntityKind.RocketLauncher => new EntityStats(75, 32, 7, 36, 12, 6, Armor: 0, ProjectileKind: ProjectileKind.Ballistic, SplashRadius: 2),
            EntityKind.GhostBuild => new EntityStats(20, VisionRadius: 0),
            EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter => new EntityStats(40, VisionRadius: 1, Armor: 0),
            _ => new EntityStats(60, VisionRadius: 2, Armor: 1)
        };
    }

    private static IReadOnlyDictionary<ItemId, int> Cost(params (ItemId Item, int Amount)[] costs)
    {
        return costs.ToDictionary(cost => cost.Item, cost => cost.Amount);
    }
}
