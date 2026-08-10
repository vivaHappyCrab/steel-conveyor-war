namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Unit pathfinding and continuous movement toward order targets.
    /// </summary>
    private static class MovementSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.ProcessMovement();
        }
    }

    private void ProcessMovement()
    {
        CollectSortedAliveEntities(_scratchEntities, static entity => MvpDefinitions.UnitKinds.Contains(entity.Kind));
        for (var i = 0; i < _scratchEntities.Count; i++)
        {
            var unit = _scratchEntities[i];
            var target = GetMovementTarget(unit);
            if (target is null || unit.Position == target.Value)
            {
                continue;
            }

            unit.IsGarrisoned = false;
            MoveMobileEntityTowardTile(unit, target.Value);
        }
    }

    private TilePosition? GetMovementTarget(WorldEntity unit)
    {
        if (unit.IsGarrisoned)
        {
            return null;
        }

        if ((unit.Order.Kind is BastionOrderKind.AttackArea or BastionOrderKind.Scout) && unit.Order.Target is not null)
        {
            return unit.Order.Target;
        }

        if (unit.Order.Kind == BastionOrderKind.Patrol && unit.Order.WaypointList.Count >= 2)
        {
            var waypoints = unit.Order.WaypointList;
            var index = ((unit.Order.WaypointIndex % waypoints.Count) + waypoints.Count) % waypoints.Count;
            var waypoint = waypoints[index];
            if (unit.Position.IsWithinEuclideanRange(waypoint, 0))
            {
                var nextIndex = (index + 1) % waypoints.Count;
                unit.Order = unit.Order with { WaypointIndex = nextIndex };
                return waypoints[nextIndex];
            }

            return waypoint;
        }

        if (unit.Order.Kind == BastionOrderKind.Defend && unit.AssignedBastionId is not null)
        {
            var bastion = World.GetEntity(unit.AssignedBastionId.Value);
            if (bastion is null)
            {
                return null;
            }

            // Sortie only for active bastion Defend; Scout/AttackArea home units stay put.
            if (bastion.Order.Kind == BastionOrderKind.Defend)
            {
                var visionRadius = GetBastionVisionRadius(bastion);
                var threat = FindNearestEnemyInRange(bastion, visionRadius);
                if (threat is not null)
                {
                    return threat.Position;
                }
            }

            return bastion.Position;
        }

        if (unit.AssignedBastionId is not null)
        {
            return World.GetEntity(unit.AssignedBastionId.Value)?.Position;
        }

        return null;
    }

    private bool MoveMobileEntityTowardTile(WorldEntity entity, TilePosition target)
    {
        if (!World.IsInside(target))
        {
            ResetMovementPath(entity);
            return false;
        }

        if (entity.Position == target && entity.CurrentWaypoint is null && entity.MovementPath.Count == 0)
        {
            entity.WorldPosition = WorldPosition.FromTileCenter(target);
            return true;
        }

        if (entity.CurrentWaypoint is null)
        {
            if (entity.MovementPath.Count == 0)
            {
                var path = FindGroundPath(entity, entity.Position, target);
                if (path.Count == 0)
                {
                    return !IsGroundPassable(entity, target);
                }

                entity.MovementPathMutable.AddRange(path);
            }

            entity.CurrentWaypoint = entity.MovementPath[0];
            entity.MovementPathMutable.RemoveAt(0);
        }

        var waypointPosition = WorldPosition.FromTileCenter(entity.CurrentWaypoint.Value);
        var distance = entity.WorldPosition.DistanceTo(waypointPosition);
        if (distance <= MvpDefinitions.MobileMoveWorldUnitsPerTick)
        {
            if (!CanOccupyWorldPosition(entity, waypointPosition))
            {
                ResetMovementPath(entity);
                return false;
            }

            entity.WorldPosition = waypointPosition;
            World.RelocateEntity(entity, entity.CurrentWaypoint.Value);
            entity.CurrentWaypoint = null;
            return entity.MovementPath.Count == 0 && (entity.Position == target || !IsGroundPassable(entity, target));
        }

        var dx = waypointPosition.X - entity.WorldPosition.X;
        var dy = waypointPosition.Y - entity.WorldPosition.Y;
        var nextPosition = new WorldPosition(
            entity.WorldPosition.X + dx / distance * MvpDefinitions.MobileMoveWorldUnitsPerTick,
            entity.WorldPosition.Y + dy / distance * MvpDefinitions.MobileMoveWorldUnitsPerTick);
        if (!CanOccupyWorldPosition(entity, nextPosition))
        {
            ResetMovementPath(entity);
            return false;
        }

        entity.WorldPosition = nextPosition;
        World.RelocateEntity(entity, nextPosition.ToTilePosition());
        return false;
    }

    private List<TilePosition> FindGroundPath(WorldEntity entity, TilePosition start, TilePosition target)
    {
        foreach (var pathTarget in GetCandidatePathTargets(entity, start, target))
        {
            var path = FindGroundPathToTarget(entity, start, pathTarget);
            if (path.Count > 0 || start == pathTarget)
            {
                return path;
            }
        }

        return [];
    }

    private List<TilePosition> FindGroundPathToTarget(WorldEntity entity, TilePosition start, TilePosition target)
    {
        if (start == target)
        {
            return [];
        }

        var open = new PriorityQueue<TilePosition, (int F, int H, int Y, int X)>();
        var previous = new Dictionary<TilePosition, TilePosition?>();
        var costSoFar = new Dictionary<TilePosition, int>();
        previous[start] = null;
        costSoFar[start] = 0;
        open.Enqueue(start, (OctileDistance(start, target), OctileDistance(start, target), start.Y, start.X));

        while (open.TryDequeue(out var current, out _))
        {
            if (current == target)
            {
                return ReconstructPath(previous, target);
            }

            foreach (var next in GetNeighborTiles(current))
            {
                if (!CanEnterNeighbor(entity, current, next, start))
                {
                    continue;
                }

                var newCost = costSoFar[current] + GetStepCost(current, next);
                if (costSoFar.TryGetValue(next, out var existingCost) && newCost >= existingCost)
                {
                    continue;
                }

                costSoFar[next] = newCost;
                previous[next] = current;
                var heuristic = OctileDistance(next, target);
                open.Enqueue(next, (newCost + heuristic, heuristic, next.Y, next.X));
            }
        }

        return [];
    }

    private IEnumerable<TilePosition> GetCandidatePathTargets(WorldEntity entity, TilePosition start, TilePosition target)
    {
        if (World.IsInside(target) && IsGroundPassable(entity, target))
        {
            yield return target;
            yield break;
        }

        var maxRadius = Math.Max(World.Size.Width, World.Size.Height);
        for (var radius = 1; radius <= maxRadius; radius++)
        {
            var candidates = new List<TilePosition>();
            for (var y = target.Y - radius; y <= target.Y + radius; y++)
            {
                for (var x = target.X - radius; x <= target.X + radius; x++)
                {
                    if (Math.Max(Math.Abs(target.X - x), Math.Abs(target.Y - y)) != radius)
                    {
                        continue;
                    }

                    var candidate = new TilePosition(x, y);
                    if (World.IsInside(candidate) && IsGroundPassable(entity, candidate))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            foreach (var candidate in candidates
                .OrderBy(candidate => OctileDistance(start, candidate))
                .ThenBy(candidate => candidate.ManhattanDistance(target))
                .ThenBy(candidate => candidate.Y)
                .ThenBy(candidate => candidate.X))
            {
                yield return candidate;
            }
        }
    }

    private static List<TilePosition> ReconstructPath(Dictionary<TilePosition, TilePosition?> previous, TilePosition target)
    {
        var path = new List<TilePosition>();
        var current = target;
        while (previous[current] is not null)
        {
            path.Add(current);
            current = previous[current]!.Value;
        }

        path.Reverse();
        return path;
    }

    private static IEnumerable<TilePosition> GetNeighborTiles(TilePosition current)
    {
        yield return current.Offset(Direction.North);
        yield return current.Offset(Direction.East);
        yield return current.Offset(Direction.South);
        yield return current.Offset(Direction.West);
        yield return new TilePosition(current.X + 1, current.Y - 1);
        yield return new TilePosition(current.X + 1, current.Y + 1);
        yield return new TilePosition(current.X - 1, current.Y + 1);
        yield return new TilePosition(current.X - 1, current.Y - 1);
    }

    private bool CanEnterNeighbor(WorldEntity mover, TilePosition current, TilePosition next, TilePosition start)
    {
        if (!World.IsInside(next) || (next != start && !IsGroundPassable(mover, next)))
        {
            return false;
        }

        if (!IsDiagonalStep(current, next))
        {
            return true;
        }

        var horizontal = new TilePosition(next.X, current.Y);
        var vertical = new TilePosition(current.X, next.Y);
        return (horizontal == start || IsGroundPassable(mover, horizontal))
            && (vertical == start || IsGroundPassable(mover, vertical));
    }

    private static bool IsDiagonalStep(TilePosition from, TilePosition to)
    {
        return from.X != to.X && from.Y != to.Y;
    }

    /// <summary>Integer A* step cost (straight=10, diagonal=14 ≈ 10√2). See ADR 0001.</summary>
    private const int PathCostStraight = 10;
    private const int PathCostDiagonal = 14;

    private static int GetStepCost(TilePosition from, TilePosition to)
    {
        return IsDiagonalStep(from, to) ? PathCostDiagonal : PathCostStraight;
    }

    private static int OctileDistance(TilePosition from, TilePosition to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);
        var diagonal = Math.Min(dx, dy);
        var straight = Math.Max(dx, dy) - diagonal;
        return diagonal * PathCostDiagonal + straight * PathCostStraight;
    }

    private bool IsGroundPassable(WorldEntity mover, TilePosition tile)
    {
        return World.IsInside(tile) && !World.GetEntitiesAt(tile)
            .Any(entity => entity.Id != mover.Id && entity.IsAlive && MvpDefinitions.BlocksGroundMovement(entity.Kind));
    }

    private bool CanOccupyWorldPosition(WorldEntity mover, WorldPosition position)
    {
        if (!World.IsInside(position.ToTilePosition()))
        {
            return false;
        }

        var radius = MvpDefinitions.GetCollisionSize(mover.Kind).Radius;
        if (radius <= 0)
        {
            return true;
        }

        foreach (var entity in World.Entities)
        {
            if (entity.Id == mover.Id || !entity.IsAlive || entity.IsGarrisoned)
            {
                continue;
            }

            if (MvpDefinitions.BlocksGroundMovement(entity.Kind))
            {
                if (CircleIntersectsEntityFootprint(position, radius, entity)
                    && !MovesOutOfExistingOverlap(mover, position, radius, entity))
                {
                    return false;
                }

                continue;
            }

            // Moving units ignore unit↔unit collision (radius effectively 0); stopped units keep full size.
            if (IsMobileEntityMoving(mover) || IsMobileEntityMoving(entity))
            {
                continue;
            }

            var otherRadius = MvpDefinitions.GetCollisionSize(entity.Kind).Radius;
            if (otherRadius <= 0)
            {
                continue;
            }

            var minDistance = radius + otherRadius;
            var minDistanceSq = minDistance * minDistance;
            var nextDistanceSq = position.DistanceSquaredTo(entity.WorldPosition);
            if (nextDistanceSq >= minDistanceSq)
            {
                continue;
            }

            var currentDistanceSq = mover.WorldPosition.DistanceSquaredTo(entity.WorldPosition);
            if (currentDistanceSq < minDistanceSq && nextDistanceSq > currentDistanceSq)
            {
                // Allow sliding out of an existing overlap.
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// True when a mobile entity has active movement (waypoint, path, or move target) and is not garrisoned.
    /// </summary>
    private static bool IsMobileEntityMoving(WorldEntity entity)
    {
        if (entity.IsGarrisoned)
        {
            return false;
        }

        if (entity.CurrentWaypoint is not null || entity.MovementPath.Count > 0)
        {
            return true;
        }

        return entity.MoveTarget is not null && entity.MoveTarget.Value != entity.Position;
    }

    private static bool CircleIntersectsEntityFootprint(WorldPosition position, double radius, WorldEntity obstacle)
    {
        return DistanceSquaredToEntityFootprint(position, obstacle) < radius * radius;
    }

    private static bool MovesOutOfExistingOverlap(WorldEntity mover, WorldPosition nextPosition, double radius, WorldEntity obstacle)
    {
        var currentDistanceSq = DistanceSquaredToEntityFootprint(mover.WorldPosition, obstacle);
        var radiusSq = radius * radius;
        if (currentDistanceSq >= radiusSq)
        {
            return false;
        }

        return DistanceSquaredToEntityFootprint(nextPosition, obstacle) > currentDistanceSq;
    }

    private static double DistanceSquaredToEntityFootprint(WorldPosition position, WorldEntity obstacle)
    {
        var footprintKind = obstacle.Kind == EntityKind.GhostBuild && obstacle.BuildTargetKind is not null
            ? obstacle.BuildTargetKind.Value
            : obstacle.Kind;
        var footprint = MvpDefinitions.GetFootprint(footprintKind);
        var closestX = Math.Clamp(position.X, obstacle.Position.X, obstacle.Position.X + footprint.Width);
        var closestY = Math.Clamp(position.Y, obstacle.Position.Y, obstacle.Position.Y + footprint.Height);
        var dx = position.X - closestX;
        var dy = position.Y - closestY;
        return dx * dx + dy * dy;
    }

    private static void ResetMovementPath(WorldEntity entity)
    {
        entity.MovementPathMutable.Clear();
        entity.CurrentWaypoint = null;
    }
}
