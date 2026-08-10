namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Inserter and conveyor item transfer for one tick.
    /// </summary>
    private static class LogisticsSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.ProcessLogistics();
        }
    }

    private void ProcessLogistics()
    {
        ProcessInserters();
        ProcessConveyors();
    }

    private void ProcessInserters()
    {
        var inserters = World.Entities
            .Where(entity => entity.IsAlive && entity.Kind == EntityKind.Inserter)
            .OrderBy(entity => entity.Id)
            .ToList();

        // Empty-handed extract: at most one pull per source entity per tick (fair share by tick + source id).
        var extractGroups = inserters
            .Where(inserter => inserter.HeldItem is null)
            .Select(inserter =>
            {
                var source = World.GetTopEntityAt(inserter.Position.Offset(Opposite(inserter.Direction)));
                return (Inserter: inserter, Source: source);
            })
            .Where(pair => pair.Source is not null)
            .GroupBy(pair => pair.Source!.Id)
            .OrderBy(group => group.Key);

        foreach (var group in extractGroups)
        {
            var candidates = group.OrderBy(pair => pair.Inserter.Id).ToList();
            var start = (int)((Tick + group.Key) % candidates.Count);
            for (var offset = 0; offset < candidates.Count; offset++)
            {
                var index = (start + offset) % candidates.Count;
                var (inserter, source) = candidates[index];
                if (TryExtractItem(source!, inserter.FilterItem, out var item))
                {
                    inserter.HeldItem = item;
                    inserter.HeldTransferTicksRemaining = MvpDefinitions.InserterTransferTicks;
                    break;
                }
            }
        }

        foreach (var inserter in inserters.Where(entity => entity.HeldItem is not null))
        {
            if (inserter.HeldTransferTicksRemaining > 0)
            {
                inserter.HeldTransferTicksRemaining--;
                continue;
            }

            var target = World.GetTopEntityAt(inserter.Position.Offset(inserter.Direction));
            if (target is not null && TryInsertItem(target, inserter.HeldItem!.Value))
            {
                inserter.HeldItem = null;
            }
        }
    }

    private void ProcessConveyors()
    {
        foreach (var conveyor in World.Entities.Where(entity => entity.IsAlive && (entity.Kind == EntityKind.Conveyor || entity.Kind == EntityKind.UndergroundConveyor)).OrderBy(entity => entity.Id).ToList())
        {
            foreach (var conveyorItem in conveyor.ConveyorItems.ToList())
            {
                conveyorItem.ProgressTicks++;
                var moveTicks = MvpDefinitions.ConveyorMoveTicks;
                if (conveyor.OwnerId is not null)
                {
                    moveTicks = ResolveStat(conveyor.OwnerId.Value, ResearchStatIds.ConveyorMoveTicks, moveTicks);
                }

                if (conveyorItem.ProgressTicks < moveTicks)
                {
                    continue;
                }

                var target = World.GetTopEntityAt(conveyor.Position.Offset(conveyor.Direction));
                if (target is not null && TryInsertItem(target, conveyorItem.Item))
                {
                    conveyor.ConveyorItemsMutable.Remove(conveyorItem);
                }
            }
        }
    }

    private static Direction Opposite(Direction direction)
    {
        return direction switch
        {
            Direction.North => Direction.South,
            Direction.East => Direction.West,
            Direction.South => Direction.North,
            Direction.West => Direction.East,
            _ => direction
        };
    }

    private static bool TryExtractItem(WorldEntity source, ItemId? filter, out ItemId item)
    {
        if (source.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor)
        {
            var conveyorItem = source.ConveyorItems
                .OrderBy(slot => slot.Item)
                .FirstOrDefault(slot => filter is null || slot.Item == filter.Value);
            if (conveyorItem is not null)
            {
                item = conveyorItem.Item;
                source.ConveyorItemsMutable.Remove(conveyorItem);
                return true;
            }
        }

        if (source.Kind == EntityKind.Hub && source.Inventory.TryTakeFirst(filter is null ? null : candidate => candidate == filter.Value, out item))
        {
            return true;
        }

        if (IsBuildingWithBuffers(source.Kind) && source.OutputBuffer.TryTakeFirst(filter is null ? null : candidate => candidate == filter.Value, out item))
        {
            return true;
        }

        return source.Inventory.TryTakeFirst(filter is null ? null : candidate => candidate == filter.Value, out item);
    }

    private bool TryInsertItem(WorldEntity target, ItemId item)
    {
        if (target.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor)
        {
            return TryAddToConveyor(target, item, progressTicks: 0);
        }

        if (target.Kind == EntityKind.Hub)
        {
            return target.Inventory.TryAddWithinTotalStackLimit(item, 1, GetHubStorageStacks(target.OwnerId));
        }

        return IsBuildingWithBuffers(target.Kind)
            ? TryAddToBuffer(target.InputBuffer, item, 1)
            : target.Inventory.TryAddWithinStackLimit(item, 1);
    }

    private static bool TryAddToBuffer(Inventory buffer, ItemId item, int amount)
    {
        return buffer.TryAddWithinStackLimit(item, amount);
    }

    private static bool TryAddToConveyor(WorldEntity conveyor, ItemId item, int progressTicks)
    {
        if (conveyor.ConveyorItems.Count >= MvpDefinitions.ConveyorMaxItemsPerTile)
        {
            return false;
        }

        conveyor.ConveyorItemsMutable.Add(new ConveyorItem(item, progressTicks));
        return true;
    }

    private static bool IsBuildingWithBuffers(EntityKind kind)
    {
        return kind is EntityKind.Mine
            or EntityKind.CoalMine
            or EntityKind.OilWell
            or EntityKind.Smelter
            or EntityKind.Refinery
            or EntityKind.Assembler
            or EntityKind.SolarPanel
            or EntityKind.CoalPlant
            or EntityKind.Hub
            or EntityKind.TankFactory
            or EntityKind.DroneCenter
            or EntityKind.Laboratory
            or EntityKind.MachineGunTurret
            or EntityKind.CannonTurret
            or EntityKind.AntiAirTurret;
    }

    private int GetHubStorageStacks(PlayerId? ownerId)
    {
        if (ownerId is null)
        {
            return MvpDefinitions.HubStorageStacks;
        }

        return ResolveStat(ownerId.Value, ResearchStatIds.HubStorageStacks, MvpDefinitions.HubStorageStacks, minValue: 1);
    }
}
