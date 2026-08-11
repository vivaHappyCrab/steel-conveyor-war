namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R15: batched emptiest-first energy fill must match the classic per-unit heap algorithm
/// (same <see cref="EnergyFillRatioComparer"/> order) and stay deterministic under dual-run ticks.
/// </summary>
public sealed class EnergyBatchedWaterfillTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(100)]
    [InlineData(1000)]
    public void BatchedFill_MatchesPerUnit_EqualRatiosAlternateById(int energy)
    {
        AssertBuffersMatch(
            energy,
            (1, 0, 10),
            (2, 0, 10),
            (3, 0, 10));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(500)]
    public void BatchedFill_MatchesPerUnit_MixedCapacitiesAndPartialFills(int energy)
    {
        AssertBuffersMatch(
            energy,
            (10, 0, 100),
            (20, 5, 10),
            (5, 0, 10),
            (7, 9, 10),
            (3, 50, 400));
    }

    [Fact]
    public void BatchedFill_MatchesPerUnit_DeficitAndSurplusAndFullSkip()
    {
        // Deficit: not enough to fill everyone.
        AssertBuffersMatch(
            energy: 11,
            (1, 0, 100),
            (2, 0, 100));

        // Surplus: more energy than remaining space (full entity skipped).
        AssertBuffersMatch(
            energy: 10_000,
            (1, 8, 10),
            (2, 10, 10),
            (3, 0, 5));

        // Single consumer takes everything up to capacity.
        AssertBuffersMatch(
            energy: 50,
            (42, 0, 30));
    }

    [Fact]
    public void BatchedFill_MatchesPerUnit_ManyConsumersHighProduction()
    {
        var specs = new (int Id, int Buffer, int Capacity)[200];
        for (var i = 0; i < specs.Length; i++)
        {
            specs[i] = (i + 1, i % 17, 20 + (i % 5) * 10);
        }

        AssertBuffersMatch(energy: 25_000, specs);
    }

    [Fact]
    public void DualRun_SameSeed_HashesAndEnergyBuffersMatch()
    {
        const int seed = 4242;
        const int ticks = 300;

        var (hashA, buffersA) = RunAndCapture(seed, ticks);
        var (hashB, buffersB) = RunAndCapture(seed, ticks);

        Assert.Equal(hashA, hashB);
        Assert.Equal(buffersA, buffersB);
    }

    [Fact]
    public void Simulation_ManySolarAndConsumers_FillsDeterministically()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 99);
        var owner = new PlayerId(1);
        var bastion = simulation.World.Entities.First(entity =>
            entity.OwnerId == owner && entity.Kind == EntityKind.Bastion);

        // Spawn many producers/consumers via test seam (skips ghost placement constraints).
        for (var i = 0; i < 8; i++)
        {
            var pos = new TilePosition(bastion.Position.X + 10 + i, bastion.Position.Y + 8);
            Assert.True(simulation.TrySpawnEntityForTests(EntityKind.SolarPanel, pos, owner, out _));
        }

        for (var i = 0; i < 24; i++)
        {
            var pos = new TilePosition(
                bastion.Position.X + 10 + (i % 8) * 2,
                bastion.Position.Y + 10 + (i / 8) * 2);
            Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Assembler, pos, owner, out _));
        }

        AdvanceTicks(simulation, 60);

        var consumers = simulation.World.Entities
            .Where(entity => entity.OwnerId == owner && entity.EnergyBufferCapacity > 0)
            .OrderBy(entity => entity.Id)
            .ToArray();
        Assert.True(consumers.Length >= 24);

        var energy = simulation.GetPlayer(owner).PowerProduced;
        Assert.True(energy > 0);

        var perUnit = CloneConsumersEmpty(consumers);
        var batched = CloneConsumersEmpty(consumers);

        PowerSystem.DistributeEnergyEmptiestFirstPerUnitForTests(perUnit, energy * 40);
        PowerSystem.DistributeEnergyEmptiestFirstBatchedForTests(batched, energy * 40);

        Assert.Equal(
            perUnit.Select(entity => (entity.Id, entity.EnergyBuffer)),
            batched.Select(entity => (entity.Id, entity.EnergyBuffer)));

        // Live dual-run with the same many-consumer layout stays hash-stable.
        var hashA = RunManyConsumerScenario(seed: 99, ticks: 120);
        var hashB = RunManyConsumerScenario(seed: 99, ticks: 120);
        Assert.Equal(hashA, hashB);
    }

    private static void AssertBuffersMatch(int energy, params (int Id, int Buffer, int Capacity)[] specs)
    {
        var perUnit = CreateConsumers(specs);
        var batched = CreateConsumers(specs);

        PowerSystem.DistributeEnergyEmptiestFirstPerUnitForTests(perUnit, energy);
        PowerSystem.DistributeEnergyEmptiestFirstBatchedForTests(batched, energy);

        Assert.Equal(
            perUnit.Select(entity => (entity.Id, entity.EnergyBuffer, entity.EnergyBufferCapacity)),
            batched.Select(entity => (entity.Id, entity.EnergyBuffer, entity.EnergyBufferCapacity)));
    }

    private static WorldEntity[] CreateConsumers(params (int Id, int Buffer, int Capacity)[] specs)
    {
        var owner = new PlayerId(1);
        return specs
            .Select(spec => new WorldEntity(spec.Id, EntityKind.Assembler, new TilePosition(spec.Id, 0), owner)
            {
                EnergyBuffer = spec.Buffer,
                EnergyBufferCapacity = spec.Capacity,
            })
            .ToArray();
    }

    private static WorldEntity[] CloneConsumersEmpty(IReadOnlyList<WorldEntity> source)
    {
        var clone = new WorldEntity[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            var entity = source[i];
            clone[i] = new WorldEntity(entity.Id, entity.Kind, entity.Position, entity.OwnerId)
            {
                EnergyBuffer = 0,
                EnergyBufferCapacity = entity.EnergyBufferCapacity,
            };
        }

        return clone;
    }

    private static (string Hash, IReadOnlyList<(int Id, int Buffer)> Buffers) RunAndCapture(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: seed);
        AdvanceTicks(simulation, ticks);
        var buffers = simulation.World.Entities
            .Where(entity => entity.EnergyBufferCapacity > 0)
            .OrderBy(entity => entity.Id)
            .Select(entity => (entity.Id, entity.EnergyBuffer))
            .ToArray();
        return (simulation.ComputeStateHash(), buffers);
    }

    private static string RunManyConsumerScenario(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: seed);
        var owner = new PlayerId(1);
        var bastion = simulation.World.Entities.First(entity =>
            entity.OwnerId == owner && entity.Kind == EntityKind.Bastion);

        for (var i = 0; i < 8; i++)
        {
            var pos = new TilePosition(bastion.Position.X + 10 + i, bastion.Position.Y + 8);
            Assert.True(simulation.TrySpawnEntityForTests(EntityKind.SolarPanel, pos, owner, out _));
        }

        for (var i = 0; i < 24; i++)
        {
            var pos = new TilePosition(
                bastion.Position.X + 10 + (i % 8) * 2,
                bastion.Position.Y + 10 + (i / 8) * 2);
            Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Assembler, pos, owner, out _));
        }

        AdvanceTicks(simulation, ticks);
        return simulation.ComputeStateHash();
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }
}
