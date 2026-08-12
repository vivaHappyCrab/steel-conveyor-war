using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

// H05/R30: The buildable set is composed from the match BuildCostCatalog rather than a hardcoded
// list, so every entity configured in build-costs.json (e.g. UndergroundConveyor/SteelWall/
// CannonTurret/AntiAirTurret) appears in the menu without editing SFML code. The only thing kept
// here is a preferred display ordering; any catalog kind not listed is appended deterministically.
// Production path: ComposeFrom(simulation.BuildCostCatalog) at session start — never Embedded.
public static class BuildMenuCatalog
{
    private static readonly EntityKind[] PreferredOrder =
    [
        // First 10 keep their historical quick-page order/hotkeys (index 9 = Hub -> key "0").
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
        EntityKind.MachineGunTurret,
        // Previously missing from the hardcoded menu; appended so composition matches the catalog.
        EntityKind.UndergroundConveyor,
        EntityKind.SteelWall,
        EntityKind.CannonTurret,
        EntityKind.AntiAirTurret
    ];

    /// <summary>
    /// Embedded-default composition for unit tests only. Production sessions must call
    /// <see cref="ComposeFrom"/> with <see cref="GameSimulation.BuildCostCatalog"/>.
    /// </summary>
    [Obsolete("H05: use ComposeFrom(simulation.BuildCostCatalog) at session start; not for production.")]
    public static EntityKind[] BuildableKinds => ComposeFrom(MvpBuildCostCatalog.Embedded);

    /// <summary>
    /// Deterministically orders every kind present in <paramref name="catalog"/>: known kinds follow
    /// PreferredOrder, and any remaining catalog kinds are appended in EntityKind order.
    /// </summary>
    public static EntityKind[] ComposeFrom(BuildCostCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var remaining = catalog.Costs.Keys.ToHashSet();
        var ordered = new List<EntityKind>(remaining.Count);
        foreach (var kind in PreferredOrder)
        {
            if (remaining.Remove(kind))
            {
                ordered.Add(kind);
            }
        }

        foreach (var kind in remaining.OrderBy(k => (int)k))
        {
            ordered.Add(kind);
        }

        return [.. ordered];
    }
}
