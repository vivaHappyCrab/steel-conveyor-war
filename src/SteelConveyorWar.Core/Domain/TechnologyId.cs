namespace SteelConveyorWar.Core;

public readonly record struct TechnologyId(string Value) : IComparable<TechnologyId>
{
    public static readonly TechnologyId LightBot = new("technology.t1.light-bot");
    public static readonly TechnologyId ImprovedConveyors = new("technology.t1.improved-conveyors");
    public static readonly TechnologyId MachineGunTurret = new("technology.t1.machine-gun-turret");
    public static readonly TechnologyId ConcreteWalls = new("technology.t1.concrete-walls");
    public static readonly TechnologyId Scout = new("technology.t1.scout");
    public static readonly TechnologyId MediumBot = new("technology.t2.medium-bot");
    public static readonly TechnologyId MediumTank = new("technology.t2.medium-tank");
    public static readonly TechnologyId RocketLauncher = new("technology.t2.rocket-launcher");
    public static readonly TechnologyId ConstructionDrone = new("technology.t2.construction-drone");
    public static readonly TechnologyId AntiAirTurret = new("technology.t2.anti-air-turret");
    public static readonly TechnologyId UndergroundConveyors = new("technology.t2.underground-conveyors");
    public static readonly TechnologyId SteelWalls = new("technology.t2.steel-walls");
    public static readonly TechnologyId AdditionalBastions = new("technology.t2.additional-bastions");

    public static readonly TechnologyId ProductionI = new("technology.t1.production-i");
    public static readonly TechnologyId EnergyI = new("technology.t1.energy-i");
    public static readonly TechnologyId CommandI = new("technology.t1.command-i");
    public static readonly TechnologyId MetallurgyII = new("technology.t2.metallurgy-ii");
    public static readonly TechnologyId SupplyII = new("technology.t2.supply-ii");
    public static readonly TechnologyId CommandII = new("technology.t2.command-ii");

    public int CompareTo(TechnologyId other) => string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;
}
