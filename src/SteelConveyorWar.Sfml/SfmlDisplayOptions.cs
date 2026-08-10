using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public sealed record SfmlDisplayOptions
{
    public uint Width { get; }
    public uint Height { get; }
    public string Title { get; }
    public int TicksPerSecond { get; }

    public SfmlDisplayOptions(uint width, uint height, string title, int ticksPerSecond)
    {
        if (ticksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticksPerSecond),
                ticksPerSecond,
                "Host ticksPerSecond must be a positive integer.");
        }

        Width = width;
        Height = height;
        Title = title;
        TicksPerSecond = ticksPerSecond;
    }

    public static SfmlDisplayOptions Default { get; } = new(
        1392,
        720,
        "Steel Conveyor War",
        GameSimulation.TicksPerSecond);
}
