namespace SteelConveyorWar.Core;

public sealed record GameSettings(
    int SchemaVersion,
    string GameId,
    string DisplayName,
    int TicksPerSecond,
    int DefaultRandomSeed,
    string ResearchContentFile,
    string ResearchProfileId,
    string MapContentFile,
    string BuildCostsContentFile,
    string GameplayTablesContentFile)
{
    public static GameSettings Default { get; } = new(
        SchemaVersion: 1,
        GameId: "steel-conveyor-war",
        DisplayName: "Steel Conveyor War",
        TicksPerSecond: 30,
        DefaultRandomSeed: 42,
        ResearchContentFile: "research.json",
        ResearchProfileId: ResearchProfileIds.MvpB,
        MapContentFile: "maps/default.json",
        BuildCostsContentFile: "build-costs.json",
        GameplayTablesContentFile: "gameplay-tables.json");
}
