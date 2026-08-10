namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Combat targeting/damage, bastion-death cascade, and out-of-combat repair for one tick.
    /// </summary>
    private static class CombatSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.ProcessCombat();
            sim.CascadeBastionDeaths();
            sim.ProcessRepairOutOfCombat();
        }
    }

    private int ResolveAttackDamage(WorldEntity attacker, EntityStats baseline)
    {
        if (attacker.OwnerId is null)
        {
            return baseline.AttackDamage;
        }

        return ResolveStat(
            attacker.OwnerId.Value,
            ResearchStatIds.AttackDamage,
            baseline.AttackDamage,
            attacker.Kind.ToString(),
            minValue: 0);
    }

    private int ResolveArmor(WorldEntity target, EntityStats baseline)
    {
        if (target.OwnerId is null)
        {
            return baseline.Armor;
        }

        return ResolveStat(
            target.OwnerId.Value,
            ResearchStatIds.Armor,
            baseline.Armor,
            target.Kind.ToString(),
            minValue: 0);
    }

    private int ResolveAttackCooldown(WorldEntity attacker, EntityStats baseline)
    {
        if (attacker.OwnerId is null)
        {
            return baseline.AttackCooldownTicks;
        }

        return ResolveStat(
            attacker.OwnerId.Value,
            ResearchStatIds.AttackCooldownTicks,
            baseline.AttackCooldownTicks,
            attacker.Kind.ToString(),
            minValue: 1);
    }

    private int ComputeDamageAgainst(WorldEntity attacker, EntityStats attackerStats, WorldEntity target)
    {
        SyncResolvedMaxHealth(target);
        var attackDamage = ResolveAttackDamage(attacker, attackerStats);
        var targetStats = MvpDefinitions.GetStats(target.Kind);
        var armor = ResolveArmor(target, targetStats);
        var resistance = CombatDamage.GetResistanceBasisPoints(
            attackerStats.ProjectileKind,
            MvpDefinitions.GetCombatTargetCategory(target.Kind));
        return CombatDamage.ComputeFinalDamage(attackDamage, armor, resistance);
    }

    /// <summary>
    /// True when GroundToGround fire at a ground unit is blocked by an allied Wall/SteelWall on the LoS ray.
    /// Ballistic and AirToGround ignore walls. Buildings/walls as targets are never covered.
    /// Allied means same TeamId (static map-config alliances).
    /// </summary>
    private bool IsGroundToGroundBlockedByAlliedWall(
        WorldEntity attacker,
        WorldEntity target,
        ProjectileKind projectileKind,
        CombatSpatialIndex spatial)
    {
        if (projectileKind != ProjectileKind.GroundToGround)
        {
            return false;
        }

        if (!MvpDefinitions.IsGroundUnitForWallCover(target.Kind) || target.OwnerId is null)
        {
            return false;
        }

        foreach (var tile in EnumerateLineExclusive(attacker.Position, target.Position))
        {
            if (!World.IsInside(tile))
            {
                continue;
            }

            if (spatial.HasAlliedWallAt(tile, target.OwnerId.Value, AreAllied))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bresenham tiles strictly between <paramref name="from"/> and <paramref name="to"/> (endpoints excluded).
    /// </summary>
    internal static IEnumerable<TilePosition> EnumerateLineExclusive(TilePosition from, TilePosition to)
    {
        var x0 = from.X;
        var y0 = from.Y;
        var x1 = to.X;
        var y1 = to.Y;
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }

            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }

            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }

            yield return new TilePosition(x0, y0);
        }
    }

    private void ApplyCombatDamage(WorldEntity target, int damage)
    {
        if (damage <= 0)
        {
            return;
        }

        target.Health = Math.Max(0, target.Health - damage);
    }

    private void ProcessRepairOutOfCombat()
    {
        if (Tick % 30 != 0)
        {
            return;
        }

        foreach (var player in _players.OrderBy(player => player.Id.Value))
        {
            if (!CapabilityResolver.HasCapability(player.Research, ResearchCapabilityIds.RepairOutOfCombat))
            {
                continue;
            }

            foreach (var bastion in World.Entities
                .Where(entity => entity.IsAlive && entity.OwnerId == player.Id && entity.Kind == EntityKind.Bastion)
                .OrderBy(entity => entity.Id))
            {
                foreach (var unit in World.Entities
                    .Where(entity =>
                        entity.IsAlive
                        && entity.OwnerId == player.Id
                        && MvpDefinitions.UnitKinds.Contains(entity.Kind)
                        && entity.Health < entity.MaxHealth
                        && entity.AttackCooldownRemaining <= 0
                        && entity.Position.ManhattanDistance(bastion.Position) <= 4)
                    .OrderBy(entity => entity.Id))
                {
                    unit.Health = Math.Min(unit.MaxHealth, unit.Health + 1);
                }
            }
        }
    }

    private void ProcessCombat()
    {
        _presentation.ClearCombatShots();
        // Per-pass combat index keeps range/splash queries neighborhood-limited; GameWorld tile
        // occupancy (#66) does not replace position-radius combat scans yet.
        var spatial = CombatSpatialIndex.Build(World.Entities);
        CollectSortedAliveEntities(
            _scratchEntities,
            static entity => !entity.IsGarrisoned
                             && entity.OwnerId is not null
                             && MvpDefinitions.GetStats(entity.Kind).AttackDamage > 0);

        for (var attackerIndex = 0; attackerIndex < _scratchEntities.Count; attackerIndex++)
        {
            var attacker = _scratchEntities[attackerIndex];
            // Cascade may have killed this attacker earlier in the same pass.
            if (!attacker.IsAlive || attacker.IsGarrisoned)
            {
                continue;
            }

            if (attacker.AttackCooldownRemaining > 0)
            {
                attacker.AttackCooldownRemaining--;
                continue;
            }

            var stats = MvpDefinitions.GetStats(attacker.Kind);
            var attackRange = stats.AttackRange;
            var target = FindNearestCombatTarget(attacker, attackRange, spatial);
            if (target is null)
            {
                continue;
            }

            if (MvpDefinitions.GetPowerDemand(attacker.Kind) > 0 && !TryConsumeBuildingEnergy(attacker))
            {
                continue;
            }

            attacker.AttackCooldownRemaining = ResolveAttackCooldown(attacker, stats);

            if (IsGroundToGroundBlockedByAlliedWall(attacker, target, stats.ProjectileKind, spatial))
            {
                continue;
            }

            _presentation.AddCombatShot(new CombatShotEvent(
                attacker.Id,
                target.Id,
                attacker.WorldPosition,
                target.WorldPosition,
                stats.ProjectileKind));

            var primaryDamage = ComputeDamageAgainst(attacker, stats, target);
            ApplyCombatDamage(target, primaryDamage);
            var killed = !target.IsAlive;

            if (stats.SplashRadius > 0)
            {
                _scratchEntitiesSecondary.Clear();
                foreach (var entity in spatial.QueryByPositionInEuclideanRange(target.Position, stats.SplashRadius))
                {
                    if (!entity.IsAlive
                        || entity.IsGarrisoned
                        || entity.Id == target.Id
                        || entity.OwnerId is null
                        || AreAllied(attacker.OwnerId, entity.OwnerId))
                    {
                        continue;
                    }

                    _scratchEntitiesSecondary.Add(entity);
                }

                _scratchEntitiesSecondary.Sort(static (left, right) => left.Id.CompareTo(right.Id));

                for (var splashIndex = 0; splashIndex < _scratchEntitiesSecondary.Count; splashIndex++)
                {
                    var splashTarget = _scratchEntitiesSecondary[splashIndex];
                    var splashDamage = ComputeDamageAgainst(attacker, stats, splashTarget);
                    ApplyCombatDamage(splashTarget, splashDamage);
                    if (!splashTarget.IsAlive)
                    {
                        killed = true;
                    }
                }
            }

            if (killed)
            {
                CascadeBastionDeaths();
            }
        }
    }

    private WorldEntity? FindNearestCombatTarget(
        WorldEntity attacker,
        int attackRange,
        CombatSpatialIndex spatial)
    {
        WorldEntity? best = null;
        var bestDistanceSquared = 0;
        foreach (var entity in spatial.QueryByPositionInEuclideanRange(attacker.Position, attackRange))
        {
            if (!entity.IsAlive
                || entity.IsGarrisoned
                || entity.OwnerId is null
                || AreAllied(attacker.OwnerId, entity.OwnerId))
            {
                continue;
            }

            var distanceSquared = attacker.Position.EuclideanDistanceSquared(entity.Position);
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

    private void CollectSortedAliveEntities(List<WorldEntity> into, Func<WorldEntity, bool> predicate)
    {
        into.Clear();
        var entities = World.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (entity.IsAlive && predicate(entity))
            {
                into.Add(entity);
            }
        }

        into.Sort(static (left, right) => left.Id.CompareTo(right.Id));
    }
}
