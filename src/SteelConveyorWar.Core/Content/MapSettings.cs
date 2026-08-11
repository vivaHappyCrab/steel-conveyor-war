namespace SteelConveyorWar.Core;

/// <summary>
/// Static per-match roster from map config. Alliances (TeamId) do not change mid-match in MVP.
/// Start tiles are optional in JSON/tests; missing seats for the classic 1v1 ids resolve to the
/// historical CreateStartingEntities layout for the default world size.
/// </summary>
public sealed record MapPlayerDefinition(
    int Id,
    string Name,
    int TeamId,
    TilePosition? StartCommander = null,
    TilePosition? StartBastion = null,
    TilePosition? StartHub = null,
    string? Color = null)
{
    public const string DefaultColorPlayerOne = "#46BAFF";
    public const string DefaultColorPlayerTwo = "#DC4646";
    public const string DefaultColorOther = "#C8C8C8";

    /// <summary>
    /// World size used by <see cref="GameSimulation.CreateNewGame"/> until maps encode size.
    /// Default 1v1 start coords in <c>config/maps/default.json</c> are authored against this grid.
    /// </summary>
    public const int DefaultWorldWidth = 192;
    public const int DefaultWorldHeight = 112;

    public static string DefaultColorForPlayer(int playerId) => playerId switch
    {
        1 => DefaultColorPlayerOne,
        2 => DefaultColorPlayerTwo,
        _ => DefaultColorOther
    };

    /// <summary>
    /// Resolves start tiles: explicit values win; otherwise the classic 1v1 geometry for seats 1/2.
    /// </summary>
    public (TilePosition Commander, TilePosition Bastion, TilePosition Hub) ResolveStartPositions(WorldSize worldSize)
    {
        if (StartCommander is not null && StartBastion is not null && StartHub is not null)
        {
            return (StartCommander.Value, StartBastion.Value, StartHub.Value);
        }

        if (StartCommander is not null || StartBastion is not null || StartHub is not null)
        {
            throw new InvalidOperationException(
                $"Map player '{Id}' must declare all of startCommander, startBastion, and startHub, or omit all three.");
        }

        var midY = worldSize.Height / 2;
        return Id switch
        {
            1 => (
                new TilePosition(4, midY),
                new TilePosition(1, midY),
                new TilePosition(5, midY + 2)),
            2 => (
                new TilePosition(worldSize.Width - 5, midY),
                new TilePosition(worldSize.Width - 4, midY),
                new TilePosition(worldSize.Width - 6, midY + 2)),
            _ => throw new InvalidOperationException(
                $"Map player '{Id}' is missing startCommander/startBastion/startHub (defaults exist only for seats 1 and 2).")
        };
    }
}

/// <summary>
/// Map bootstrap metadata (players/teams). Terrain is still seed-generated — this is not a terrain blob.
/// </summary>
public sealed record MapSettings(
    int SchemaVersion,
    string MapId,
    IReadOnlyList<MapPlayerDefinition> Players)
{
    public static MapSettings Default1v1 { get; } = CreateDefault1v1();

    private static MapSettings CreateDefault1v1()
    {
        const int midY = MapPlayerDefinition.DefaultWorldHeight / 2;
        const int width = MapPlayerDefinition.DefaultWorldWidth;
        return new(
            SchemaVersion: 1,
            MapId: "default",
            Players:
            [
                new MapPlayerDefinition(
                    1,
                    "Blue",
                    1,
                    new TilePosition(4, midY),
                    new TilePosition(1, midY),
                    new TilePosition(5, midY + 2),
                    MapPlayerDefinition.DefaultColorPlayerOne),
                new MapPlayerDefinition(
                    2,
                    "Red",
                    2,
                    new TilePosition(width - 5, midY),
                    new TilePosition(width - 4, midY),
                    new TilePosition(width - 6, midY + 2),
                    MapPlayerDefinition.DefaultColorPlayerTwo)
            ]);
    }
}
