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
        CollectSortedAliveEntities(_scratchEntities, static entity => entity.Kind == EntityKind.Inserter);

        // Empty-handed extract: at most one pull per source entity per tick (fair share by tick + source id).
        _scratchInserterExtracts.Clear();
        for (var i = 0; i < _scratchEntities.Count; i++)
        {
            var inserter = _scratchEntities[i];
            if (inserter.HeldItem is not null)
            {
                continue;
            }

            var source = World.GetTopEntityAt(MvpDefinitions.InserterPickupTile(
                inserter.Position, inserter.Direction, inserter.InserterLongReach));
            if (source is null)
            {
                continue;
            }

            _scratchInserterExtracts.Add((source.Id, inserter, source));
        }

        _scratchInserterExtracts.Sort(static (left, right) =>
        {
            var bySource = left.SourceId.CompareTo(right.SourceId);
            return bySource != 0 ? bySource : left.Inserter.Id.CompareTo(right.Inserter.Id);
        });

        var extractIndex = 0;
        while (extractIndex < _scratchInserterExtracts.Count)
        {
            var sourceId = _scratchInserterExtracts[extractIndex].SourceId;
            var groupStart = extractIndex;
            while (extractIndex < _scratchInserterExtracts.Count
                   && _scratchInserterExtracts[extractIndex].SourceId == sourceId)
            {
                extractIndex++;
            }

            var groupCount = extractIndex - groupStart;
            var start = (int)((Tick + sourceId) % groupCount);
            for (var offset = 0; offset < groupCount; offset++)
            {
                var index = groupStart + ((start + offset) % groupCount);
                var (_, inserter, source) = _scratchInserterExtracts[index];
                if (TryExtractItem(source, inserter.FilterItem, out var item))
                {
                    inserter.HeldItem = item;
                    inserter.HeldTransferTicksRemaining = MvpDefinitions.InserterTransferTicks;
                    break;
                }
            }
        }

        for (var i = 0; i < _scratchEntities.Count; i++)
        {
            var inserter = _scratchEntities[i];
            if (inserter.HeldItem is null)
            {
                continue;
            }

            if (inserter.HeldTransferTicksRemaining > 0)
            {
                inserter.HeldTransferTicksRemaining--;
                continue;
            }

            var target = World.GetTopEntityAt(MvpDefinitions.InserterDropTile(
                inserter.Position, inserter.Direction, inserter.InserterLongReach));
            if (target is not null && TryInsertItem(target, inserter.HeldItem!.Value))
            {
                inserter.HeldItem = null;
            }
        }
    }

    private void ProcessConveyors()
    {
        CollectSortedAliveEntities(
            _scratchEntities,
            static entity => entity.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor);

        for (var conveyorIndex = 0; conveyorIndex < _scratchEntities.Count; conveyorIndex++)
        {
            var conveyor = _scratchEntities[conveyorIndex];
            _scratchConveyorItems.Clear();
            var liveItems = conveyor.ConveyorItemsMutable;
            for (var itemIndex = 0; itemIndex < liveItems.Count; itemIndex++)
            {
                _scratchConveyorItems.Add(liveItems[itemIndex]);
            }

            for (var itemIndex = 0; itemIndex < _scratchConveyorItems.Count; itemIndex++)
            {
                var conveyorItem = _scratchConveyorItems[itemIndex];
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
                if (target is not null
                    && target.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor
                    && TryInsertItem(target, conveyorItem.Item))
                {
                    conveyor.ConveyorItemsMutable.Remove(conveyorItem);
                }
            }
        }
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
            return target.Inventory.TryAddWithinTotalStackLimit(item, 1, GetHubStorageStacks(target.OwnerId), GameplayTables);
        }

        return IsBuildingWithBuffers(target.Kind)
            ? TryAddToBuffer(target.InputBuffer, item, 1)
            : target.Inventory.TryAddWithinStackLimit(item, 1, GameplayTables);
    }

    private bool TryAddToBuffer(Inventory buffer, ItemId item, int amount)
    {
        return buffer.TryAddWithinStackLimit(item, amount, GameplayTables);
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
