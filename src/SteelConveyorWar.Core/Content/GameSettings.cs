namespace SteelConveyorWar.Core;

public sealed record WindowSettings(
    uint Width,
    uint Height,
    string Title)
{
    public static WindowSettings Default { get; } = new(1392, 720, "Steel Conveyor War");
}

public sealed record GameSettings(
    int SchemaVersion,
    string GameId,
    string DisplayName,
    int TicksPerSecond,
    int DefaultRandomSeed,
    string ResearchContentFile,
    string ResearchProfileId,
    WindowSettings Window)
{
    public static GameSettings Default { get; } = new(
        SchemaVersion: 1,
        GameId: "steel-conveyor-war",
        DisplayName: "Steel Conveyor War",
        TicksPerSecond: 30,
        DefaultRandomSeed: 42,
        ResearchContentFile: "research.json",
        ResearchProfileId: ResearchProfileIds.MvpB,
        Window: WindowSettings.Default);
}
