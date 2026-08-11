using System.Diagnostics;
using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Benchmarks;

/// <summary>Shared scenario builders + quick (non-BDN) tick/alloc measurement for CI soft gate.</summary>
public static class TickScenarioRunner
{
    // Soft budgets: intentionally loose until calibrated; tighten via SCW_BENCH_HARD_GATE after baselines.
    private const long DefaultP95BudgetNs = 50_000_000; // 50 ms/tick
    private const long DefaultAllocBudgetBytes = 5_000_000;

    public static QuickBenchReport RunQuickMatrix(int ticks = 60)
    {
        var scenarios = new List<QuickScenarioResult>
        {
            Measure("idle", () => CreateIdle(42), ticks),
            Measure("army_move", () => CreateArmyMove(43, 40), ticks),
            Measure("battle", () => CreateBattle(44, 24), ticks),
            Measure("power", () => CreatePowerConsumers(45, 80), ticks),
            Measure("fow", () => CreateArmyMove(46, 40), ticks),
        };
        return new QuickBenchReport(DateTimeOffset.UtcNow, scenarios);
    }

    public static GameSimulation CreateIdle(int seed) => GameSimulation.CreateNewGame(seed);

    public static GameSimulation CreateArmyMove(int seed, int unitCount)
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

        sim.AdvanceTick();
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

        // Extra solar so FillEnergyBuffers has work every tick.
        for (var i = 0; i < 20; i++)
        {
            _ = sim.TrySpawnEntityForTests(EntityKind.SolarPanel, new TilePosition(12 + i, 6), owner, out _);
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
        // Warmup
        for (var i = 0; i < 5; i++)
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
            samples[i] = (long)(sw.Elapsed.TotalNanoseconds);
        }

        var allocAfter = GC.GetAllocatedBytesForCurrentThread();
        Array.Sort(samples);
        var p50 = samples[samples.Length / 2];
        var p95 = samples[(int)(samples.Length * 0.95)];
        var allocPerTick = (allocAfter - allocBefore) / Math.Max(1, ticks);
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
