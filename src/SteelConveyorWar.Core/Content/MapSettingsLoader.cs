using System.Text.Json;

namespace SteelConveyorWar.Core;

public static class MapSettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = ContentJsonOptions.CreateStrict();

    public static MapSettings Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<MapConfigDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Map settings JSON deserialized to null.");

        // Additive start/color fields remain compatible with schemaVersion 1.
        ContentSchema.RequireSupportedVersion("map", dto.SchemaVersion);

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

            var startCommander = ParseOptionalTile(player.StartCommander, player.Id, "startCommander");
            var startBastion = ParseOptionalTile(player.StartBastion, player.Id, "startBastion");
            var startHub = ParseOptionalTile(player.StartHub, player.Id, "startHub");
            var color = string.IsNullOrWhiteSpace(player.Color)
                ? MapPlayerDefinition.DefaultColorForPlayer(player.Id)
                : NormalizeColor(player.Color, player.Id);

            // Validate partial/complete start triples early; ResolveStartPositions also guards at spawn.
            if (startCommander is not null || startBastion is not null || startHub is not null)
            {
                if (startCommander is null || startBastion is null || startHub is null)
                {
                    throw new InvalidOperationException(
                        $"Map player '{player.Id}' must declare all of startCommander, startBastion, and startHub, or omit all three.");
                }

                EnsureInsideDefaultWorld(startCommander.Value, player.Id, "startCommander");
                EnsureInsideDefaultWorld(startBastion.Value, player.Id, "startBastion");
                EnsureInsideDefaultWorld(startHub.Value, player.Id, "startHub");
            }
            else
            {
                // Omit → classic 1v1 defaults for seats 1/2 (authoring against default world size).
                var worldSize = new WorldSize(
                    MapPlayerDefinition.DefaultWorldWidth,
                    MapPlayerDefinition.DefaultWorldHeight);
                var resolved = new MapPlayerDefinition(player.Id, player.Name.Trim(), player.TeamId)
                    .ResolveStartPositions(worldSize);
                startCommander = resolved.Commander;
                startBastion = resolved.Bastion;
                startHub = resolved.Hub;
            }

            players.Add(new MapPlayerDefinition(
                player.Id,
                player.Name.Trim(),
                player.TeamId,
                startCommander,
                startBastion,
                startHub,
                color));
        }

        return new MapSettings(dto.SchemaVersion, dto.MapId.Trim(), players);
    }

    private static TilePosition? ParseOptionalTile(TileDto? dto, int playerId, string fieldName)
    {
        if (dto is null)
        {
            return null;
        }

        return new TilePosition(dto.X, dto.Y);
    }

    private static void EnsureInsideDefaultWorld(TilePosition tile, int playerId, string fieldName)
    {
        var world = new WorldSize(
            MapPlayerDefinition.DefaultWorldWidth,
            MapPlayerDefinition.DefaultWorldHeight);
        if (tile.X < 0 || tile.Y < 0 || tile.X >= world.Width || tile.Y >= world.Height)
        {
            throw new InvalidOperationException(
                $"Map player '{playerId}' {fieldName} ({tile.X},{tile.Y}) is outside world " +
                $"{world.Width}x{world.Height}.");
        }
    }

    private static string NormalizeColor(string color, int playerId)
    {
        var trimmed = color.Trim();
        if (trimmed.Length == 7 && trimmed[0] == '#')
        {
            for (var i = 1; i < 7; i++)
            {
                if (!char.IsAsciiHexDigit(trimmed[i]))
                {
                    throw new InvalidOperationException(
                        $"Map player '{playerId}' color must be a #RRGGBB hex string.");
                }
            }

            return trimmed.ToUpperInvariant();
        }

        throw new InvalidOperationException(
            $"Map player '{playerId}' color must be a #RRGGBB hex string.");
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
        public TileDto? StartCommander { get; set; }
        public TileDto? StartBastion { get; set; }
        public TileDto? StartHub { get; set; }
        public string? Color { get; set; }
    }

    private sealed class TileDto
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
