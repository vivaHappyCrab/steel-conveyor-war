using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

public sealed class ReplayPlaybackClockTests
{
    [Fact]
    public void Paused_YieldsZeroDelta()
    {
        Assert.Equal(0f, ReplayPlaybackClock.SimulationDelta(0.016f, paused: true, rate: 2, finished: false));
    }

    [Fact]
    public void Finished_YieldsZeroDelta()
    {
        Assert.Equal(0f, ReplayPlaybackClock.SimulationDelta(0.016f, paused: false, rate: 4, finished: true));
    }

    [Fact]
    public void Rate2x_ScalesFrameDelta()
    {
        Assert.Equal(0.032f, ReplayPlaybackClock.SimulationDelta(0.016f, paused: false, rate: ReplayPlaybackClock.Rate2x, finished: false));
    }

    [Fact]
    public void NonPositiveRate_YieldsZeroDelta()
    {
        Assert.Equal(0f, ReplayPlaybackClock.SimulationDelta(0.016f, paused: false, rate: 0, finished: false));
    }
}
