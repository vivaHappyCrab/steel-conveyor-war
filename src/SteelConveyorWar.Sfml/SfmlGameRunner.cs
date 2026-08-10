using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// Thin SFML host entry: delegates window/input/render/UI composition to <see cref="SfmlPlaySession"/>.
/// No gameplay rules live in the SFML adapter.
/// </summary>
public sealed class SfmlGameRunner
{
    public void Run(GameSimulation simulation, int? maxFrames = null, SfmlDisplayOptions? display = null)
    {
        new SfmlPlaySession().Run(simulation, maxFrames, display);
    }
}
