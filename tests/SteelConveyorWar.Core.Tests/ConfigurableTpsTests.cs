namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R32: the configured ticks-per-second must actually drive Core's time-dependent quantities
/// (the energy-history window/ring capacity and the per-match tick rate), while the default
/// path stays byte-for-byte identical so determinism hashes do not move.
/// </summary>
public sealed class ConfigurableTpsTests
{
    private static readonly PlayerId Blue = new(1);

    [Fact]
    public void EnergyHistory_CapacityScalesWithConfiguredTps()
    {
        Assert.Equal(
            EnergyStatsHistory.MaxWindowSeconds * GameSimulation.DefaultTicksPerSecond,
            new EnergyStatsHistory().Capacity);
        Assert.Equal(
            EnergyStatsHistory.MaxWindowSeconds * 60,
            new EnergyStatsHistory(60).Capacity);
    }

    [Fact]
    public void EnergyHistory_RejectsNonPositiveTps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyStatsHistory(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyStatsHistory(-5));
    }

    [Fact]
    public void EnergyHistory_WindowDurationTracksTps_NotAFixedTickCount()
    {
        var empty = new Dictionary<EntityKind, int>();
        var h30 = new EnergyStatsHistory(30);
        var h60 = new EnergyStatsHistory(60);

        // 30 recorded ticks: a full 1-second display bucket at 30 TPS, only half a second at 60 TPS.
        for (long tick = 0; tick < 30; tick++)
        {
            h30.Record(tick, 0, 1, empty, empty);
            h60.Record(tick, 0, 1, empty, empty);
        }

        // At 30 TPS the first 1s bucket is complete; at 60 TPS it is not yet — proving the window
        // is measured in configured seconds, not a hard-coded tick count.
        Assert.Equal(1, h30.Query(10).SampleCount);
        Assert.Equal(0, h60.Query(10).SampleCount);

        // Feed the remaining 30 ticks so 60 TPS also closes its first 1s (60-tick) bucket.
        for (long tick = 30; tick < 60; tick++)
        {
            h60.Record(tick, 0, 1, empty, empty);
        }

        var window60 = h60.Query(10);
        Assert.Equal(1, window60.SampleCount);
        Assert.Equal(1f, window60.DemandSeries[0], 3);
    }

    [Fact]
    public void GameCreationOptions_ThreadsTpsIntoSimulationAndPlayerHistory()
    {
        var options = GameCreationOptions.Default with { TicksPerSecond = 60 };
        var simulation = GameSimulation.CreateNewGame(options);

        Assert.Equal(60, simulation.TicksPerSecond);
        Assert.Equal(
            EnergyStatsHistory.MaxWindowSeconds * 60,
            simulation.GetPlayer(Blue).EnergyStats.Capacity);
    }

    [Fact]
    public void DefaultTps_IsUsedWhenOptionsDoNotSpecifyIt()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);

        Assert.Equal(GameSimulation.DefaultTicksPerSecond, simulation.TicksPerSecond);
        Assert.Equal(
            EnergyStatsHistory.MaxWindowSeconds * GameSimulation.DefaultTicksPerSecond,
            simulation.GetPlayer(Blue).EnergyStats.Capacity);
    }

    [Fact]
    public void DefaultTps_LeavesStateHashUnchanged_RegressionGuard()
    {
        // A match created without a TPS and one pinned to the default must be behaviorally identical:
        // the new configurable plumbing must not perturb the deterministic state hash on the default path.
        var implicitDefault = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 7 });
        var explicitDefault = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 7, TicksPerSecond = GameSimulation.DefaultTicksPerSecond });

        for (var step = 0; step < 60; step++)
        {
            implicitDefault.AdvanceTick();
            explicitDefault.AdvanceTick();
        }

        Assert.Equal(implicitDefault.ComputeStateHash(), explicitDefault.ComputeStateHash());
    }
}
