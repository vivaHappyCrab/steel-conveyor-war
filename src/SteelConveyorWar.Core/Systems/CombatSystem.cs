namespace SteelConveyorWar.Core;

/// <summary>
/// R06: standalone combat system extracted from the <see cref="GameSimulation"/> god-object. Handles
/// targeting/damage, the bastion-death cascade, and out-of-combat repair for one tick. Owns its scratch
/// buffers and reaches the rest of the simulation only through <see cref="ISimulationSystemContext"/>,
/// so it can be unit-tested in isolation.
/// </summary>
internal sealed class CombatSystem
{
    private readonly ISimulationSystemContext _context;

    // Reused across ticks to avoid LINQ/ToList allocations on the hot combat path.
    private readonly List<WorldEntity> _scratchEntities = new();
    private readonly List<WorldEntity> _scratchEntitiesSecondary = new();
    private readonly List<PlayerState> _scratchPlayers = new();

    public CombatSystem(ISimulationSystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public void Tick()
    {
        ProcessCombat();
        _context.CascadeBastionDeaths();
        ProcessRepairOutOfCombat();
    }

    private int ResolveAttackDamage(WorldEntity attacker, EntityStats baseline)
    {
        if (attacker.OwnerId is null)
        {
            return baseline.AttackDamage;
        }

        return _context.ResolveStat(
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

        return _context.ResolveStat(
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

        return _context.ResolveStat(
            attacker.OwnerId.Value,
            ResearchStatIds.AttackCooldownTicks,
            baseline.AttackCooldownTicks,
            attacker.Kind.ToString(),
            minValue: 1);
    }

    private int ComputeDamageAgainst(WorldEntity attacker, EntityStats attackerStats, WorldEntity target)
    {
        _context.SyncResolvedMaxHealth(target);
        var attackDamage = ResolveAttackDamage(attacker, attackerStats);
        var targetStats = _context.GameplayTables.GetStats(target.Kind);
        var armor = ResolveArmor(target, targetStats);
        var resistance = CombatDamage.GetResistanceBasisPoints(
            attackerStats.ProjectileKind,
            MvpDefinitions.GetCombatTargetCategory(target.Kind),
            _context.GameplayTables);
        return CombatDamage.ComputeFinalDamage(attackDamage, armor, resistance);
    }

    /// <summary>
    /// R06: test/helper bridge. Combat damage computation moved here from <see cref="GameSimulation"/>
    /// during the god-object split; this exposes the same formula-C result to the simulation's
    /// test helper without widening the private method's visibility beyond the assembly.
    /// </summary>
    internal int ComputeDamageForTests(WorldEntity attacker, EntityStats attackerStats, WorldEntity target)
        => ComputeDamageAgainst(attacker, attackerStats, target);

    /// <summary>
    /// True when GroundToGround fire at a ground unit is blocked by an allied Wall/SteelWall on the LoS ray.
    /// Ballistic and AirToGround ignore walls. Buildings/walls as targets are never covered.
    /// Allied means same TeamId (static map-config alliances).
    /// </summary>
    private bool IsGroundToGroundBlockedByAlliedWall(
        WorldEntity attacker,
        WorldEntity target,
        ProjectileKind projectileKind,
        SpatialQueryIndex spatial)
    {
        if (projectileKind != ProjectileKind.GroundToGround)
        {
            return false;
        }

        if (!MvpDefinitions.IsGroundUnitForWallCover(
                target.Kind,
                _context.GameplayTables.GetStats(target.Kind).MovementType)
            || target.OwnerId is null)
        {
            return false;
        }

        foreach (var tile in EnumerateLineExclusive(attacker.Position, target.Position))
        {
            if (!_context.World.IsInside(tile))
            {
                continue;
            }

            if (spatial.HasAlliedWallAt(tile, target.OwnerId.Value, _context.AreAllied))
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

    private static void ApplyCombatDamage(WorldEntity target, int damage)
    {
        if (damage <= 0)
        {
            return;
        }

        target.Health = Math.Max(0, target.Health - damage);
    }

    private void ProcessRepairOutOfCombat()
    {
        if (_context.Tick % 30 != 0)
        {
            return;
        }

        _context.CollectSortedAliveEntities(
            _scratchEntities,
            static entity => entity.Kind == EntityKind.Bastion);
        _context.CollectSortedAliveEntities(
            _scratchEntitiesSecondary,
            static entity =>
                MvpDefinitions.UnitKinds.Contains(entity.Kind)
                && entity.Health < entity.MaxHealth
                && entity.AttackCooldownRemaining <= 0);

        _scratchPlayers.Clear();
        for (var i = 0; i < _context.Players.Count; i++)
        {
            _scratchPlayers.Add(_context.Players[i]);
        }

        _scratchPlayers.Sort(static (left, right) => left.Id.Value.CompareTo(right.Id.Value));

        for (var playerIndex = 0; playerIndex < _scratchPlayers.Count; playerIndex++)
        {
            var player = _scratchPlayers[playerIndex];
            if (!CapabilityResolver.HasCapability(player.Research, ResearchCapabilityIds.RepairOutOfCombat))
            {
                continue;
            }

            for (var bastionIndex = 0; bastionIndex < _scratchEntities.Count; bastionIndex++)
            {
                var bastion = _scratchEntities[bastionIndex];
                if (!bastion.IsAlive || bastion.OwnerId != player.Id)
                {
                    continue;
                }

                for (var unitIndex = 0; unitIndex < _scratchEntitiesSecondary.Count; unitIndex++)
                {
                    var unit = _scratchEntitiesSecondary[unitIndex];
                    if (!unit.IsAlive
                        || unit.OwnerId != player.Id
                        || unit.Position.ManhattanDistance(bastion.Position) > 4)
                    {
                        continue;
                    }

                    unit.Health = Math.Min(unit.MaxHealth, unit.Health + 1);
                }
            }
        }
    }

    private void ProcessCombat()
    {
        _context.Presentation.ClearCombatShots();
        // M06: share the post-factory spatial index (movement Relocate keeps it current). Second
        // full rebuild would be redundant when entity set is unchanged after factory.
        var spatial = _context.SharedSpatialIndex;
        var tables = _context.GameplayTables;
        _context.CollectSortedAliveEntities(
            _scratchEntities,
            entity => !entity.IsGarrisoned
                             && entity.OwnerId is not null
                             && tables.GetStats(entity.Kind).AttackDamage > 0);

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

            var stats = tables.GetStats(attacker.Kind);
            if (!MvpDefinitions.CanFireWhileMoving(attacker.Kind, stats.MovementType) && attacker.IsActivelyMoving)
            {
                continue;
            }

            var attackRange = stats.AttackRange;
            var target = FindNearestCombatTarget(attacker, attackRange, spatial);
            if (target is null)
            {
                continue;
            }

            if (tables.GetPowerDemand(attacker.Kind) > 0 && !_context.TryConsumeBuildingEnergy(attacker))
            {
                continue;
            }

            attacker.AttackCooldownRemaining = ResolveAttackCooldown(attacker, stats);

            if (IsGroundToGroundBlockedByAlliedWall(attacker, target, stats.ProjectileKind, spatial))
            {
                continue;
            }

            _context.Presentation.AddCombatShot(new CombatShotEvent(
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
                        || _context.AreAllied(attacker.OwnerId, entity.OwnerId))
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
                _context.CascadeBastionDeaths();
            }
        }
    }

    private WorldEntity? FindNearestCombatTarget(
        WorldEntity attacker,
        int attackRange,
        SpatialQueryIndex spatial)
    {
        WorldEntity? best = null;
        var bestDistanceSquared = 0;
        foreach (var entity in spatial.QueryByPositionInEuclideanRange(attacker.Position, attackRange))
        {
            if (!entity.IsAlive
                || entity.IsGarrisoned
                || entity.OwnerId is null
                || _context.AreAllied(attacker.OwnerId, entity.OwnerId))
            {
                continue;
            }

            if (attacker.Kind == EntityKind.Commander
                && _context.GameplayTables.GetStats(entity.Kind).MovementType == MovementType.Flying)
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
}
