namespace SteelConveyorWar.Sfml;

public sealed record SfmlDisplayOptions(uint Width, uint Height, string Title)
{
    public static SfmlDisplayOptions Default { get; } = new(1392, 720, "Steel Conveyor War");
}
