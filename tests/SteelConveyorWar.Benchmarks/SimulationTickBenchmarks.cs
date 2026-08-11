using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Benchmarks;

/// <summary>R22: BenchmarkDotNet scenarios for Core tick cost (idle / army / battle / FoW / power).</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 1, iterationCount: 5)]
public class SimulationTickBenchmarks
{
    private GameSimulation _idle = null!;
    private GameSimulation _army = null!;
    private GameSimulation _battle = null!;
    private GameSimulation _power = null!;

    [GlobalSetup]
    public void Setup()
    {
        _idle = TickScenarioRunner.CreateIdle(seed: 42);
        _army = TickScenarioRunner.CreateArmyMove(seed: 43, unitCount: 40);
        _battle = TickScenarioRunner.CreateBattle(seed: 44, unitCount: 24);
        _power = TickScenarioRunner.CreatePowerConsumers(seed: 45, consumerCount: 80);
    }

    [Benchmark(Baseline = true)]
    public void IdleFactory_Tick() => _idle.AdvanceTick();

    [Benchmark]
    public void ArmyMove_Tick() => _army.AdvanceTick();

    [Benchmark]
    public void Battle_Tick() => _battle.AdvanceTick();

    [Benchmark]
    public void PowerFill_Tick() => _power.AdvanceTick();

    [Benchmark]
    public void FogOfWar_Tick()
    {
        // FoW runs every tick; army scenario stresses vision sources.
        _army.AdvanceTick();
    }
}
