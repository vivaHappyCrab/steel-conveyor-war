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

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.AttackArea, target)));
        Assert.Equal(BastionOrderKind.AttackArea, bastion.Order.Kind);
        Assert.Equal(target, bastion.Order.Target);
        Assert.Equal(BastionOrderKind.AttackArea, tank.Order.Kind);
        Assert.Equal(target, tank.Order.Target);
        Assert.False(tank.IsGarrisoned);

        AdvanceTicks(simulation, 120);
        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.NotEqual(start, tank.Position);
        Assert.True(tank.Position.ManhattanDistance(target) <= start.ManhattanDistance(target));
    }

    [Fact]
    public void TryIssueBastionOrder_AttackArea_CompletesToDefendWhenCombatUnitsReachTarget()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var target = new TilePosition(tank.Position.X + 1, tank.Position.Y);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.AttackArea, target)));

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

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Scout, target)));
        Assert.Equal(BastionOrderKind.Scout, bastion.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, tank.Order.Kind);
        Assert.Equal(BastionOrderKind.Scout, scout.Order.Kind);
        Assert.Equal(target, scout.Order.Target);

        AdvanceTicks(simulation, 80);

        Assert.Equal(BastionOrderKind.Defend, bastion.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, simulation.World.GetEntity(scout.Id)!.Order.Kind);
    }

    [Fact]
    public void TryIssueBastionOrder_AttackArea_KeepsScoutsOnDefend()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        var target = new TilePosition(tank.Position.X + 2, tank.Position.Y);

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.AttackArea, target)));
        Assert.Equal(BastionOrderKind.AttackArea, tank.Order.Kind);
        Assert.Equal(BastionOrderKind.Defend, scout.Order.Kind);
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
            bastion.Id,
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
            bastion.Id,
            new BastionOrder(BastionOrderKind.Patrol, Waypoints: [new TilePosition(1, 1)])));
        Assert.False(simulation.TryIssueBastionOrder(
            bastion.Id,
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
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Scout, scoutTarget)));

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

        var stats = MvpDefinitions.GetStats(EntityKind.Commander);
        var healthBefore = defender.Health;
        simulation.AdvanceTick();

        Assert.Equal(healthBefore - stats.AttackDamage, defender.Health);
        Assert.Equal(stats.AttackCooldownTicks, attacker.AttackCooldownRemaining);
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
        simulation.AdvanceTick();

        Assert.Equal(healthBefore - MvpDefinitions.GetStats(EntityKind.Commander).AttackDamage, defender.Health);
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
        Assert.True(PlaceAdjacent(simulation, attacker, defender.Position));
        Assert.True(simulation.TrySetEntityHealthForTests(
            defender.Id,
            MvpDefinitions.GetStats(EntityKind.Commander).AttackDamage));

        simulation.AdvanceTick();

        Assert.Equal(GameStatus.PlayerWon, simulation.Status);
        Assert.Equal(new PlayerId(1), simulation.WinnerId);
        Assert.False(defender.IsAlive);
    }

    [Fact]
    public void ProcessBastions_Defend_GarrisonsAdjacentUnit()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(PlaceAdjacent(simulation, tank, bastion.Position));

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Defend)));
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
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Defend)));
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
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Defend)));
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
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastion.Id));
        Assert.Equal(bastion.Id, simulation.World.GetEntity(factoryId)!.AssignedBastionId);

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
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Defend)));

        var enemy = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(PlaceAdjacent(simulation, enemy, bastion.Position));
        Assert.True(simulation.TrySetEntityHealthForTests(
            bastion.Id,
            MvpDefinitions.GetStats(EntityKind.Commander).AttackDamage));

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
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Scout, scoutTarget)));
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
    public void ProcessBastions_AttackArea_GarrisonsHomeScouts()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        var scout = ProduceScoutForBastion(simulation, bastion.Id);
        Assert.True(PlaceAdjacent(simulation, scout, bastion.Position));
        var target = new TilePosition(tank.Position.X + 3, tank.Position.Y);
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.AttackArea, target)));
        Assert.Equal(BastionOrderKind.Defend, scout.Order.Kind);

        simulation.AdvanceTick();
        scout = simulation.World.GetEntity(scout.Id)!;
        Assert.True(scout.IsGarrisoned);
    }

    [Fact]
    public void GarrisonedUnit_DoesNotOccupyTiles()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        var tank = ProduceTankForBastion(simulation, bastion.Id);
        Assert.True(PlaceAdjacent(simulation, tank, bastion.Position));
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Defend)));
        simulation.AdvanceTick();

        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.True(tank.IsGarrisoned);
        Assert.DoesNotContain(simulation.World.GetEntitiesAt(bastion.Position), entity => entity.Id == tank.Id);
    }

    private static WorldEntity ProduceTankForBastion(GameSimulation simulation, int bastionId)
    {
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, new TilePosition(2, simulation.World.Size.Height / 2 + 6), out var factoryId));
        AdvanceTicks(simulation, 30);
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastionId));
        AdvanceTicks(simulation, 36);
        return simulation.World.Entities.Single(entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastionId);
    }

    private static WorldEntity ProduceScoutForBastion(GameSimulation simulation, int bastionId)
    {
        Assert.True(simulation.TryForceCompleteResearch(new PlayerId(1), TechnologyId.Scout));
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.DroneCenter, new TilePosition(6, simulation.World.Size.Height / 2 + 6), out var factoryId));
        AdvanceTicks(simulation, 30);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 20);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.Scout, bastionId));
        AdvanceTicks(simulation, 30);
        return simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Scout && entity.AssignedBastionId == bastionId);
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
