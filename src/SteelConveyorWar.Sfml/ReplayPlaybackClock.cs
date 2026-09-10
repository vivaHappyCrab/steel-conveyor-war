namespace SteelConveyorWar.Sfml;

/// <summary>
/// Maps pause/rate/finished into the delta fed to <see cref="FixedStepPacer"/> during replay watch.
/// </summary>
public static class ReplayPlaybackClock
{
    public const int Rate1x = 1;
    public const int Rate2x = 2;
    public const int Rate4x = 4;

    public static float SimulationDelta(float frameDt, bool paused, int rate, bool finished)
    {
        if (paused || finished || rate <= 0)
        {
            return 0f;
        }

        return frameDt * rate;
    }
}
