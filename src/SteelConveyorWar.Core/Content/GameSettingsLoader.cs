using System.Text.Json;

namespace SteelConveyorWar.Core;

public static class GameSettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static GameSettings Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<GameConfigDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Game settings JSON deserialized to null.");

        if (dto.SchemaVersion < 1)
        {
            throw new InvalidOperationException($"Unsupported game schemaVersion '{dto.SchemaVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(dto.GameId))
        {
            throw new InvalidOperationException("game.json is missing gameId.");
        }

        var ticksPerSecond = ResolveTicksPerSecond(dto.Simulation?.TicksPerSecond);

        return new GameSettings(
            dto.SchemaVersion,
            dto.GameId,
            string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.GameId : dto.DisplayName,
            ticksPerSecond,
            dto.Simulation?.DefaultRandomSeed ?? GameSettings.Default.DefaultRandomSeed,
            string.IsNullOrWhiteSpace(dto.Research?.Content) ? GameSettings.Default.ResearchContentFile : dto.Research.Content,
            string.IsNullOrWhiteSpace(dto.Research?.Profile) ? GameSettings.Default.ResearchProfileId : dto.Research.Profile,
            string.IsNullOrWhiteSpace(dto.Map?.Content) ? GameSettings.Default.MapContentFile : dto.Map.Content,
            string.IsNullOrWhiteSpace(dto.BuildCosts?.Content) ? GameSettings.Default.BuildCostsContentFile : dto.BuildCosts.Content);
    }

    private static int ResolveTicksPerSecond(int? configured)
    {
        if (configured is null or 0)
        {
            return GameSettings.Default.TicksPerSecond;
        }

        if (configured < 0)
        {
            throw new InvalidOperationException(
                $"simulation.ticksPerSecond must be a positive integer (got '{configured}').");
        }

        return configured.Value;
    }

    private sealed class GameConfigDto
    {
        public int SchemaVersion { get; set; }
        public string GameId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public SimulationConfigDto? Simulation { get; set; }
        public ResearchConfigDto? Research { get; set; }
        public MapConfigRefDto? Map { get; set; }
        public BuildCostsConfigRefDto? BuildCosts { get; set; }
    }

    private sealed class SimulationConfigDto
    {
        public int TicksPerSecond { get; set; }
        public int DefaultRandomSeed { get; set; } = 42;
    }

    private sealed class ResearchConfigDto
    {
        public string Content { get; set; } = "research.json";
        public string Profile { get; set; } = ResearchProfileIds.MvpB;
    }

    private sealed class MapConfigRefDto
    {
        public string Content { get; set; } = "maps/default.json";
    }

    private sealed class BuildCostsConfigRefDto
    {
        public string Content { get; set; } = "build-costs.json";
    }
}
