namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Processes commander build/demolish/move queues and completes ghost construction.
    /// </summary>
    private static class CommanderOrdersSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.ProcessCommanderBuildOrders();
            sim.ProcessCommanderDemolishOrders();
            sim.ProcessCommanderMoveCommands();
            sim.CompleteGhostBuilds();
        }
    }

    private void ProcessCommanderBuildOrders()
    {
        _spatialQueryIndex.Rebuild(World.Entities, GameplayTables);
        foreach (var commander in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Commander && entity.QueuedBuildOrder is not null).ToList())
        {
            var order = commander.QueuedBuildOrder!;
            if (!CanPlaceBuilding(order.TargetKind, order.TargetPosition))
            {
                commander.QueuedBuildOrder = null;
                continue;
            }

            if (IsWithinBuildRadius(commander, order.TargetKind, order.TargetPosition))
            {
                TryPlaceGhostBuildFromCommander(
                    commander.Id,
                    order.TargetKind,
                    order.TargetPosition,
                    out _,
                    order.Direction,
                    order.SelectedItemRecipe);
                continue;
            }

            MoveMobileEntityTowardTile(commander, order.TargetPosition, _spatialQueryIndex);
        }
    }

    private void ProcessCommanderDemolishOrders()
    {
        _spatialQueryIndex.Rebuild(World.Entities, GameplayTables);
        foreach (var commander in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Commander && entity.QueuedDemolishOrder is not null).ToList())
        {
            var order = commander.QueuedDemolishOrder!;
            var target = World.GetEntity(order.TargetEntityId);
            if (!TryResolveDemolishCostKind(commander, target, out var costKind))
            {
                commander.QueuedDemolishOrder = null;
                continue;
            }

            if (IsWithinBuildRadius(commander, costKind, target!.Position))
            {
                TryDemolishBuilding(commander.Id, order.TargetEntityId);
                continue;
            }

            MoveMobileEntityTowardTile(commander, target.Position, _spatialQueryIndex);
        }
    }

    private void ProcessCommanderMoveCommands()
    {
        _spatialQueryIndex.Rebuild(World.Entities, GameplayTables);
        foreach (var commander in World.Entities.Where(entity =>
                     entity.IsAlive
                     && entity.Kind == EntityKind.Commander
                     && entity.MoveTarget is not null
                     && entity.QueuedBuildOrder is null
                     && entity.QueuedDemolishOrder is null).ToList())
        {
            if (commander.Position == commander.MoveTarget && commander.CurrentWaypoint is null && commander.MovementPath.Count == 0)
            {
                commander.MoveTarget = null;
                continue;
            }

            if (MoveMobileEntityTowardTile(commander, commander.MoveTarget!.Value, _spatialQueryIndex))
            {
                commander.MoveTarget = null;
            }
        }
    }


    private void CompleteGhostBuilds()
    {
        foreach (var ghost in World.Entities.Where(entity => entity.Kind == EntityKind.GhostBuild && entity.IsAlive).ToList())
        {
            ghost.ConstructionTicksRemaining--;
            if (ghost.ConstructionTicksRemaining > 0 || ghost.BuildTargetKind is null || ghost.OwnerId is null)
            {
                continue;
            }

            ghost.Kind = ghost.BuildTargetKind.Value;
            ghost.BuildTargetKind = null;
            var stats = GameplayTables.GetStats(ghost.Kind);
            ghost.MaxHealth = stats.MaxHealth;
            ghost.Health = stats.MaxHealth;
            ConfigureEntityDefaults(ghost);
            SyncResolvedMaxHealth(ghost);
        }
    }
}
