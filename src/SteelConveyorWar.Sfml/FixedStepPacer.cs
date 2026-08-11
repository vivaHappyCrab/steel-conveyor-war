namespace SteelConveyorWar.Sfml;

/// <summary>
/// R23: bounds the fixed-step catch-up loop so a long stall (minimized window, breakpoint) cannot
/// trigger a spiral-of-death where the simulation tries to run thousands of ticks in one frame.
/// </summary>
public static class FixedStepPacer
{
    /// <summary>Maximum simulation ticks advanced in a single rendered frame.</summary>
    public const int MaxTicksPerFrame = 8;

    /// <summary>
    /// Given the accumulated real time and the fixed step, returns how many ticks to run this frame
    /// (capped at <see cref="MaxTicksPerFrame"/>) and the accumulator to carry into the next frame.
    /// <para>
    /// Remainder policy: normal frames carry the sub-step remainder for smooth pacing. When the cap
    /// is hit and backlog still exceeds one step, the excess is <b>dropped</b> (only the sub-step
    /// remainder is kept) so debt cannot grow unbounded. Dropping is acceptable for this local
    /// single-player host; a future lockstep network host must NOT drop — it must stall/resync to
    /// stay in step with peers.
    /// </para>
    /// </summary>
    public static (int Ticks, float RemainingAccumulator) Plan(float accumulator, float fixedDelta)
    {
        if (fixedDelta <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fixedDelta), fixedDelta, "Fixed delta must be positive.");
        }

        var ticks = 0;
        while (accumulator >= fixedDelta && ticks < MaxTicksPerFrame)
        {
            accumulator -= fixedDelta;
            ticks++;
        }

        // Cap reached but backlog remains: drop the excess, keep only the sub-step remainder.
        if (accumulator >= fixedDelta)
        {
            accumulator %= fixedDelta;
        }

        return (ticks, accumulator);
    }
}
