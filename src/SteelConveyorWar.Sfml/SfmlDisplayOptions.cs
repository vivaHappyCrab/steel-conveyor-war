using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public sealed class SfmlDisplayOptions
{
    public static PlayerId DefaultLocalPlayerId { get; } = new(1);

    public uint Width { get; }
    public uint Height { get; }
    public string Title { get; }
    public int TicksPerSecond { get; }
    public PlayerId LocalPlayerId { get; }

    public SfmlDisplayOptions(
        uint width,
        uint height,
        string title,
        int ticksPerSecond,
        PlayerId? localPlayerId = null)
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
        LocalPlayerId = localPlayerId ?? DefaultLocalPlayerId;
    }

    public static SfmlDisplayOptions Default { get; } = new(
        1392,
        720,
        "Steel Conveyor War",
        GameSimulation.TicksPerSecond,
        DefaultLocalPlayerId);
}
