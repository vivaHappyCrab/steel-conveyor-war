using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Benchmarks;

/// <summary>R22/M04: BenchmarkDotNet scenarios for Core tick cost (idle / army / battle / FoW / power).</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 1, iterationCount: 5)]
public class SimulationTickBenchmarks
{
    private GameSimulation _idle = null!;
    private GameSimulation _army = null!;
    private GameSimulation _battle = null!;
    private GameSimulation _power = null!;
    private GameSimulation _fow = null!;

    [GlobalSetup]
    public void Setup()
    {
        _idle = TickScenarioRunner.CreateIdleFactory(seed: 42, buildingCount: 500);
        _army = TickScenarioRunner.CreateArmyMove(seed: 43, unitCount: 100);
        _battle = TickScenarioRunner.CreateBattle(seed: 44, unitCount: 100);
        _power = TickScenarioRunner.CreatePowerEqual(seed: 45, consumerCount: 200);
        _fow = TickScenarioRunner.CreateFowMoving(seed: 46, unitCount: 100);
    }

    [Benchmark(Baseline = true)]
    public void IdleFactory_Tick() => _idle.AdvanceTick();

    [Benchmark]
    public void ArmyMove_Tick() => _army.AdvanceTick();

    [Benchmark]
    public void Battle_Tick() => _battle.AdvanceTick();

    [Benchmark]
    public void PowerEqual_Tick() => _power.AdvanceTick();

    [Benchmark]
    public void FogOfWarMoving_Tick() => _fow.AdvanceTick();
}
