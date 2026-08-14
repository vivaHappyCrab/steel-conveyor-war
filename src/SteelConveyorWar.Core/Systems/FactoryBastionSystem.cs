namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Factory unit production, bastion garrison/orders, and bastion-death cascade.
    /// R17: accounting uses reusable sorted scratch buffers (live mid-tick updates on spawn)
    /// instead of repeated LINQ scans of <see cref="GameWorld.Entities"/>.
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
        RefreshArmyAccountingEpochForDeaths();
        CollectSortedAliveEntities(_scratchFactories, static entity => MvpDefinitions.FactoryKinds.Contains(entity.Kind));
        CollectSortedAliveEntities(_scratchBastions, static entity => entity.Kind == EntityKind.Bastion);
        CollectSortedAliveEntities(_scratchUnits, static entity => MvpDefinitions.UnitKinds.Contains(entity.Kind));

        for (var factoryIndex = 0; factoryIndex < _scratchFactories.Count; factoryIndex++)
        {
            var factory = _scratchFactories[factoryIndex];
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

            // Idle skip (manual only): autofill must re-resolve deficits every tick.
            // Same inputs + army epoch + owner research capability epoch ⇒ start outcome unchanged.
            var ownerResearchEpoch = factory.OwnerId is null
                ? 0
                : GetPlayer(factory.OwnerId.Value).Research.CapabilityEpoch;
            if (factory.IsManualProductionTarget
                && factory.IdleFactorySupplyEpoch == _armyAccountingEpoch
                && factory.IdleFactoryInputVersion == factory.InputBuffer.MutationVersion
                && factory.IdleFactoryResearchEpoch == ownerResearchEpoch)
            {
                continue;
            }

            // Autofill re-resolves every idle tick across all owned bastion deficits.
            if (!factory.IsManualProductionTarget)
            {
                factory.ProductionTargetKind = ChooseBastionDeficit(factory);
            }

            if (factory.ProductionTargetKind is null || !GameplayTables.ProductionRecipes.TryGetValue(factory.ProductionTargetKind.Value, out var recipe))
            {
                RememberIdleFactorySkip(factory);
                continue;
            }

            if (!IsRecipeUnlockedForOwner(factory.OwnerId, recipe))
            {
                // H04 Variant B: waiting on unlock must re-check every tick (do not arm idle skip).
                continue;
            }

            // Manual and autofill both wait when template demand or army capacity is full.
            if (factory.OwnerId is null || !CanStartUnitProduction(factory.OwnerId.Value, recipe.OutputKind))
            {
                RememberIdleFactorySkip(factory);
                continue;
            }

            if (!CanFactoryProduce(factory.Kind, recipe.OutputKind) || !factory.InputBuffer.TryRemoveAll(recipe.Inputs))
            {
                RememberIdleFactorySkip(factory);
                continue;
            }

            var workTicks = recipe.WorkTicks;
            if (factory.OwnerId is not null)
            {
                workTicks = ResolveStat(factory.OwnerId.Value, ResearchStatIds.FactoryWorkTicks, workTicks, recipe.OutputKind.ToString());
            }

            factory.WorkTicksTotal = workTicks;
            factory.WorkTicksRemaining = workTicks;
            // In-flight supply changed for later factories in this same tick.
            BumpArmyAccountingEpoch();
        }
    }

    private void RememberIdleFactorySkip(WorldEntity factory)
    {
        factory.IdleFactorySupplyEpoch = _armyAccountingEpoch;
        factory.IdleFactoryInputVersion = factory.InputBuffer.MutationVersion;
        factory.IdleFactoryResearchEpoch = factory.OwnerId is null
            ? 0
            : GetPlayer(factory.OwnerId.Value).Research.CapabilityEpoch;
    }

    private void RefreshArmyAccountingEpochForDeaths()
    {
        var aliveUnits = 0;
        var entities = World.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (entity.IsAlive && MvpDefinitions.UnitKinds.Contains(entity.Kind))
            {
                aliveUnits++;
            }
        }

        if (_armyAccountingAliveUnits >= 0 && aliveUnits != _armyAccountingAliveUnits)
        {
            BumpArmyAccountingEpoch();
        }

        _armyAccountingAliveUnits = aliveUnits;
    }

    private EntityKind? ChooseBastionDeficit(WorldEntity factory)
    {
        if (factory.OwnerId is null)
        {
            return null;
        }

        for (var bastionIndex = 0; bastionIndex < _scratchBastions.Count; bastionIndex++)
        {
            var bastion = _scratchBastions[bastionIndex];
            if (!bastion.IsAlive || bastion.OwnerId != factory.OwnerId)
            {
                continue;
            }

            CollectSortedTemplateKinds(bastion);
            for (var kindIndex = 0; kindIndex < _scratchTemplateKinds.Count; kindIndex++)
            {
                var desiredKind = _scratchTemplateKinds[kindIndex];
                var desiredCount = bastion.BastionTemplate.GetValueOrDefault(desiredKind);
                if (desiredCount <= 0 || !CanFactoryProduce(factory.Kind, desiredKind))
                {
                    continue;
                }

                if (!GameplayTables.ProductionRecipes.TryGetValue(desiredKind, out var recipe)
                    || !IsRecipeUnlockedForOwner(factory.OwnerId, recipe))
                {
                    continue;
                }

                var current = CountBastionUnitSupply(bastion.Id, desiredKind);
                if (current < desiredCount)
                {
                    return desiredKind;
                }
            }
        }

        return null;
    }

    private void CollectSortedTemplateKinds(WorldEntity bastion)
    {
        _scratchTemplateKinds.Clear();
        foreach (var pair in bastion.BastionTemplate)
        {
            _scratchTemplateKinds.Add(pair.Key);
        }

        _scratchTemplateKinds.Sort();
    }

    private int CountBastionUnitSupply(int bastionId, EntityKind unitKind)
    {
        var living = CountLivingAssignedToBastion(bastionId, unitKind);

        var bastion = World.GetEntity(bastionId);
        if (bastion?.OwnerId is null)
        {
            return living;
        }

        return living + CountInFlightAttributedToBastion(bastion.OwnerId.Value, bastionId, unitKind);
    }

    private int CountLivingAssignedToBastion(int bastionId, EntityKind unitKind)
    {
        var living = 0;
        for (var i = 0; i < _scratchUnits.Count; i++)
        {
            var entity = _scratchUnits[i];
            if (entity.IsAlive
                && entity.AssignedBastionId == bastionId
                && entity.Kind == unitKind)
            {
                living++;
            }
        }

        return living;
    }

    /// <summary>
    /// True when player-wide living+in-flight supply of <paramref name="unitKind"/> is below
    /// summed bastion templates for that kind, and total army supply is below template capacity.
    /// </summary>
    private bool CanStartUnitProduction(PlayerId ownerId, EntityKind unitKind)
    {
        var kindDemand = 0;
        for (var i = 0; i < _scratchBastions.Count; i++)
        {
            var bastion = _scratchBastions[i];
            if (bastion.IsAlive && bastion.OwnerId == ownerId)
            {
                kindDemand += bastion.BastionTemplate.GetValueOrDefault(unitKind);
            }
        }

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
        var living = 0;
        for (var i = 0; i < _scratchUnits.Count; i++)
        {
            var entity = _scratchUnits[i];
            if (entity.IsAlive && entity.OwnerId == ownerId && entity.Kind == unitKind)
            {
                living++;
            }
        }

        return living + CountInFlightOfKind(ownerId, unitKind);
    }

    private int CountPlayerArmySupply(PlayerId ownerId)
    {
        var living = 0;
        for (var i = 0; i < _scratchUnits.Count; i++)
        {
            var entity = _scratchUnits[i];
            if (entity.IsAlive && entity.OwnerId == ownerId)
            {
                living++;
            }
        }

        var inFlight = 0;
        for (var i = 0; i < _scratchFactories.Count; i++)
        {
            var entity = _scratchFactories[i];
            if (entity.IsAlive
                && entity.OwnerId == ownerId
                && entity.ProductionTargetKind is not null
                && entity.WorkTicksRemaining > 0
                && MvpDefinitions.UnitKinds.Contains(entity.ProductionTargetKind.Value))
            {
                inFlight++;
            }
        }

        return living + inFlight;
    }

    private int CountInFlightOfKind(PlayerId ownerId, EntityKind unitKind)
    {
        var count = 0;
        for (var i = 0; i < _scratchFactories.Count; i++)
        {
            var entity = _scratchFactories[i];
            if (entity.IsAlive
                && entity.OwnerId == ownerId
                && entity.ProductionTargetKind == unitKind
                && entity.WorkTicksRemaining > 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Attributes in-flight factory crafts of <paramref name="unitKind"/> to bastions greedily:
    /// lowest factory Id fills the lowest bastion Id that still has living-based deficit.
    /// </summary>
    private int CountInFlightAttributedToBastion(PlayerId ownerId, int bastionId, EntityKind unitKind)
    {
        _scratchRemainingDeficit.Clear();
        for (var i = 0; i < _scratchBastions.Count; i++)
        {
            var bastion = _scratchBastions[i];
            if (!bastion.IsAlive || bastion.OwnerId != ownerId)
            {
                continue;
            }

            var desired = bastion.BastionTemplate.GetValueOrDefault(unitKind);
            var living = CountLivingAssignedToBastion(bastion.Id, unitKind);
            _scratchRemainingDeficit[bastion.Id] = Math.Max(0, desired - living);
        }

        var attributed = 0;
        for (var factoryIndex = 0; factoryIndex < _scratchFactories.Count; factoryIndex++)
        {
            var factory = _scratchFactories[factoryIndex];
            if (!factory.IsAlive
                || factory.OwnerId != ownerId
                || factory.ProductionTargetKind != unitKind
                || factory.WorkTicksRemaining <= 0)
            {
                continue;
            }

            // Lowest bastion Id with remaining deficit (scratch bastions are Id-sorted).
            int? assignee = null;
            for (var bastionIndex = 0; bastionIndex < _scratchBastions.Count; bastionIndex++)
            {
                var candidateId = _scratchBastions[bastionIndex].Id;
                if (_scratchRemainingDeficit.TryGetValue(candidateId, out var remaining) && remaining > 0)
                {
                    assignee = candidateId;
                    break;
                }
            }

            if (assignee is null)
            {
                continue;
            }

            _scratchRemainingDeficit[assignee.Value]--;
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
        _spatialQueryIndex.InsertAlive(unit, GameplayTables);
        // Live mid-tick accounting: later factories in this tick must see the new living unit.
        InsertSortedById(_scratchUnits, unit);
        _armyAccountingAliveUnits++;
        BumpArmyAccountingEpoch();
        return true;
    }

    /// <summary>
    /// Lowest owned bastion Id that still needs <paramref name="unitKind"/> (living count vs template).
    /// Returns null when no bastion has a deficit (production should have waited at the factory).
    /// </summary>
    private int? ChooseSpawnBastionId(PlayerId ownerId, EntityKind unitKind)
    {
        for (var i = 0; i < _scratchBastions.Count; i++)
        {
            var bastion = _scratchBastions[i];
            if (!bastion.IsAlive || bastion.OwnerId != ownerId)
            {
                continue;
            }

            var desired = bastion.BastionTemplate.GetValueOrDefault(unitKind);
            if (desired <= 0)
            {
                continue;
            }

            var living = CountLivingAssignedToBastion(bastion.Id, unitKind);
            if (living < desired)
            {
                return bastion.Id;
            }
        }

        return null;
    }

    private bool TryFindSpawnTileNear(WorldEntity factory, EntityKind unitKind, out TilePosition spawnTile)
    {
        var footprint = GameplayTables.GetFootprint(factory.Kind);
        for (var ring = 1; ring <= 6; ring++)
        {
            _scratchSpawnCandidates.Clear();
            var minX = factory.Position.X - ring;
            var maxX = factory.Position.X + footprint.Width - 1 + ring;
            var minY = factory.Position.Y - ring;
            var maxY = factory.Position.Y + footprint.Height - 1 + ring;
            for (var x = minX; x <= maxX; x++)
            {
                _scratchSpawnCandidates.Add(new TilePosition(x, minY));
                _scratchSpawnCandidates.Add(new TilePosition(x, maxY));
            }

            for (var y = minY + 1; y <= maxY - 1; y++)
            {
                _scratchSpawnCandidates.Add(new TilePosition(minX, y));
                _scratchSpawnCandidates.Add(new TilePosition(maxX, y));
            }

            // Deterministic order: Manhattan(factory) then X then Y (same as previous LINQ).
            _scratchSpawnCandidates.Sort((left, right) =>
            {
                var distCmp = left.ManhattanDistance(factory.Position).CompareTo(right.ManhattanDistance(factory.Position));
                if (distCmp != 0)
                {
                    return distCmp;
                }

                var xCmp = left.X.CompareTo(right.X);
                return xCmp != 0 ? xCmp : left.Y.CompareTo(right.Y);
            });

            for (var i = 0; i < _scratchSpawnCandidates.Count; i++)
            {
                var tile = _scratchSpawnCandidates[i];
                if (!World.IsInside(tile))
                {
                    continue;
                }

                var probe = new WorldEntity(-1, unitKind, tile, factory.OwnerId);
                if (IsStopTilePassable(probe, tile)
                    && CanOccupyWorldPosition(probe, probe.WorldPosition, _spatialQueryIndex))
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
        CollectSortedAliveEntities(_scratchBastions, static entity => entity.Kind == EntityKind.Bastion);
        CollectSortedAliveEntities(_scratchUnits, static entity => MvpDefinitions.UnitKinds.Contains(entity.Kind));

        for (var bastionIndex = 0; bastionIndex < _scratchBastions.Count; bastionIndex++)
        {
            var bastion = _scratchBastions[bastionIndex];
            TryCompleteBastionOrder(bastion);

            CollectUnitsAssignedToBastion(bastion.Id);

            // Home units keep Defend while bastion is Scout/AttackArea; garrison them,
            // but sortie only during active bastion Defend.
            var allowSortie = bastion.Order.Kind == BastionOrderKind.Defend;
            var threat = allowSortie
                ? FindNearestEnemyInRange(bastion, GetBastionVisionRadius(bastion), _spatialQueryIndex)
                : null;

            for (var unitIndex = 0; unitIndex < _scratchBastionUnits.Count; unitIndex++)
            {
                var unit = _scratchBastionUnits[unitIndex];
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

    private void CollectUnitsAssignedToBastion(int bastionId)
    {
        _scratchBastionUnits.Clear();
        for (var i = 0; i < _scratchUnits.Count; i++)
        {
            var entity = _scratchUnits[i];
            if (entity.IsAlive && entity.AssignedBastionId == bastionId)
            {
                _scratchBastionUnits.Add(entity);
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
    private bool IsWithinBastionGarrisonRange(WorldEntity bastion, TilePosition unitPosition)
    {
        var footprint = GameplayTables.GetFootprint(bastion.Kind);
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
            CollectUnitsAssignedToBastion(bastion.Id);
            var attackCount = 0;
            var allArrived = true;
            for (var i = 0; i < _scratchBastionUnits.Count; i++)
            {
                var entity = _scratchBastionUnits[i];
                if (entity.Order.Kind != BastionOrderKind.AttackArea)
                {
                    continue;
                }

                attackCount++;
                if (!entity.Position.IsWithinEuclideanRange(bastion.Order.Target.Value, 1))
                {
                    allArrived = false;
                }
            }

            if (attackCount > 0 && allArrived && !AssignedAttackUnitsHaveEnemyInRange())
            {
                SwitchBastionToDefend(bastion);
            }

            return;
        }

        if (bastion.Order.Kind == BastionOrderKind.Scout && bastion.Order.Target is not null)
        {
            CollectUnitsAssignedToBastion(bastion.Id);
            var scoutCount = 0;
            var allArrived = true;
            for (var i = 0; i < _scratchBastionUnits.Count; i++)
            {
                var entity = _scratchBastionUnits[i];
                if (entity.Kind != EntityKind.Scout)
                {
                    continue;
                }

                scoutCount++;
                if (!entity.Position.IsWithinEuclideanRange(bastion.Order.Target.Value, 1))
                {
                    allArrived = false;
                }
            }

            if (scoutCount > 0 && allArrived)
            {
                SwitchBastionToDefend(bastion);
            }
        }
    }

    private bool AssignedAttackUnitsHaveEnemyInRange()
    {
        var spatial = _spatialQueryIndex;
        for (var i = 0; i < _scratchBastionUnits.Count; i++)
        {
            var unit = _scratchBastionUnits[i];
            if (unit.Order.Kind != BastionOrderKind.AttackArea)
            {
                continue;
            }

            var stats = GameplayTables.GetStats(unit.Kind);
            if (stats.AttackDamage <= 0 || stats.AttackRange <= 0)
            {
                continue;
            }

            if (FindNearestEnemyInRange(unit, stats.AttackRange, spatial) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private void SwitchBastionToDefend(WorldEntity bastion)
    {
        var defend = new BastionOrder(BastionOrderKind.Defend);
        bastion.Order = defend;
        CollectUnitsAssignedToBastion(bastion.Id);
        for (var i = 0; i < _scratchBastionUnits.Count; i++)
        {
            var unit = _scratchBastionUnits[i];
            unit.Order = defend;
            ResetMovementPath(unit);
        }
    }

    private int GetBastionVisionRadius(WorldEntity bastion)
    {
        var radius = GameplayTables.GetStats(EntityKind.Bastion).VisionRadius;
        if (bastion.OwnerId is not null)
        {
            radius = ResolveStat(bastion.OwnerId.Value, ResearchStatIds.VisionRadius, radius, minValue: 0);
        }

        return radius;
    }

    private WorldEntity? FindNearestEnemyInRange(WorldEntity origin, int radius, SpatialQueryIndex spatial)
    {
        if (origin.OwnerId is null)
        {
            return null;
        }

        WorldEntity? best = null;
        var bestDistanceSquared = 0;
        foreach (var entity in spatial.QueryByPositionInEuclideanRange(origin.Position, radius))
        {
            if (!entity.IsAlive
                || entity.IsGarrisoned
                || entity.OwnerId is null
                || AreAllied(origin.OwnerId, entity.OwnerId))
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
