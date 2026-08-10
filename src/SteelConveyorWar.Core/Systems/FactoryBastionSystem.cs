namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Factory unit production, bastion garrison/orders, and bastion-death cascade.
    /// </summary>
    private static class FactoryBastionSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.ProcessFactoryProduction();
            sim.ProcessBastions();
        }
    }

    private void ProcessFactoryProduction()
    {
        foreach (var factory in World.Entities.Where(entity => entity.IsAlive && MvpDefinitions.FactoryKinds.Contains(entity.Kind)).OrderBy(entity => entity.Id))
        {
            if (factory.WorkTicksRemaining > 0 && factory.ProductionTargetKind is not null)
            {
                if (!TryConsumeBuildingEnergy(factory))
                {
                    continue;
                }

                factory.WorkTicksRemaining--;
                if (factory.WorkTicksRemaining == 0)
                {
                    if (!TrySpawnProducedUnit(factory, factory.ProductionTargetKind.Value))
                    {
                        // Keep craft in-flight until a free collision tile appears; do not drop the unit.
                        factory.WorkTicksRemaining = 1;
                        continue;
                    }

                    factory.WorkTicksTotal = 0;
                    if (!factory.IsManualProductionTarget)
                    {
                        factory.ProductionTargetKind = null;
                    }
                }

                continue;
            }

            // Autofill re-resolves every idle tick across all owned bastion deficits.
            if (!factory.IsManualProductionTarget)
            {
                factory.ProductionTargetKind = ChooseBastionDeficit(factory);
            }

            if (factory.ProductionTargetKind is null || !MvpDefinitions.ProductionRecipes.TryGetValue(factory.ProductionTargetKind.Value, out var recipe))
            {
                continue;
            }

            if (!IsRecipeUnlockedForOwner(factory.OwnerId, recipe))
            {
                continue;
            }

            // Manual and autofill both wait when template demand or army capacity is full.
            if (factory.OwnerId is null || !CanStartUnitProduction(factory.OwnerId.Value, recipe.OutputKind))
            {
                continue;
            }

            if (!CanFactoryProduce(factory.Kind, recipe.OutputKind) || !factory.InputBuffer.TryRemoveAll(recipe.Inputs))
            {
                continue;
            }

            var workTicks = recipe.WorkTicks;
            if (factory.OwnerId is not null)
            {
                workTicks = ResolveStat(factory.OwnerId.Value, ResearchStatIds.FactoryWorkTicks, workTicks, recipe.OutputKind.ToString());
            }

            factory.WorkTicksTotal = workTicks;
            factory.WorkTicksRemaining = workTicks;
        }
    }

    private EntityKind? ChooseBastionDeficit(WorldEntity factory)
    {
        if (factory.OwnerId is null)
        {
            return null;
        }

        foreach (var bastion in World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.OwnerId == factory.OwnerId
                && entity.Kind == EntityKind.Bastion)
            .OrderBy(entity => entity.Id))
        {
            foreach (var desired in bastion.BastionTemplate.OrderBy(pair => pair.Key))
            {
                if (desired.Value <= 0 || !CanFactoryProduce(factory.Kind, desired.Key))
                {
                    continue;
                }

                if (!MvpDefinitions.ProductionRecipes.TryGetValue(desired.Key, out var recipe)
                    || !IsRecipeUnlockedForOwner(factory.OwnerId, recipe))
                {
                    continue;
                }

                var current = CountBastionUnitSupply(bastion.Id, desired.Key);
                if (current < desired.Value)
                {
                    return desired.Key;
                }
            }
        }

        return null;
    }

    private int CountBastionUnitSupply(int bastionId, EntityKind unitKind)
    {
        var living = World.Entities.Count(entity =>
            entity.IsAlive
            && entity.AssignedBastionId == bastionId
            && entity.Kind == unitKind);

        var bastion = World.GetEntity(bastionId);
        if (bastion?.OwnerId is null)
        {
            return living;
        }

        return living + CountInFlightAttributedToBastion(bastion.OwnerId.Value, bastionId, unitKind);
    }

    /// <summary>
    /// True when player-wide living+in-flight supply of <paramref name="unitKind"/> is below
    /// summed bastion templates for that kind, and total army supply is below template capacity.
    /// </summary>
    private bool CanStartUnitProduction(PlayerId ownerId, EntityKind unitKind)
    {
        var kindDemand = World.Entities
            .Where(entity => entity.IsAlive && entity.OwnerId == ownerId && entity.Kind == EntityKind.Bastion)
            .Sum(bastion => bastion.BastionTemplate.GetValueOrDefault(unitKind));
        if (kindDemand <= 0)
        {
            return false;
        }

        if (CountPlayerUnitKindSupply(ownerId, unitKind) >= kindDemand)
        {
            return false;
        }

        return CountPlayerArmySupply(ownerId) < GetBastionTemplateCapacity(ownerId);
    }

    private int CountPlayerUnitKindSupply(PlayerId ownerId, EntityKind unitKind)
    {
        var living = World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == ownerId
            && entity.Kind == unitKind);
        return living + CountInFlightOfKind(ownerId, unitKind);
    }

    private int CountPlayerArmySupply(PlayerId ownerId)
    {
        var living = World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == ownerId
            && MvpDefinitions.UnitKinds.Contains(entity.Kind));
        var inFlight = World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == ownerId
            && MvpDefinitions.FactoryKinds.Contains(entity.Kind)
            && entity.ProductionTargetKind is not null
            && entity.WorkTicksRemaining > 0
            && MvpDefinitions.UnitKinds.Contains(entity.ProductionTargetKind.Value));
        return living + inFlight;
    }

    private int CountInFlightOfKind(PlayerId ownerId, EntityKind unitKind) =>
        World.Entities.Count(entity =>
            entity.IsAlive
            && entity.OwnerId == ownerId
            && MvpDefinitions.FactoryKinds.Contains(entity.Kind)
            && entity.ProductionTargetKind == unitKind
            && entity.WorkTicksRemaining > 0);

    /// <summary>
    /// Attributes in-flight factory crafts of <paramref name="unitKind"/> to bastions greedily:
    /// lowest factory Id fills the lowest bastion Id that still has living-based deficit.
    /// </summary>
    private int CountInFlightAttributedToBastion(PlayerId ownerId, int bastionId, EntityKind unitKind)
    {
        var bastions = World.Entities
            .Where(entity => entity.IsAlive && entity.OwnerId == ownerId && entity.Kind == EntityKind.Bastion)
            .OrderBy(entity => entity.Id)
            .ToList();

        var remainingDeficit = new Dictionary<int, int>();
        foreach (var bastion in bastions)
        {
            var desired = bastion.BastionTemplate.GetValueOrDefault(unitKind);
            var living = World.Entities.Count(entity =>
                entity.IsAlive
                && entity.AssignedBastionId == bastion.Id
                && entity.Kind == unitKind);
            remainingDeficit[bastion.Id] = Math.Max(0, desired - living);
        }

        var attributed = 0;
        foreach (var factory in World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.OwnerId == ownerId
                && MvpDefinitions.FactoryKinds.Contains(entity.Kind)
                && entity.ProductionTargetKind == unitKind
                && entity.WorkTicksRemaining > 0)
            .OrderBy(entity => entity.Id))
        {
            _ = factory;
            var assignee = remainingDeficit
                .Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key)
                .Select(pair => (int?)pair.Key)
                .FirstOrDefault();
            if (assignee is null)
            {
                continue;
            }

            remainingDeficit[assignee.Value]--;
            if (assignee.Value == bastionId)
            {
                attributed++;
            }
        }

        return attributed;
    }

    private bool IsRecipeUnlockedForOwner(PlayerId? ownerId, ProductionRecipe recipe)
    {
        if (ownerId is null || recipe.RequiredTechnology is null)
        {
            return true;
        }

        var owner = GetPlayer(ownerId.Value);
        return owner.ResearchedTechnologies.Contains(recipe.RequiredTechnology.Value)
            || CapabilityResolver.IsRecipeUnlocked(owner.Research, recipe.OutputKind.ToString())
            || owner.Research.UnlockedEntityKinds.Contains(recipe.OutputKind.ToString());
    }

    private static bool CanFactoryProduce(EntityKind factoryKind, EntityKind unitKind)
    {
        return unitKind == EntityKind.Scout ? factoryKind == EntityKind.DroneCenter : factoryKind == EntityKind.TankFactory;
    }

    private bool TrySpawnProducedUnit(WorldEntity factory, EntityKind unitKind)
    {
        if (factory.OwnerId is null)
        {
            return false;
        }

        if (!TryFindSpawnTileNear(factory, unitKind, out var spawnTile))
        {
            return false;
        }

        var unit = CreateEntity(unitKind, spawnTile, factory.OwnerId);
        var bastionId = ChooseSpawnBastionId(factory.OwnerId.Value, unitKind);
        unit.AssignedBastionId = bastionId;
        if (bastionId is not null)
        {
            var bastion = World.GetEntity(bastionId.Value);
            if (bastion is not null)
            {
                ApplyBastionOrderToUnit(unit, bastion.Order);
            }
        }

        World.AddEntity(unit);
        return true;
    }

    /// <summary>
    /// Lowest owned bastion Id that still needs <paramref name="unitKind"/> (living count vs template).
    /// Returns null when no bastion has a deficit (production should have waited at the factory).
    /// </summary>
    private int? ChooseSpawnBastionId(PlayerId ownerId, EntityKind unitKind)
    {
        foreach (var bastion in World.Entities
            .Where(entity => entity.IsAlive && entity.OwnerId == ownerId && entity.Kind == EntityKind.Bastion)
            .OrderBy(entity => entity.Id))
        {
            var desired = bastion.BastionTemplate.GetValueOrDefault(unitKind);
            if (desired <= 0)
            {
                continue;
            }

            var living = World.Entities.Count(entity =>
                entity.IsAlive
                && entity.AssignedBastionId == bastion.Id
                && entity.Kind == unitKind);
            if (living < desired)
            {
                return bastion.Id;
            }
        }

        return null;
    }

    private bool TryFindSpawnTileNear(WorldEntity factory, EntityKind unitKind, out TilePosition spawnTile)
    {
        var footprint = MvpDefinitions.GetFootprint(factory.Kind);
        for (var ring = 1; ring <= 6; ring++)
        {
            var candidates = new List<TilePosition>();
            var minX = factory.Position.X - ring;
            var maxX = factory.Position.X + footprint.Width - 1 + ring;
            var minY = factory.Position.Y - ring;
            var maxY = factory.Position.Y + footprint.Height - 1 + ring;
            for (var x = minX; x <= maxX; x++)
            {
                candidates.Add(new TilePosition(x, minY));
                candidates.Add(new TilePosition(x, maxY));
            }

            for (var y = minY + 1; y <= maxY - 1; y++)
            {
                candidates.Add(new TilePosition(minX, y));
                candidates.Add(new TilePosition(maxX, y));
            }

            foreach (var tile in candidates
                         .Where(tile => World.IsInside(tile))
                         .OrderBy(tile => tile.ManhattanDistance(factory.Position))
                         .ThenBy(tile => tile.X)
                         .ThenBy(tile => tile.Y))
            {
                var probe = new WorldEntity(-1, unitKind, tile, factory.OwnerId);
                if (IsGroundPassable(probe, tile) && CanOccupyWorldPosition(probe, probe.WorldPosition))
                {
                    spawnTile = tile;
                    return true;
                }
            }
        }

        spawnTile = default;
        return false;
    }

    private void ProcessBastions()
    {
        foreach (var bastion in World.Entities.Where(entity => entity.IsAlive && entity.Kind == EntityKind.Bastion).OrderBy(entity => entity.Id))
        {
            TryCompleteBastionOrder(bastion);

            var units = World.Entities
                .Where(entity => entity.IsAlive && entity.AssignedBastionId == bastion.Id && MvpDefinitions.UnitKinds.Contains(entity.Kind))
                .OrderBy(entity => entity.Id)
                .ToList();

            // Home units keep Defend while bastion is Scout/AttackArea; garrison them,
            // but sortie only during active bastion Defend.
            var allowSortie = bastion.Order.Kind == BastionOrderKind.Defend;
            var threat = allowSortie
                ? FindNearestEnemyInRange(bastion, GetBastionVisionRadius(bastion))
                : null;

            foreach (var unit in units)
            {
                if (unit.Order.Kind != BastionOrderKind.Defend)
                {
                    continue;
                }

                if (threat is not null)
                {
                    TryEjectFromGarrison(unit);
                    continue;
                }

                // Bastion is multi-tile; units stop on the footprint perimeter (building collision)
                // and must hide when adjacent to any footprint tile — not only near bastion.Position.
                if (IsWithinBastionGarrisonRange(bastion, unit.Position))
                {
                    World.RelocateEntity(unit, bastion.Position);
                    unit.WorldPosition = WorldPosition.FromTileCenter(bastion.Position);
                    ResetMovementPath(unit);
                    unit.IsGarrisoned = true;
                }
            }
        }
    }

    /// <summary>
    /// Clears garrison and relocates the unit to a free perimeter tile outside the bastion
    /// footprint so continuous collision can move again (Attack/Scout/Defend sortie).
    /// </summary>
    private void TryEjectFromGarrison(WorldEntity unit)
    {
        if (!unit.IsGarrisoned)
        {
            return;
        }

        WorldEntity? bastion = null;
        if (unit.AssignedBastionId is { } bastionId)
        {
            bastion = World.GetEntity(bastionId);
        }

        if (bastion is not null
            && bastion.IsAlive
            && bastion.Kind == EntityKind.Bastion
            && TryFindSpawnTileNear(bastion, unit.Kind, out var ejectTile))
        {
            World.RelocateEntity(unit, ejectTile);
            unit.WorldPosition = WorldPosition.FromTileCenter(ejectTile);
            ResetMovementPath(unit);
        }

        unit.IsGarrisoned = false;
    }

    /// <summary>
    /// True when <paramref name="unitPosition"/> is on or Chebyshev-adjacent to the bastion footprint
    /// (units cannot occupy building tiles, so they garrison from the perimeter ring).
    /// </summary>
    private static bool IsWithinBastionGarrisonRange(WorldEntity bastion, TilePosition unitPosition)
    {
        var footprint = MvpDefinitions.GetFootprint(bastion.Kind);
        var minX = bastion.Position.X - 1;
        var maxX = bastion.Position.X + footprint.Width;
        var minY = bastion.Position.Y - 1;
        var maxY = bastion.Position.Y + footprint.Height;
        return unitPosition.X >= minX
            && unitPosition.X <= maxX
            && unitPosition.Y >= minY
            && unitPosition.Y <= maxY;
    }

    private void TryCompleteBastionOrder(WorldEntity bastion)
    {
        if (bastion.Order.Kind == BastionOrderKind.AttackArea && bastion.Order.Target is not null)
        {
            // Scouts join AttackArea for FoW; completion waits for every assigned unit still on
            // AttackArea (combat and scouts), so scout-only pushes and post-wipe leftovers can finish.
            var attackUnits = World.Entities
                .Where(entity =>
                    entity.IsAlive
                    && entity.AssignedBastionId == bastion.Id
                    && MvpDefinitions.UnitKinds.Contains(entity.Kind)
                    && entity.Order.Kind == BastionOrderKind.AttackArea)
                .ToList();
            if (attackUnits.Count > 0
                && attackUnits.All(unit => unit.Position.IsWithinEuclideanRange(bastion.Order.Target.Value, 1)))
            {
                SwitchBastionToDefend(bastion);
            }

            return;
        }

        if (bastion.Order.Kind == BastionOrderKind.Scout && bastion.Order.Target is not null)
        {
            var scouts = World.Entities
                .Where(entity => entity.IsAlive && entity.AssignedBastionId == bastion.Id && entity.Kind == EntityKind.Scout)
                .ToList();
            if (scouts.Count > 0
                && scouts.All(unit => unit.Position.IsWithinEuclideanRange(bastion.Order.Target.Value, 1)))
            {
                SwitchBastionToDefend(bastion);
            }
        }
    }

    private void SwitchBastionToDefend(WorldEntity bastion)
    {
        var defend = new BastionOrder(BastionOrderKind.Defend);
        bastion.Order = defend;
        foreach (var unit in World.Entities.Where(entity =>
                     entity.IsAlive
                     && entity.AssignedBastionId == bastion.Id
                     && MvpDefinitions.UnitKinds.Contains(entity.Kind)))
        {
            unit.Order = defend;
            ResetMovementPath(unit);
        }
    }

    private int GetBastionVisionRadius(WorldEntity bastion)
    {
        var radius = MvpDefinitions.GetStats(EntityKind.Bastion).VisionRadius;
        if (bastion.OwnerId is not null)
        {
            radius = ResolveStat(bastion.OwnerId.Value, ResearchStatIds.VisionRadius, radius, minValue: 0);
        }

        return radius;
    }

    private WorldEntity? FindNearestEnemyInRange(WorldEntity origin, int radius)
    {
        if (origin.OwnerId is null)
        {
            return null;
        }

        WorldEntity? best = null;
        var bestDistanceSquared = 0;
        var entities = World.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (!entity.IsAlive
                || entity.IsGarrisoned
                || entity.OwnerId is null
                || AreAllied(origin.OwnerId, entity.OwnerId)
                || !origin.Position.IsWithinEuclideanRange(entity.Position, radius))
            {
                continue;
            }

            var distanceSquared = origin.Position.EuclideanDistanceSquared(entity.Position);
            if (best is null
                || distanceSquared < bestDistanceSquared
                || (distanceSquared == bestDistanceSquared && entity.Id < best.Id))
            {
                best = entity;
                bestDistanceSquared = distanceSquared;
            }
        }

        return best;
    }

    private void CascadeBastionDeaths()
    {
        _scratchDeadBastions.Clear();
        var entities = World.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (entity.Kind == EntityKind.Bastion && !entity.IsAlive)
            {
                _scratchDeadBastions.Add(entity);
            }
        }

        for (var bastionIndex = 0; bastionIndex < _scratchDeadBastions.Count; bastionIndex++)
        {
            var bastion = _scratchDeadBastions[bastionIndex];
            _scratchCascadeUnits.Clear();
            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity.AssignedBastionId == bastion.Id
                    && entity.IsAlive
                    && MvpDefinitions.UnitKinds.Contains(entity.Kind))
                {
                    _scratchCascadeUnits.Add(entity);
                }
            }

            for (var unitIndex = 0; unitIndex < _scratchCascadeUnits.Count; unitIndex++)
            {
                _scratchCascadeUnits[unitIndex].Health = 0;
            }
        }
    }
}
