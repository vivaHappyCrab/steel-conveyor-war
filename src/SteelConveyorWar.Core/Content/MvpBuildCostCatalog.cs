namespace SteelConveyorWar.Core;

/// <summary>
/// Embedded parity source for construction balance (same values as historical <c>MvpDefinitions</c> tables).
/// Unit tests and <see cref="GameCreationOptions.Default"/> use this; Client disk startup loads <c>config/build-costs.json</c>.
/// </summary>
public static class MvpBuildCostCatalog
{
    public static BuildCostCatalog Embedded { get; } = CreateEmbedded();

    public static BuildCostCatalog CreateEmbedded()
    {
        var costs = new Dictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>
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
            [EntityKind.Bastion] = Cost((ItemId.IronPlate, 60), (ItemId.CopperPlate, 30))
        };

        var ticks = costs.Keys.ToDictionary(kind => kind, _ => 30);
        var requirements = new Dictionary<EntityKind, TechnologyId>
        {
            [EntityKind.MachineGunTurret] = TechnologyId.MachineGunTurret,
            [EntityKind.UndergroundConveyor] = TechnologyId.UndergroundConveyors,
            [EntityKind.SteelWall] = TechnologyId.SteelWalls,
            [EntityKind.AntiAirTurret] = TechnologyId.AntiAirTurret
        };

        return new BuildCostCatalog(1, costs, ticks, requirements);
    }

    private static IReadOnlyDictionary<ItemId, int> Cost(params (ItemId Item, int Amount)[] costs)
    {
        return costs.ToDictionary(cost => cost.Item, cost => cost.Amount);
    }
}
