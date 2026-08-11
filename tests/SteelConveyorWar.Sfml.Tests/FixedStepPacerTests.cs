using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

/// <summary>
/// R23: the fixed-step catch-up loop must be bounded so a huge frame delta (after a stall) cannot
/// spin thousands of ticks in one frame.
/// </summary>
public sealed class FixedStepPacerTests
{
    private const float FixedDelta = 1f / 30f;

    [Fact]
    public void NormalFrame_RunsExpectedTicks_AndCarriesRemainder()
    {
        // 2.5 steps of backlog -> 2 ticks, half a step carried forward.
        var (ticks, remaining) = FixedStepPacer.Plan(FixedDelta * 2.5f, FixedDelta);

        Assert.Equal(2, ticks);
        Assert.InRange(remaining, FixedDelta * 0.49f, FixedDelta * 0.51f);
    }

    [Fact]
    public void HugeDelta_IsCappedAtMaxTicksPerFrame_AndDropsBacklog()
    {
        // A 10-second pause at 30 TPS would be 300 ticks without a cap.
        var (ticks, remaining) = FixedStepPacer.Plan(10f, FixedDelta);

        Assert.Equal(FixedStepPacer.MaxTicksPerFrame, ticks);
        // Excess backlog is dropped: never carry a full step or more into the next frame.
        Assert.True(remaining < FixedDelta);
    }

    [Fact]
    public void InsufficientBacklog_RunsNoTicks_AndKeepsAccumulator()
    {
        var (ticks, remaining) = FixedStepPacer.Plan(FixedDelta * 0.4f, FixedDelta);

        Assert.Equal(0, ticks);
        Assert.InRange(remaining, FixedDelta * 0.39f, FixedDelta * 0.41f);
    }

    [Fact]
    public void NonPositiveFixedDelta_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FixedStepPacer.Plan(1f, 0f));
    }
}
