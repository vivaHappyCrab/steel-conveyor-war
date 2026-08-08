using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public static class BuildMenuCatalog
{
    public static readonly EntityKind[] BuildableKinds =
    [
        EntityKind.Mine,
        EntityKind.CoalMine,
        EntityKind.OilWell,
        EntityKind.Smelter,
        EntityKind.Refinery,
        EntityKind.Conveyor,
        EntityKind.Inserter,
        EntityKind.Assembler,
        EntityKind.TankFactory,
        EntityKind.Hub,
        EntityKind.Laboratory,
        EntityKind.DroneCenter,
        EntityKind.SolarPanel,
        EntityKind.CoalPlant,
        EntityKind.Bastion,
        EntityKind.Wall,
        EntityKind.MachineGunTurret
    ];
}
