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
    private bool IsGroundToGroundBlockedByAlliedWall(WorldEntity attacker, WorldEntity target, ProjectileKind projectileKind)
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

            if (World.GetEntitiesAt(tile).Any(entity =>
                    entity.IsAlive
                    && MvpDefinitions.IsWallKind(entity.Kind)
                    && AreAllied(entity.OwnerId, target.OwnerId)))
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
        _combatShotsThisTick.Clear();
        foreach (var attacker in World.Entities.Where(entity =>
                     entity.IsAlive
                     && !entity.IsGarrisoned
                     && entity.OwnerId is not null
                     && MvpDefinitions.GetStats(entity.Kind).AttackDamage > 0).OrderBy(entity => entity.Id).ToList())
        {
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
            var target = World.Entities
                .Where(entity =>
                    entity.IsAlive
                    && !entity.IsGarrisoned
                    && entity.OwnerId is not null
                    && !AreAllied(attacker.OwnerId, entity.OwnerId))
                .Where(entity => attacker.Position.IsWithinEuclideanRange(entity.Position, attackRange))
                .OrderBy(entity => attacker.Position.EuclideanDistanceSquared(entity.Position))
                .ThenBy(entity => entity.Id)
                .FirstOrDefault();
            if (target is null)
            {
                continue;
            }

            if (MvpDefinitions.GetPowerDemand(attacker.Kind) > 0 && !TryConsumeBuildingEnergy(attacker))
            {
                continue;
            }

            attacker.AttackCooldownRemaining = ResolveAttackCooldown(attacker, stats);

            if (IsGroundToGroundBlockedByAlliedWall(attacker, target, stats.ProjectileKind))
            {
                continue;
            }

            _combatShotsThisTick.Add(new CombatShotEvent(
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
                foreach (var splashTarget in World.Entities
                             .Where(entity =>
                                 entity.IsAlive
                                 && !entity.IsGarrisoned
                                 && entity.Id != target.Id
                                 && entity.OwnerId is not null
                                 && !AreAllied(attacker.OwnerId, entity.OwnerId)
                                 && target.Position.IsWithinEuclideanRange(entity.Position, stats.SplashRadius))
                             .OrderBy(entity => entity.Id)
                             .ToList())
                {
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
}
