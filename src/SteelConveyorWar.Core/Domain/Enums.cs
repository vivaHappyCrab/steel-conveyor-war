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
    CopperWire,
    Circuit,
    SciencePackT1,
    SciencePackT2,
    Ammo,
    Shell,
    AntiAirShell
}

public enum ItemRecipeId
{
    IronGear,
    CopperWire,
    Circuit,
    SciencePackT1,
    SciencePackT2
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
