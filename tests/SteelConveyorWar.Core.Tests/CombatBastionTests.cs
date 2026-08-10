namespace SteelConveyorWar.Core.Tests;

public sealed class CombatBastionTests
{
    [Fact]
    public void TryIssueBastionOrder_AttackArea_PropagatesToAssignedUnitAndMoves()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var start = tank.Position;
        var target = new TilePosition(start.X + 3, start.Y);

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.AttackArea, target)));
        Assert.Equal(BastionOrderKind.AttackArea, bastion.Order.Kind);
        Assert.Equal(target, bastion.Order.Target);
        Assert.Equal(BastionOrderKind.AttackArea, tank.Order.Kind);
        Assert.Equal(target, tank.Order.Target);
        Assert.False(tank.IsGarrisoned);

        AdvanceTicks(simulation, 5);
        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.Equal(BastionOrderKind.AttackArea, bastion.Order.Kind);
        Assert.Equal(BastionOrderKind.AttackArea, tank.Order.Kind);
        Assert.True(
            tank.Position != start
            || tank.CurrentWaypoint is not null
            || tank.MovementPath.Count > 0);
    }

    [Fact]
    public void TryIssueBastionOrder_AttackArea_CompletesToDefendWhenCombatUnitsReachTarget()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var target = new TilePosition(tank.Position.X + 1, tank.Position.Y);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.AttackArea, target)));

        AdvanceTicks(simulation, 80);

        Assert.Equal(BastionOrderKind.Defend, bastion.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, simulation.World.GetEntity(tank.Id)!.Order.Kind);
    }

    [Fact]
    public void TryIssueBastionOrder_Scout_OnlyAssignsScoutsAndCompletesToDefend()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        var target = new TilePosition(scout.Position.X + 1, scout.Position.Y);

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Scout, target)));
        Assert.Equal(BastionOrderKind.Scout, bastion.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, tank.Order.Kind);
        Assert.Equal(BastionOrderKind.Scout, scout.Order.Kind);
        Assert.Equal(target, scout.Order.Target);

        AdvanceTicks(simulation, 80);

        Assert.Equal(BastionOrderKind.Defend, bastion.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, simulation.World.GetEntity(scout.Id)!.Order.Kind);
    }

    [Fact]
    public void TryIssueBastionOrder_AttackArea_AssignsScoutsSameTarget()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        var target = new TilePosition(tank.Position.X + 2, tank.Position.Y);

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.AttackArea, target)));
        Assert.Equal(BastionOrderKind.AttackArea, tank.Order.Kind);
        Assert.Equal(BastionOrderKind.AttackArea, scout.Order.Kind);
        Assert.Equal(target, scout.Order.Target);
    }

    [Fact]
    public void TryIssueBastionOrder_AttackArea_CompletesResetsScoutToDefend()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        // Completion waits for combat and scouts; start both one tile from the target.
        var target = new TilePosition(bastion.Position.X + 4, bastion.Position.Y);
        var staging = new TilePosition(target.X - 1, target.Y);
        Assert.True(simulation.TryTeleportEntityForTests(tank.Id, staging));
        Assert.True(simulation.TryTeleportEntityForTests(scout.Id, staging));
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.AttackArea, target)));

        AdvanceTicks(simulation, 80);

        Assert.Equal(BastionOrderKind.Defend, bastion.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, simulation.World.GetEntity(tank.Id)!.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, simulation.World.GetEntity(scout.Id)!.Order.Kind);
    }

    [Fact]
    public void TryIssueBastionOrder_AttackArea_ScoutOnlyCompletesToDefend()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        var target = new TilePosition(scout.Position.X + 1, scout.Position.Y);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.AttackArea, target)));

        AdvanceTicks(simulation, 80);

        Assert.Equal(BastionOrderKind.Defend, bastion.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, simulation.World.GetEntity(scout.Id)!.Order.Kind);
    }

    [Fact]
    public void TryIssueBastionOrder_Defend_ClearsInFlightAttackPath()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        var attackTarget = new TilePosition(bastion.Position.X + 8, bastion.Position.Y);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.AttackArea, attackTarget)));

        AdvanceTicks(simulation, 12);
        scout = simulation.World.GetEntity(scout.Id)!;
        Assert.True(scout.CurrentWaypoint is not null || scout.MovementPath.Count > 0);

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Defend)));
        scout = simulation.World.GetEntity(scout.Id)!;
        Assert.Equal(BastionOrderKind.Defend, scout.Order.Kind);
        Assert.Null(scout.CurrentWaypoint);
        Assert.Empty(scout.MovementPath);
    }

    [Fact]
    public void TryIssueBastionOrder_Patrol_CyclesBetweenWaypoints()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var a = tank.Position;
        var b = new TilePosition(a.X + 2, a.Y);
        Assert.True(simulation.TryIssueBastionOrder(
            bastion.Id, new PlayerId(1),
            new BastionOrder(BastionOrderKind.Patrol, Waypoints: [a, b])));

        AdvanceTicks(simulation, 200);
        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.Equal(BastionOrderKind.Patrol, tank.Order.Kind);
        Assert.True(tank.Order.WaypointIndex is 0 or 1);
        Assert.True(tank.Position == a || tank.Position == b || tank.Position.ManhattanDistance(a) <= 2);
    }

    [Fact]
    public void TryIssueBastionOrder_Patrol_RejectsInvalidWaypointCount()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.False(simulation.TryIssueBastionOrder(
            bastion.Id, new PlayerId(1),
            new BastionOrder(BastionOrderKind.Patrol, Waypoints: [new TilePosition(1, 1)])));
        Assert.False(simulation.TryIssueBastionOrder(
            bastion.Id, new PlayerId(1),
            new BastionOrder(
                BastionOrderKind.Patrol,
                Waypoints:
                [
                    new TilePosition(1, 1),
                    new TilePosition(2, 1),
                    new TilePosition(3, 1),
                    new TilePosition(4, 1),
                    new TilePosition(5, 1)
                ])));
    }

    [Fact]
    public void SpawnProducedUnit_InheritsBastionScoutOrder_OnlyForScouts()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var scoutTarget = new TilePosition(bastion.Position.X + 4, bastion.Position.Y);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Scout, scoutTarget)));

        var tank = ProduceTankForBastion(simulation, bastion.Id);
        Assert.Equal(BastionOrderKind.Defend, tank.Order.Kind);

        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        Assert.Equal(BastionOrderKind.Scout, scout.Order.Kind);
        Assert.Equal(scoutTarget, scout.Order.Target);
    }

    [Fact]
    public void ProcessCombat_DamagesNearestEnemyAndAppliesCooldown()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(PlaceAdjacent(simulation, attacker, defender.Position));

        var expectedDamage = simulation.ComputeCombatDamageForTests(attacker.Id, defender.Id);
        var expectedCooldown = simulation.ResolveStat(
            attacker.OwnerId!.Value,
            ResearchStatIds.AttackCooldownTicks,
            MvpDefinitions.GetStats(EntityKind.Commander).AttackCooldownTicks,
            EntityKind.Commander.ToString());
        var healthBefore = defender.Health;
        simulation.AdvanceTick();

        Assert.Equal(healthBefore - expectedDamage, defender.Health);
        Assert.Equal(expectedCooldown, attacker.AttackCooldownRemaining);
    }

    [Fact]
    public void ProcessCombat_EmitsCombatShotEvent()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.TryTeleportEntityForTests(attacker.Id, new TilePosition(10, 10)));
        Assert.True(simulation.TryTeleportEntityForTests(defender.Id, new TilePosition(11, 11)));

        simulation.AdvanceTick();

        Assert.Contains(
            simulation.CombatShotsThisTick,
            shot => shot.AttackerId == attacker.Id
                && shot.TargetId == defender.Id
                && shot.ProjectileKind == ProjectileKind.GroundToGround);
    }

    [Fact]
    public void CanOccupyWorldPosition_RejectsOverlapWithOtherUnit()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tankA = ProduceTankForBastion(simulation, bastion.Id);
        var tankB = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(simulation.TryTeleportEntityForTests(tankA.Id, new TilePosition(20, 20)));
        Assert.True(simulation.TryTeleportEntityForTests(tankB.Id, new TilePosition(30, 30)));

        Assert.False(simulation.CanOccupyWorldPositionForTests(tankB.Id, tankA.WorldPosition));
        Assert.True(simulation.CanOccupyWorldPositionForTests(
            tankB.Id,
            WorldPosition.FromTileCenter(new TilePosition(40, 40))));
    }

    [Fact]
    public void CanOccupyWorldPosition_AllowsOverlapWhileMoverIsMoving()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tankA = ProduceTankForBastion(simulation, bastion.Id);
        var tankB = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(simulation.TryTeleportEntityForTests(tankA.Id, new TilePosition(20, 20)));
        Assert.True(simulation.TryTeleportEntityForTests(tankB.Id, new TilePosition(22, 20)));
        var attackTarget = new TilePosition(40, 20);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.AttackArea, attackTarget)));

        AdvanceTicks(simulation, 5);
        tankB = simulation.World.GetEntity(tankB.Id)!;
        Assert.True(tankB.CurrentWaypoint is not null || tankB.MovementPath.Count > 0);

        Assert.True(simulation.CanOccupyWorldPositionForTests(tankB.Id, tankA.WorldPosition));
    }

    [Fact]
    public void ProcessCombat_EuclideanDiagonalInRange_DamagesEnemy()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        // Manhattan distance 2, Euclidean distance sqrt(2) ~= 1.41 — in range for AttackRange 3, was out of Manhattan range 1.
        Assert.True(simulation.TryTeleportEntityForTests(attacker.Id, new TilePosition(10, 10)));
        Assert.True(simulation.TryTeleportEntityForTests(defender.Id, new TilePosition(11, 11)));

        var healthBefore = defender.Health;
        var expectedDamage = simulation.ComputeCombatDamageForTests(attacker.Id, defender.Id);
        simulation.AdvanceTick();

        Assert.Equal(healthBefore - expectedDamage, defender.Health);
        Assert.True(attacker.Position.IsWithinEuclideanRange(defender.Position, 3));
        Assert.Equal(2, attacker.Position.ManhattanDistance(defender.Position));
    }

    [Fact]
    public void ProcessCombat_DoesNotDamageFriendlyEntities()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var friendly = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Bastion && entity.OwnerId == new PlayerId(1));
        Assert.True(PlaceAdjacent(simulation, attacker, friendly.Position));

        var healthBefore = friendly.Health;
        simulation.AdvanceTick();

        Assert.Equal(healthBefore, friendly.Health);
    }

    [Fact]
    public void ProcessCombat_WhenEnemyCommanderDies_PlayerWins()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        var defenderId = defender.Id;
        Assert.True(PlaceAdjacent(simulation, attacker, defender.Position));
        var lethal = simulation.ComputeCombatDamageForTests(attacker.Id, defender.Id);
        Assert.True(simulation.TrySetEntityHealthForTests(defender.Id, lethal));

        simulation.AdvanceTick();

        Assert.Equal(GameStatus.PlayerWon, simulation.Status);
        Assert.Equal(new PlayerId(1), simulation.WinnerId);
        Assert.False(defender.IsAlive);
        Assert.Null(simulation.World.GetEntity(defenderId));
        Assert.DoesNotContain(
            simulation.World.Entities,
            entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.Contains(
            simulation.World.Entities,
            entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1) && entity.IsAlive);
    }

    [Fact]
    public void DamageEntity_WhenCommanderDies_RemovesDefeatedCommanderOnNextAdvance()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        var defenderId = defender.Id;

        simulation.DamageEntity(defenderId, defender.Health);

        Assert.Equal(GameStatus.PlayerWon, simulation.Status);
        Assert.Equal(new PlayerId(1), simulation.WinnerId);
        Assert.False(defender.IsAlive);
        Assert.NotNull(simulation.World.GetEntity(defenderId));

        simulation.AdvanceTick();

        Assert.Null(simulation.World.GetEntity(defenderId));
        Assert.DoesNotContain(
            simulation.World.Entities,
            entity => entity.Kind == EntityKind.Commander && !entity.IsAlive);
    }

    [Fact]
    public void ProcessBastions_Defend_GarrisonsAdjacentUnit()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(PlaceAdjacent(simulation, tank, bastion.Position));

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Defend)));
        simulation.AdvanceTick();

        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.True(tank.IsGarrisoned);
        Assert.Equal(BastionOrderKind.Defend, tank.Order.Kind);
    }

    [Fact]
    public void ProcessBastions_Defend_UngarrisonsWhenEnemyInVision()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(PlaceAdjacent(simulation, tank, bastion.Position));
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Defend)));
        simulation.AdvanceTick();
        Assert.True(simulation.World.GetEntity(tank.Id)!.IsGarrisoned);

        var enemy = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.TryTeleportEntityForTests(
            enemy.Id,
            new TilePosition(bastion.Position.X + 3, bastion.Position.Y)));

        simulation.AdvanceTick();
        Assert.False(simulation.World.GetEntity(tank.Id)!.IsGarrisoned);
    }

    [Fact]
    public void BastionDeath_KillsAssignedGarrisonedUnits()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(PlaceAdjacent(simulation, tank, bastion.Position));
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Defend)));
        simulation.AdvanceTick();
        Assert.True(simulation.World.GetEntity(tank.Id)!.IsGarrisoned);

        var tankId = tank.Id;
        var bastionId = bastion.Id;
        simulation.DamageEntity(bastionId, bastion.Health);
        Assert.False(simulation.World.GetEntity(tankId)!.IsAlive);
        Assert.False(simulation.World.GetEntity(bastionId)!.IsAlive);

        simulation.AdvanceTick();
        Assert.Null(simulation.World.GetEntity(tankId));
        Assert.Null(simulation.World.GetEntity(bastionId));
    }

    [Fact]
    public void BastionDeath_DoesNotKillAssignedFactory()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, new TilePosition(2, simulation.World.Size.Height / 2 + 6), out var factoryId));
        AdvanceTicks(simulation, 30);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, new PlayerId(1), EntityKind.BasicTank));
        Assert.Null(simulation.World.GetEntity(factoryId)!.AssignedBastionId);

        simulation.DamageEntity(bastion.Id, bastion.Health);
        Assert.False(simulation.World.GetEntity(bastion.Id)!.IsAlive);
        Assert.True(simulation.World.GetEntity(factoryId)!.IsAlive);
    }

    [Fact]
    public void ProcessCombat_BastionDeath_CascadesAssignedUnitsSameTick()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        // Keep the tank far from the attacker so combat targets the bastion only.
        Assert.True(simulation.TryTeleportEntityForTests(tank.Id, new TilePosition(2, simulation.World.Size.Height / 2 + 6)));
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Defend)));

        var enemy = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(PlaceAdjacent(simulation, enemy, bastion.Position));
        var lethal = simulation.ComputeCombatDamageForTests(enemy.Id, bastion.Id);
        Assert.True(simulation.TrySetEntityHealthForTests(bastion.Id, lethal));

        var tankId = tank.Id;
        var bastionId = bastion.Id;
        simulation.AdvanceTick();

        Assert.Null(simulation.World.GetEntity(bastionId));
        Assert.Null(simulation.World.GetEntity(tankId));
    }

    [Fact]
    public void ProcessBastions_Scout_GarrisonsHomeCombatUnitsAndDoesNotSortie()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(PlaceAdjacent(simulation, tank, bastion.Position));
        var scoutTarget = new TilePosition(bastion.Position.X + 4, bastion.Position.Y);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Scout, scoutTarget)));
        Assert.Equal(BastionOrderKind.Defend, tank.Order.Kind);

        simulation.AdvanceTick();
        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.True(tank.IsGarrisoned);

        var enemy = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.TryTeleportEntityForTests(
            enemy.Id,
            new TilePosition(bastion.Position.X + 3, bastion.Position.Y)));

        simulation.AdvanceTick();
        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.True(tank.IsGarrisoned);
        Assert.Equal(bastion.Position, tank.Position);
    }

    [Fact]
    public void ProcessBastions_AttackArea_ScoutsJoinAttack()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        var start = scout.Position;
        var target = new TilePosition(tank.Position.X + 3, tank.Position.Y);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.AttackArea, target)));
        Assert.Equal(BastionOrderKind.AttackArea, scout.Order.Kind);
        Assert.Equal(target, scout.Order.Target);

        AdvanceTicks(simulation, 80);
        scout = simulation.World.GetEntity(scout.Id)!;
        Assert.False(scout.IsGarrisoned);
        Assert.NotEqual(start, scout.Position);
    }

    [Fact]
    public void GarrisonedUnit_DoesNotOccupyTiles()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(PlaceAdjacent(simulation, tank, bastion.Position));
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Defend)));
        simulation.AdvanceTick();

        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.True(tank.IsGarrisoned);
        Assert.DoesNotContain(simulation.World.GetEntitiesAt(bastion.Position), entity => entity.Id == tank.Id);
    }

    [Fact]
    public void ProcessBastions_Defend_GarrisonsFromFarFootprintEdgeAndHidesFromWorldQueries()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var footprint = MvpDefinitions.GetFootprint(EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);

        // Far side of the 3x3 footprint — outside Euclidean range 1 of bastion.Position,
        // which previously left units visible on the perimeter.
        var farEdge = new TilePosition(bastion.Position.X + footprint.Width, bastion.Position.Y + footprint.Height - 1);
        Assert.False(farEdge.IsWithinEuclideanRange(bastion.Position, 1));
        Assert.True(simulation.TryTeleportEntityForTests(tank.Id, farEdge));

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new PlayerId(1), new BastionOrder(BastionOrderKind.Defend)));
        simulation.AdvanceTick();

        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.True(tank.IsGarrisoned);
        Assert.Equal(bastion.Position, tank.Position);
        Assert.DoesNotContain(simulation.World.GetEntitiesAt(farEdge), entity => entity.Id == tank.Id);
        Assert.DoesNotContain(simulation.World.GetEntitiesAt(bastion.Position), entity => entity.Id == tank.Id);
        Assert.DoesNotContain(
            simulation.World.Entities,
            entity => entity.Id == tank.Id && entity.IsAlive && !entity.IsGarrisoned);
    }

    private static WorldEntity ProduceScoutForBastion(GameSimulation simulation, int bastionId)
    {
        Assert.True(simulation.TryForceCompleteResearch(new PlayerId(1), TechnologyId.Scout));
        var desired = simulation.World.GetEntity(bastionId)!.BastionTemplate.GetValueOrDefault(EntityKind.Scout) + 1;
        Assert.True(simulation.TrySetBastionTemplate(bastionId, new PlayerId(1), EntityKind.Scout, desired));
        var factory = simulation.World.Entities.FirstOrDefault(entity =>
            entity.IsAlive && entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.DroneCenter);
        int factoryId;
        if (factory is null)
        {
            Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.DroneCenter, new TilePosition(6, simulation.World.Size.Height / 2 + 6), out factoryId));
            AdvanceTicks(simulation, 30);
        }
        else
        {
            factoryId = factory.Id;
        }

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.Composite, 20);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, new PlayerId(1), EntityKind.Scout, bastionId));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.Scout].WorkTicks + 5);
        return simulation.World.Entities
            .Where(entity => entity.Kind == EntityKind.Scout && entity.AssignedBastionId == bastionId && entity.IsAlive)
            .OrderByDescending(entity => entity.Id)
            .First();
    }

    private static WorldEntity ProduceTankForBastion(GameSimulation simulation, int bastionId)
    {
        var desired = simulation.World.GetEntity(bastionId)!.BastionTemplate.GetValueOrDefault(EntityKind.BasicTank) + 1;
        Assert.True(simulation.TrySetBastionTemplate(bastionId, new PlayerId(1), EntityKind.BasicTank, desired));
        var factory = simulation.World.Entities.FirstOrDefault(entity =>
            entity.IsAlive && entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.TankFactory);
        int factoryId;
        if (factory is null)
        {
            Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, new TilePosition(2, simulation.World.Size.Height / 2 + 6), out factoryId));
            AdvanceTicks(simulation, 30);
        }
        else
        {
            factoryId = factory.Id;
        }

        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, new PlayerId(1), EntityKind.BasicTank, bastionId));
        AdvanceTicks(simulation, MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks + 5);
        return simulation.World.Entities
            .Where(entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastionId && entity.IsAlive)
            .OrderByDescending(entity => entity.Id)
            .First();
    }

    private static bool PlaceAdjacent(GameSimulation simulation, WorldEntity entity, TilePosition near)
    {
        var candidates = new[]
        {
            new TilePosition(near.X - 1, near.Y),
            new TilePosition(near.X + 1, near.Y),
            new TilePosition(near.X, near.Y - 1),
            new TilePosition(near.X, near.Y + 1)
        };

        var position = candidates.First(candidate => candidate.X >= 0 && candidate.Y >= 0);
        return simulation.TryTeleportEntityForTests(entity.Id, position);
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }
}
