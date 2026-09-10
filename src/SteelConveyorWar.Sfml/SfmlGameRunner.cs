using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public sealed record PlaySessionResult(GameSimulation Simulation);

/// <summary>
/// Thin SFML host entry: delegates window/input/render/UI composition to <see cref="SfmlPlaySession"/>.
/// No gameplay rules live in the SFML adapter.
/// </summary>
public sealed class SfmlGameRunner
{
    public PlaySessionResult Run(
        GameSimulation simulation,
        int? maxFrames = null,
        SfmlDisplayOptions? display = null,
        ReplayWatchSession? replay = null)
    {
        return new SfmlPlaySession().Run(simulation, maxFrames, display, replay);
    }
}
