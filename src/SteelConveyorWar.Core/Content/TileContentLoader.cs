using System.Text.Json;

namespace SteelConveyorWar.Core;

public static class TileContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static TileCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<TileCatalogDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Tile catalog JSON deserialized to null.");

        ContentSchema.RequireSupportedVersion("tiles", dto.SchemaVersion);

        if (dto.Tiles is null || dto.Tiles.Count == 0)
        {
            throw new InvalidOperationException("Tile catalog must declare at least one tile.");
        }

        var tiles = new Dictionary<string, TileDefinition>(StringComparer.Ordinal);
        foreach (var tile in dto.Tiles)
        {
            if (string.IsNullOrWhiteSpace(tile.Id))
            {
                throw new InvalidOperationException("Tile entry is missing an id.");
            }

            if (string.IsNullOrWhiteSpace(tile.Name))
            {
                throw new InvalidOperationException($"Tile '{tile.Id}' is missing a name.");
            }

            if (!tiles.TryAdd(tile.Id, new TileDefinition(tile.Id, tile.Name, tile.Walkable, tile.Resource)))
            {
                throw new InvalidOperationException($"Duplicate tile id '{tile.Id}'.");
            }
        }

        return new TileCatalog(dto.SchemaVersion, tiles);
    }

    private sealed class TileCatalogDto
    {
        public int SchemaVersion { get; set; }
        public List<TileDto>? Tiles { get; set; }
    }

    private sealed class TileDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Walkable { get; set; }
        public string? Resource { get; set; }
    }
}
