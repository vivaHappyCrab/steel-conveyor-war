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
            From: new WorldPosition(1.5, 2.5),
            To: new WorldPosition(3.5, 4.5),
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
}
