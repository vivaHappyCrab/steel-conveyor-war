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
    // H07: freeze at construction / `with` so public graphs cannot be cast-mutated.
    public IReadOnlyDictionary<string, TileDefinition> Tiles
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value, StringComparer.Ordinal);
    } = ContentFreeze.Dictionary(Tiles, StringComparer.Ordinal);

    public static TileCatalog Empty { get; } = new(1, new Dictionary<string, TileDefinition>(StringComparer.Ordinal));
}
