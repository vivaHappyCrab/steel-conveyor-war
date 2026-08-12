using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public static class BuildBarModel
{
    public const int MaxDisplayedAfford = 9;

    public static bool IsDirectedKind(EntityKind kind)
    {
        return kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter;
    }

    public static string Glyph(EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Mine => "M",
            EntityKind.CoalMine => "C",
            EntityKind.OilWell => "O",
            EntityKind.Smelter => "S",
            EntityKind.Refinery => "R",
            EntityKind.Conveyor => "=",
            EntityKind.UndergroundConveyor => "U",
            EntityKind.Inserter => "I",
            EntityKind.Assembler => "A",
            EntityKind.TankFactory => "F",
            EntityKind.Hub => "H",
            EntityKind.Laboratory => "L",
            EntityKind.DroneCenter => "D",
            EntityKind.SolarPanel => "*",
            EntityKind.CoalPlant => "P",
            EntityKind.Bastion => "B",
            EntityKind.Wall => "#",
            EntityKind.SteelWall => "W",
            EntityKind.MachineGunTurret => "T",
            EntityKind.CannonTurret => "K",
            EntityKind.AntiAirTurret => "Y",
            _ => "?"
        };
    }

    public static string? ShortcutBadge(int catalogIndex)
    {
        return catalogIndex switch
        {
            >= 0 and <= 8 => (catalogIndex + 1).ToString(),
            9 => "0",
            _ => null
        };
    }

    /// <summary>
    /// R30: Affordability is delegated to a single Core query so the UI count matches what a real
    /// ghost-build would pay for, including stock in nearby owned hubs — not just commander inventory.
    /// </summary>
    public static int AffordableBuilds(GameSimulation simulation, WorldEntity? commander, EntityKind kind)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return commander is null ? 0 : simulation.GetAffordableBuildCount(commander, kind);
    }

    public static string AffordLabel(int affordable)
    {
        if (affordable <= 0)
        {
            return "0";
        }

        return affordable > MaxDisplayedAfford ? $"{MaxDisplayedAfford}+" : affordable.ToString();
    }

    public static string Tooltip(EntityKind kind, int affordable, Direction pendingDirection, ItemRecipeId? pendingRecipe)
    {
        var parts = new List<string> { kind.ToString(), $"afford {AffordLabel(affordable)}" };
        if (IsDirectedKind(kind))
        {
            parts.Add($"dir {pendingDirection}");
        }

        if (kind == EntityKind.Assembler && pendingRecipe is not null)
        {
            parts.Add($"recipe {pendingRecipe}");
        }

        return string.Join(" · ", parts);
    }

    public static EntityKind ResolveCopyKind(WorldEntity entity)
    {
        return entity.Kind == EntityKind.GhostBuild && entity.BuildTargetKind is not null
            ? entity.BuildTargetKind.Value
            : entity.Kind;
    }

    /// <summary>
    /// H05: requires the match <paramref name="buildCosts"/> — no Embedded fallback in production.
    /// </summary>
    public static bool TryCopyFromWorldEntity(
        WorldEntity entity,
        BuildCostCatalog buildCosts,
        out EntityKind kind,
        out Direction direction,
        out ItemRecipeId? recipe)
    {
        ArgumentNullException.ThrowIfNull(buildCosts);
        kind = ResolveCopyKind(entity);
        direction = Direction.East;
        recipe = null;
        if (!buildCosts.Costs.ContainsKey(kind))
        {
            return false;
        }

        if (IsDirectedKind(kind))
        {
            direction = entity.Direction;
        }

        if (kind == EntityKind.Assembler)
        {
            recipe = entity.SelectedItemRecipe;
        }

        return true;
    }
}
