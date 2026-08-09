using System.Text.Json;

namespace SteelConveyorWar.Core;

public static class MapSettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static MapSettings Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<MapConfigDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Map settings JSON deserialized to null.");

        if (dto.SchemaVersion < 1)
        {
            throw new InvalidOperationException($"Unsupported map schemaVersion '{dto.SchemaVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(dto.MapId))
        {
            throw new InvalidOperationException("Map config is missing mapId.");
        }

        if (dto.Players is null || dto.Players.Count == 0)
        {
            throw new InvalidOperationException("Map config must declare at least one player.");
        }

        var players = new List<MapPlayerDefinition>(dto.Players.Count);
        var seenIds = new HashSet<int>();
        foreach (var player in dto.Players)
        {
            if (player.Id <= 0)
            {
                throw new InvalidOperationException("Map player id must be a positive integer.");
            }

            if (!seenIds.Add(player.Id))
            {
                throw new InvalidOperationException($"Duplicate map player id '{player.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(player.Name))
            {
                throw new InvalidOperationException($"Map player '{player.Id}' is missing a name.");
            }

            if (player.TeamId <= 0)
            {
                throw new InvalidOperationException($"Map player '{player.Id}' must declare a positive teamId.");
            }

            players.Add(new MapPlayerDefinition(player.Id, player.Name.Trim(), player.TeamId));
        }

        return new MapSettings(dto.SchemaVersion, dto.MapId.Trim(), players);
    }

    private sealed class MapConfigDto
    {
        public int SchemaVersion { get; set; }
        public string MapId { get; set; } = "";
        public List<MapPlayerDto>? Players { get; set; }
    }

    private sealed class MapPlayerDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int TeamId { get; set; }
    }
}
