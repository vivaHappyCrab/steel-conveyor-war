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
        // Stay near the left-start area so the short ground path stays clear of mirrored bases.
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
    public void ProcessCombat_DamagesNearestEnemyAndAppliesCooldown()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        PlaceAdjacent(attacker, defender.Position);

        var stats = MvpDefinitions.GetStats(EntityKind.Commander);
        var healthBefore = defender.Health;
        simulation.AdvanceTick();

        Assert.Equal(healthBefore - stats.AttackDamage, defender.Health);
        Assert.Equal(stats.AttackCooldownTicks, attacker.AttackCooldownRemaining);
    }

    [Fact]
    public void ProcessCombat_DoesNotDamageFriendlyEntities()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var friendly = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Bastion && entity.OwnerId == new PlayerId(1));
        PlaceAdjacent(attacker, friendly.Position);

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
        PlaceAdjacent(attacker, defender.Position);
        defender.Health = MvpDefinitions.GetStats(EntityKind.Commander).AttackDamage;

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
        PlaceAdjacent(tank, bastion.Position);

        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.Defend)));
        simulation.AdvanceTick();

        tank = simulation.World.GetEntity(tank.Id)!;
        Assert.True(tank.IsGarrisoned);
        Assert.Equal(BastionOrderKind.Defend, tank.Order.Kind);
    }

    private static WorldEntity ProduceTankForBastion(GameSimulation simulation, int bastionId)
    {
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.TankFactory, new TilePosition(2, 20), out var factoryId));
        AdvanceTicks(simulation, 30);
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(factoryId, ItemId.CopperPlate, 10);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, EntityKind.BasicTank, bastionId));
        AdvanceTicks(simulation, 36);
        return simulation.World.Entities.Single(entity => entity.Kind == EntityKind.BasicTank && entity.AssignedBastionId == bastionId);
    }

    private static void PlaceAdjacent(WorldEntity entity, TilePosition near)
    {
        var candidates = new[]
        {
            new TilePosition(near.X - 1, near.Y),
            new TilePosition(near.X + 1, near.Y),
            new TilePosition(near.X, near.Y - 1),
            new TilePosition(near.X, near.Y + 1)
        };

        // Prefer an empty in-bounds neighbor so combat range stays Manhattan <= 1.
        var position = candidates.First(candidate => candidate.X >= 0 && candidate.Y >= 0);
        entity.Position = position;
        entity.WorldPosition = WorldPosition.FromTileCenter(position);
        entity.MoveTarget = null;
        entity.MovementPath.Clear();
        entity.CurrentWaypoint = null;
        entity.IsGarrisoned = false;
        entity.AttackCooldownRemaining = 0;
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }
}
