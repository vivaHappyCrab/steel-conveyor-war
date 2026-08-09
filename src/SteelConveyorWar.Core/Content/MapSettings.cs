namespace SteelConveyorWar.Core;

/// <summary>
/// Static per-match roster from map config. Alliances (TeamId) do not change mid-match in MVP.
/// </summary>
public sealed record MapPlayerDefinition(int Id, string Name, int TeamId);

/// <summary>
/// Map bootstrap metadata (players/teams). Terrain is still seed-generated — this is not a terrain blob.
/// </summary>
public sealed record MapSettings(
    int SchemaVersion,
    string MapId,
    IReadOnlyList<MapPlayerDefinition> Players)
{
    public static MapSettings Default1v1 { get; } = new(
        SchemaVersion: 1,
        MapId: "default",
        Players:
        [
            new MapPlayerDefinition(1, "Blue", 1),
            new MapPlayerDefinition(2, "Red", 2)
        ]);
}
