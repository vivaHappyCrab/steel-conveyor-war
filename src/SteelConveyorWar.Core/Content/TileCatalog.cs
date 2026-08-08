namespace SteelConveyorWar.Core;

public sealed record TileDefinition(
    string Id,
    string Name,
    bool Walkable,
    string? Resource);

public sealed record TileCatalog(
    int SchemaVersion,
    IReadOnlyDictionary<string, TileDefinition> Tiles)
{
    public static TileCatalog Empty { get; } = new(1, new Dictionary<string, TileDefinition>());
}
