using System.Text.Json;

namespace SteelConveyorWar.Core;

public static class BuildCostContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static BuildCostCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<BuildCostCatalogDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Build-cost catalog JSON deserialized to null.");

        if (dto.SchemaVersion < 1)
        {
            throw new InvalidOperationException($"Unsupported build-costs schemaVersion '{dto.SchemaVersion}'.");
        }

        if (dto.Builds is null || dto.Builds.Count == 0)
        {
            throw new InvalidOperationException("Build-cost catalog must declare at least one build entry.");
        }

        var costs = new Dictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>();
        var ticks = new Dictionary<EntityKind, int>();
        var requirements = new Dictionary<EntityKind, TechnologyId>();

        foreach (var entry in dto.Builds)
        {
            if (string.IsNullOrWhiteSpace(entry.Kind))
            {
                throw new InvalidOperationException("Build entry is missing a kind.");
            }

            if (!Enum.TryParse<EntityKind>(entry.Kind, ignoreCase: true, out var kind))
            {
                throw new InvalidOperationException($"Unknown build entity kind '{entry.Kind}'.");
            }

            if (!costs.TryAdd(kind, ParseCost(entry.Cost, kind)))
            {
                throw new InvalidOperationException($"Duplicate build kind '{kind}'.");
            }

            if (entry.BuildTicks <= 0)
            {
                throw new InvalidOperationException($"Build kind '{kind}' must have buildTicks > 0.");
            }

            ticks[kind] = entry.BuildTicks;

            if (!string.IsNullOrWhiteSpace(entry.RequiredTechnology))
            {
                requirements[kind] = new TechnologyId(entry.RequiredTechnology.Trim());
            }
        }

        return new BuildCostCatalog(dto.SchemaVersion, costs, ticks, requirements);
    }

    private static IReadOnlyDictionary<ItemId, int> ParseCost(Dictionary<string, int>? cost, EntityKind kind)
    {
        if (cost is null || cost.Count == 0)
        {
            throw new InvalidOperationException($"Build kind '{kind}' must declare a non-empty cost.");
        }

        var parsed = new Dictionary<ItemId, int>();
        foreach (var (itemName, amount) in cost)
        {
            if (!Enum.TryParse<ItemId>(itemName, ignoreCase: true, out var item))
            {
                throw new InvalidOperationException($"Build kind '{kind}' has unknown item '{itemName}'.");
            }

            if (amount <= 0)
            {
                throw new InvalidOperationException($"Build kind '{kind}' cost for '{item}' must be > 0.");
            }

            if (!parsed.TryAdd(item, amount))
            {
                throw new InvalidOperationException($"Build kind '{kind}' has duplicate item '{item}'.");
            }
        }

        return parsed;
    }

    private sealed class BuildCostCatalogDto
    {
        public int SchemaVersion { get; set; }
        public List<BuildEntryDto>? Builds { get; set; }
    }

    private sealed class BuildEntryDto
    {
        public string Kind { get; set; } = "";
        public Dictionary<string, int>? Cost { get; set; }
        public int BuildTicks { get; set; } = 30;
        public string? RequiredTechnology { get; set; }
    }
}
