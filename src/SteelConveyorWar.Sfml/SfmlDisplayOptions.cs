using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public sealed record SfmlDisplayOptions(uint Width, uint Height, string Title, PlayerId LocalPlayerId)
{
    public static PlayerId DefaultLocalPlayerId { get; } = new(1);

    public static SfmlDisplayOptions Default { get; } = new(1392, 720, "Steel Conveyor War", DefaultLocalPlayerId);

    public SfmlDisplayOptions(uint width, uint height, string title)
        : this(width, height, title, DefaultLocalPlayerId)
    {
    }
}
