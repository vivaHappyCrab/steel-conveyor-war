using System.Diagnostics;
using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Benchmarks;

/// <summary>
/// Shared scenario builders + quick (non-BDN) tick/alloc measurement for CI performance gate (R22/M04).
/// Budgets are calibrated ~5× local Release p95/alloc on the expanded matrix; CI sets
/// <c>SCW_BENCH_HARD_GATE=1</c> so exceeds fail the job. Local/verify stay soft unless that env is set.
/// </summary>
public static class TickScenarioRunner
{
    // Calibrated from Release --quick on expanded matrix (idle≈1.3ms/350KB,
    // power_equal≈0.7ms/200KB, hash≈1.6ms/1.6MB string alloc). ~3–5× headroom on time;
    // alloc budget covers ComputeStateHash scratch (~1.6MB) with margin.
    private const long DefaultP95BudgetNs = 5_000_000; // 5 ms/tick
    private const long DefaultAllocBudgetBytes = 2_500_000; // 2.5 MB/tick

    public static QuickBenchReport RunQuickMatrix(int ticks = 60)
    {
        var scenarios = new List<QuickScenarioResult>
        {
            Measure("idle_factory", () => CreateIdleFactory(42, buildingCount: 500), ticks),
            Measure("army_move", () => CreateArmyMove(43, unitCount: 100, advanceSetupTick: false), ticks),
            Measure("battle", () => CreateBattle(44, unitCount: 100), ticks),
            Measure("power_equal", () => CreatePowerEqual(45, consumerCount: 200), ticks),
            Measure("fow_moving", () => CreateFowMoving(46, unitCount: 100), ticks),
            MeasureHash("hash", () => CreateIdleFactory(47, buildingCount: 200), ticks),
        };
        return new QuickBenchReport(DateTimeOffset.UtcNow, scenarios);
    }

    public static GameSimulation CreateIdle(int seed) => GameSimulation.CreateNewGame(seed);

    public static GameSimulation CreateIdleFactory(int seed, int buildingCount)
    {
        var sim = GameSimulation.CreateNewGame(seed);
        var owner = new PlayerId(1);
        var spawned = 0;
        for (var y = 8; y < 110 && spawned < buildingCount; y++)
        {
            for (var x = 10; x < 110 && spawned < buildingCount; x++)
            {
                if (sim.TrySpawnEntityForTests(EntityKind.Assembler, new TilePosition(x, y), owner, out _))
                {
                    spawned++;
                }
            }
        }

        return sim;
    }

    /// <summary>
    /// Spawns units and enqueues long move orders. When <paramref name="advanceSetupTick"/> is false,
    /// the first measured/warmup ticks include path enqueue work (M04).
    /// </summary>
    public static GameSimulation CreateArmyMove(int seed, int unitCount, bool advanceSetupTick = true)
    {
        var sim = GameSimulation.CreateNewGame(seed);
        var owner = new PlayerId(1);
        var spawned = 0;
        for (var y = 10; y < 90 && spawned < unitCount; y++)
        {
            for (var x = 20; x < 60 && spawned < unitCount; x++)
            {
                if (!sim.TrySpawnEntityForTests(EntityKind.LightBot, new TilePosition(x, y), owner, out var id))
                {
                    continue;
                }

                var entityId = id;
                sim.EnqueueForNextTick(tick => new IssueMoveCommand(owner, tick, entityId, new TilePosition(x + 30, y)));
                spawned++;
            }
        }

        if (advanceSetupTick)
        {
            sim.AdvanceTick();
        }

        return sim;
    }

    public static GameSimulation CreateBattle(int seed, int unitCount)
    {
        var sim = GameSimulation.CreateNewGame(seed);
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        var half = Math.Max(1, unitCount / 2);
        SpawnLine(sim, blue, EntityKind.LightBot, startX: 40, y: 50, count: half, dx: 1);
        SpawnLine(sim, red, EntityKind.LightBot, startX: 70, y: 50, count: half, dx: -1);

        foreach (var entity in sim.World.Entities.Where(e => e.Kind == EntityKind.LightBot && e.OwnerId == blue))
        {
            var entityId = entity.Id;
            sim.EnqueueForNextTick(tick => new IssueMoveCommand(blue, tick, entityId, new TilePosition(70, 50)));
        }

        foreach (var entity in sim.World.Entities.Where(e => e.Kind == EntityKind.LightBot && e.OwnerId == red))
        {
            var entityId = entity.Id;
            sim.EnqueueForNextTick(tick => new IssueMoveCommand(red, tick, entityId, new TilePosition(40, 50)));
        }

        sim.AdvanceTick();
        return sim;
    }

    public static GameSimulation CreatePowerConsumers(int seed, int consumerCount)
    {
        var sim = GameSimulation.CreateNewGame(seed);
        var owner = new PlayerId(1);
        var spawned = 0;
        for (var y = 8; y < 100 && spawned < consumerCount; y++)
        {
            for (var x = 10; x < 80 && spawned < consumerCount; x++)
            {
                if (sim.TrySpawnEntityForTests(EntityKind.Assembler, new TilePosition(x, y), owner, out _))
                {
                    spawned++;
                }
            }
        }

        for (var i = 0; i < 20; i++)
        {
            _ = sim.TrySpawnEntityForTests(EntityKind.SolarPanel, new TilePosition(12 + i, 6), owner, out _);
        }

        return sim;
    }

    /// <summary>
    /// Equal-ratio stress: many identical empty assemblers plus high solar production (M04/M05).
    /// </summary>
    public static GameSimulation CreatePowerEqual(int seed, int consumerCount)
    {
        var sim = GameSimulation.CreateNewGame(seed);
        var owner = new PlayerId(1);
        var spawned = 0;
        for (var y = 8; y < 120 && spawned < consumerCount; y++)
        {
            for (var x = 10; x < 120 && spawned < consumerCount; x++)
            {
                if (sim.TrySpawnEntityForTests(EntityKind.Assembler, new TilePosition(x, y), owner, out _))
                {
                    spawned++;
                }
            }
        }

        // Dense solar row so FillEnergyBuffers has large equal-ratio plateaus every tick.
        for (var i = 0; i < 80; i++)
        {
            _ = sim.TrySpawnEntityForTests(EntityKind.SolarPanel, new TilePosition(8 + (i % 40), 4 + (i / 40)), owner, out _);
        }

        return sim;
    }

    /// <summary>Dedicated FoW moving-sources scenario (not an army_move alias).</summary>
    public static GameSimulation CreateFowMoving(int seed, int unitCount)
    {
        var sim = CreateArmyMove(seed, unitCount, advanceSetupTick: false);
        // Apply first move wave so subsequent ticks keep vision sources relocating.
        sim.AdvanceTick();
        var owner = new PlayerId(1);
        var bots = sim.World.Entities
            .Where(e => e.IsAlive && e.Kind == EntityKind.LightBot && e.OwnerId == owner)
            .ToList();
        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];
            var entityId = bot.Id;
            var dest = new TilePosition(
                Math.Clamp(bot.Position.X + ((i % 2 == 0) ? 20 : -10), 5, 90),
                Math.Clamp(bot.Position.Y + 15, 5, 90));
            sim.EnqueueForNextTick(tick => new IssueMoveCommand(owner, tick, entityId, dest));
        }

        return sim;
    }

    private static void SpawnLine(
        GameSimulation sim,
        PlayerId owner,
        EntityKind kind,
        int startX,
        int y,
        int count,
        int dx)
    {
        var x = startX;
        for (var i = 0; i < count; i++)
        {
            _ = sim.TrySpawnEntityForTests(kind, new TilePosition(x, y), owner, out _);
            x += dx;
        }
    }

    private static QuickScenarioResult Measure(string name, Func<GameSimulation> factory, int ticks)
    {
        var sim = factory();
        // Short warmup so army_move pathing still falls inside the measured window.
        for (var i = 0; i < 2; i++)
        {
            sim.AdvanceTick();
        }

        var samples = new long[ticks];
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < ticks; i++)
        {
            var sw = Stopwatch.StartNew();
            sim.AdvanceTick();
            sw.Stop();
            samples[i] = (long)sw.Elapsed.TotalNanoseconds;
        }

        var allocAfter = GC.GetAllocatedBytesForCurrentThread();
        return BuildResult(name, samples, allocAfter - allocBefore, ticks);
    }

    private static QuickScenarioResult MeasureHash(string name, Func<GameSimulation> factory, int ticks)
    {
        var sim = factory();
        for (var i = 0; i < 2; i++)
        {
            sim.AdvanceTick();
        }

        // Touch hash once so first-call statics are not in the sample window.
        _ = sim.ComputeStateHash();

        var samples = new long[ticks];
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < ticks; i++)
        {
            var sw = Stopwatch.StartNew();
            _ = sim.ComputeStateHash();
            sw.Stop();
            samples[i] = (long)sw.Elapsed.TotalNanoseconds;
        }

        var allocAfter = GC.GetAllocatedBytesForCurrentThread();
        return BuildResult(name, samples, allocAfter - allocBefore, ticks);
    }

    private static QuickScenarioResult BuildResult(string name, long[] samples, long allocDelta, int ticks)
    {
        Array.Sort(samples);
        var p50 = samples[samples.Length / 2];
        var p95 = samples[(int)(samples.Length * 0.95)];
        var allocPerTick = allocDelta / Math.Max(1, ticks);
        var exceeded = p95 > DefaultP95BudgetNs || allocPerTick > DefaultAllocBudgetBytes;
        return new QuickScenarioResult(
            name,
            p50,
            p95,
            allocPerTick,
            DefaultP95BudgetNs,
            DefaultAllocBudgetBytes,
            exceeded);
    }
}

public sealed record QuickBenchReport(DateTimeOffset GeneratedAtUtc, IReadOnlyList<QuickScenarioResult> Scenarios);

public sealed record QuickScenarioResult(
    string Name,
    long P50TickNs,
    long P95TickNs,
    long AllocBytesPerTick,
    long P95BudgetNs,
    long AllocBudgetBytes,
    bool ExceededSoftBudget);
