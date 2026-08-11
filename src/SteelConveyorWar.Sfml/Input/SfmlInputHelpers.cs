using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class SfmlInputHelpers
{
    internal static bool TryGetRecipeShortcut(string key, out ItemRecipeId recipeId)
    {
        return key switch
        {
            "Num1" => SetRecipe(ItemRecipeId.IronGear, out recipeId),
            "Num2" => SetRecipe(ItemRecipeId.Composite, out recipeId),
            "Num3" => SetRecipe(ItemRecipeId.SciencePackT1, out recipeId),
            "Num4" => SetRecipe(ItemRecipeId.SciencePackT2, out recipeId),
            _ => SetRecipe(default, out recipeId, success: false)
        };
    }

    internal static bool TryGetNumberShortcut(string key, out int index)
    {
        return key switch
        {
            "Num1" => SetIndex(0, out index),
            "Num2" => SetIndex(1, out index),
            "Num3" => SetIndex(2, out index),
            "Num4" => SetIndex(3, out index),
            "Num5" => SetIndex(4, out index),
            "Num6" => SetIndex(5, out index),
            "Num7" => SetIndex(6, out index),
            "Num8" => SetIndex(7, out index),
            "Num9" => SetIndex(8, out index),
            "Num0" => SetIndex(9, out index),
            _ => SetIndex(0, out index, success: false)
        };
    }

    internal static bool SetRecipe(ItemRecipeId value, out ItemRecipeId recipeId, bool success = true)
    {
        recipeId = value;
        return success;
    }

    internal static bool SetIndex(int value, out int index, bool success = true)
    {
        index = value;
        return success;
    }

    internal static void EnsureLocalCommanderSelected(GameSimulation simulation, PlayerId localPlayer, ref int? selectedEntityId)
    {
        var selected = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
        if (selected?.Kind == EntityKind.Commander && selected.OwnerId == localPlayer)
        {
            return;
        }

        selectedEntityId = simulation.World.Entities
            .First(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Commander && entity.IsAlive)
            .Id;
    }

    internal static bool TrySelectOwnedBastionByIndex(
        GameSimulation simulation,
        PlayerId localPlayer,
        int index,
        ref int? selectedEntityId)
    {
        var bastions = simulation.World.Entities
            .Where(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Bastion && entity.IsAlive)
            .OrderBy(entity => entity.Id)
            .ToList();
        if (index < 0 || index >= bastions.Count)
        {
            return false;
        }

        selectedEntityId = bastions[index].Id;
        return true;
    }

    internal static Direction RotateDirection(Direction direction, bool clockwise)
    {
        return clockwise
            ? direction switch
            {
                Direction.North => Direction.East,
                Direction.East => Direction.South,
                Direction.South => Direction.West,
                Direction.West => Direction.North,
                _ => direction
            }
            : direction switch
            {
                Direction.North => Direction.West,
                Direction.West => Direction.South,
                Direction.South => Direction.East,
                Direction.East => Direction.North,
                _ => direction
            };
    }

    internal static void ToggleResearchAllocation(GameSimulation simulation, SfmlCommandGateway commands, PlayerId playerId)
    {
        var snapshot = simulation.GetResearchSnapshot(playerId);
        if (snapshot.Tracks.Count < 2 || !snapshot.Tracks.Any(track => track.PlayerAdjustableAllocation))
        {
            return;
        }

        var cycle = snapshot.Tracks.FirstOrDefault(track => track.Id.Contains("cycle", StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Tracks[0];
        var tactical = snapshot.Tracks.FirstOrDefault(track => track.Id != cycle.Id) ?? snapshot.Tracks[^1];
        var fullCycle = cycle.AllocationBasisPoints >= 10_000;

        // R02: enqueue through the command sink rather than mutating research allocation immediately.
        commands.SetTrackAllocation(playerId, new Dictionary<string, int>
        {
            [cycle.Id] = fullCycle ? 7_000 : 10_000,
            [tactical.Id] = fullCycle ? 3_000 : 0
        });
    }

}
