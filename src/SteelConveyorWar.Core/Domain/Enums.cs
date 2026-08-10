namespace SteelConveyorWar.Core;

public enum TerrainType
{
    Grass,
    IronOre,
    CopperOre,
    Coal,
    Oil
}

public enum EntityKind
{
    Commander,
    GhostBuild,
    Bastion,
    Hub,
    Mine,
    CoalMine,
    OilWell,
    Smelter,
    Refinery,
    SolarPanel,
    CoalPlant,
    Assembler,
    Conveyor,
    UndergroundConveyor,
    Inserter,
    TankFactory,
    DroneCenter,
    Laboratory,
    Wall,
    SteelWall,
    MachineGunTurret,
    CannonTurret,
    AntiAirTurret,
    LightBot,
    BasicTank,
    Scout,
    MediumBot,
    MediumTank,
    AntiAirBot,
    RocketLauncher
}

public enum ItemId
{
    IronOre,
    CopperOre,
    Coal,
    CrudeOil,
    IronPlate,
    CopperPlate,
    Steel,
    Fuel,
    IronGear,
    Composite,
    SciencePackT1,
    SciencePackT2,
    Ammo,
    Shell,
    AntiAirShell
}

public enum ItemRecipeId
{
    IronGear,
    Composite,
    SciencePackT1,
    SciencePackT2
}

/// <summary>Sticky auto-selected smelter recipe (null = never smelted / no recipe).</summary>
public enum SmeltRecipeId
{
    IronPlate,
    CopperPlate,
    Steel
}

public enum Direction
{
    North,
    East,
    South,
    West
}

public enum GameStatus
{
    InProgress,
    PlayerWon
}

public enum VisibilityState
{
    Unknown,
    Explored,
    Visible
}

public enum BastionOrderKind
{
    Defend,
    AttackArea,
    Patrol,
    Scout
}

public enum ProjectileKind
{
    GroundToGround = 0,
    Ballistic = 1,
    AirToGround = 2
}

/// <summary>
/// Coarse target class for resistance lookup (ProjectileKind × category).
/// </summary>
public enum CombatTargetCategory
{
    Unit = 0,
    Building = 1,
    Wall = 2
}
