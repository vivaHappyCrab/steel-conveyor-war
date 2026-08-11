namespace SteelConveyorWar.Core.Tests;

public sealed class DeterminismHashTests
{
    [Fact]
    public void SameSeedIdleRuns_ProduceIdenticalHash()
    {
        var hashA = RunIdle(seed: 42, ticks: 120);
        var hashB = RunIdle(seed: 42, ticks: 120);
        Assert.Equal(hashA, hashB);
        Assert.False(string.IsNullOrWhiteSpace(hashA));
        Assert.Equal(64, hashA.Length);
    }

    [Fact]
    public void SameSeedCommandRuns_ProduceIdenticalHash()
    {
        var hashA = RunWithMove(seed: 42, ticks: 180);
        var hashB = RunWithMove(seed: 42, ticks: 180);
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void SameSeedQueuedCommandRuns_ProduceIdenticalHash()
    {
        var hashA = RunWithQueuedMove(seed: 42, ticks: 180);
        var hashB = RunWithQueuedMove(seed: 42, ticks: 180);
        Assert.Equal(hashA, hashB);
    }

    // R16: FoW dirty-region / disk-cache path must preserve state-hash equivalence under movement.
    [Fact]
    public void SameSeedMoveRuns_ProduceIdenticalHash_WithFogDirtyRegions()
    {
        var hashA = RunWithMove(seed: 71, ticks: 120);
        var hashB = RunWithMove(seed: 71, ticks: 120);
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentHashes()
    {
        var hashA = RunIdle(seed: 42, ticks: 30);
        var hashB = RunIdle(seed: 43, ticks: 30);
        // RandomSeed is part of the hash surface even when terrain ignores it today.
        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void PresentationSideChannels_DoNotAffectStateHash()
    {
        var simulation = GameSimulation.CreateNewGame(42);
        for (var i = 0; i < 60; i++)
        {
            simulation.AdvanceTick();
        }

        var before = simulation.ComputeStateHash();

        simulation.Presentation.AddCombatShot(new CombatShotEvent(
            AttackerId: 1,
            TargetId: 2,
            From: new WorldPosition(1500, 2500),
            To: new WorldPosition(3500, 4500),
            ProjectileKind: ProjectileKind.GroundToGround));

        var player = simulation.GetPlayer(new PlayerId(1));
        player.EnergyStats.Record(
            tick: simulation.Tick + 1,
            totalProduced: 999,
            totalConsumed: 888,
            producedByKind: new Dictionary<EntityKind, int> { [EntityKind.SolarPanel] = 999 },
            consumedByKind: new Dictionary<EntityKind, int> { [EntityKind.Assembler] = 888 });
        player.SetTechSignatures(new[] { new TechSignatureHotspot(0, 0, 42) });

        Assert.NotEmpty(simulation.CombatShotsThisTick);
        Assert.True(player.EnergyStats.SampleCount > 0);
        Assert.NotEmpty(player.TechSignatures);
        Assert.Equal(before, simulation.ComputeStateHash());
    }

    // R08: QueuedBuildOrder / QueuedDemolishOrder drive future ticks, so they must be part of the hash surface.
    [Fact]
    public void QueuedBuildOrder_AffectsStateHash()
    {
        var baseline = GameSimulation.CreateNewGame(42);
        var withOrder = GameSimulation.CreateNewGame(42);

        var commander = withOrder.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
        commander.QueuedBuildOrder = new CommanderBuildOrder(
            EntityKind.TankFactory,
            new TilePosition(commander.Position.X + 1, commander.Position.Y),
            Direction.East);

        Assert.NotEqual(baseline.ComputeStateHash(), withOrder.ComputeStateHash());
    }

    [Fact]
    public void QueuedDemolishOrder_AffectsStateHash()
    {
        var baseline = GameSimulation.CreateNewGame(42);
        var withOrder = GameSimulation.CreateNewGame(42);

        var commander = withOrder.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
        var hub = withOrder.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Hub);
        commander.QueuedDemolishOrder = new CommanderDemolishOrder(hub.Id);

        Assert.NotEqual(baseline.ComputeStateHash(), withOrder.ComputeStateHash());
    }

    [Fact]
    public void SameQueuedBuildOrder_ProducesIdenticalHash()
    {
        var first = GameSimulation.CreateNewGame(42);
        var second = GameSimulation.CreateNewGame(42);

        foreach (var simulation in new[] { first, second })
        {
            var commander = simulation.World.Entities.First(entity =>
                entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
            commander.QueuedBuildOrder = new CommanderBuildOrder(
                EntityKind.TankFactory,
                new TilePosition(commander.Position.X + 1, commander.Position.Y),
                Direction.East);
        }

        Assert.Equal(first.ComputeStateHash(), second.ComputeStateHash());
    }

    [Fact]
    public void SameSeedMassUnitMoves_ProduceIdenticalHash()
    {
        var hashA = RunMassUnitMoves(seed: 42, unitCount: 48, ticks: 90);
        var hashB = RunMassUnitMoves(seed: 42, unitCount: 48, ticks: 90);
        Assert.Equal(hashA, hashB);
        Assert.False(string.IsNullOrWhiteSpace(hashA));
    }

    [Fact]
    public void SameSeedMassUnitMoves_WithEnemyThreat_ProduceIdenticalHash()
    {
        var hashA = RunMassUnitMovesWithThreat(seed: 7, ticks: 60);
        var hashB = RunMassUnitMovesWithThreat(seed: 7, ticks: 60);
        Assert.Equal(hashA, hashB);
    }

    private static string RunIdle(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }

    private static string RunWithMove(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
        Assert.True(simulation.TryIssueMoveCommand(commander.Id, new PlayerId(1), new TilePosition(12, 10)));
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }

    private static string RunWithQueuedMove(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Commander);
        simulation.EnqueueForNextTick(tick => new Commands.IssueMoveCommand(
            new PlayerId(1), tick, commander.Id, new TilePosition(12, 10)));
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }

    // R07: many moving units exercise spatial collision, A* workspace reuse, and pathfinding.
    private static string RunMassUnitMoves(int seed, int unitCount, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        var player = new PlayerId(1);
        var bastion = simulation.World.Entities.First(entity =>
            entity.OwnerId == player && entity.Kind == EntityKind.Bastion);
        var originX = bastion.Position.X + 6;
        var originY = bastion.Position.Y + 4;

        for (var i = 0; i < unitCount; i++)
        {
            var position = new TilePosition(originX + (i % 8), originY + (i / 8));
            Assert.True(simulation.TrySpawnEntityForTests(EntityKind.BasicTank, position, player, out var tankId));
            var tank = simulation.World.GetEntity(tankId)!;
            tank.AssignedBastionId = bastion.Id;
        }

        var target = new TilePosition(originX + 18, originY + 2);
        Assert.True(simulation.TryIssueBastionOrder(
            bastion.Id,
            player,
            new BastionOrder(BastionOrderKind.AttackArea, target)));

        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }

    // R07: defend threat search via spatial index (enemy inside bastion vision).
    private static string RunMassUnitMovesWithThreat(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        var player = new PlayerId(1);
        var enemy = new PlayerId(2);
        var bastion = simulation.World.Entities.First(entity =>
            entity.OwnerId == player && entity.Kind == EntityKind.Bastion);

        for (var i = 0; i < 12; i++)
        {
            var position = new TilePosition(bastion.Position.X + 5 + (i % 4), bastion.Position.Y + 4 + (i / 4));
            Assert.True(simulation.TrySpawnEntityForTests(EntityKind.BasicTank, position, player, out var tankId));
            simulation.World.GetEntity(tankId)!.AssignedBastionId = bastion.Id;
        }

        Assert.True(simulation.TryIssueBastionOrder(
            bastion.Id,
            player,
            new BastionOrder(BastionOrderKind.Defend)));

        var threatTile = new TilePosition(bastion.Position.X + 4, bastion.Position.Y);
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.LightBot, threatTile, enemy, out _));

        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }
}
