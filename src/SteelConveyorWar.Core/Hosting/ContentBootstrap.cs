namespace SteelConveyorWar.Core;

/// <summary>
/// R25: single shared content bootstrap for every host (Client, Headless). Owns config-path
/// resolution, file I/O, catalog loading, cross-catalog validation, and <see cref="GameCreationOptions"/>
/// assembly, so adding a new required catalog is a one-place change and hosts cannot drift.
/// Host-specific concerns (window/render vs headless loop) stay in each host's entry point.
/// </summary>
public static class ContentBootstrap
{
    /// <summary>
    /// Resolves the config directory from the usual candidate locations. Pure over its inputs
    /// (base/current directories) so it can be unit-tested without touching <see cref="AppContext"/>.
    /// </summary>
    public static string ResolveConfigDirectory(string baseDirectory, string currentDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "config"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "config")),
            Path.GetFullPath(Path.Combine(currentDirectory, "config"))
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(baseDirectory, "config");
    }

    /// <summary>Convenience overload using the running host's base and current directories.</summary>
    public static string ResolveConfigDirectory() =>
        ResolveConfigDirectory(AppContext.BaseDirectory, Directory.GetCurrentDirectory());

    /// <summary>
    /// Loads and validates every catalog from <paramref name="configDirectory"/> and assembles the
    /// creation options. Throws a descriptive error if the directory or any required file is missing,
    /// or if inter-catalog references / schema versions are inconsistent (fail-fast, all-or-nothing).
    /// </summary>
    public static LoadedGameContent Load(string configDirectory)
    {
        if (!Directory.Exists(configDirectory))
        {
            throw new InvalidOperationException($"Config directory not found. Looked under '{configDirectory}'.");
        }

        var gameConfigPath = Path.Combine(configDirectory, "game.json");
        if (!File.Exists(gameConfigPath))
        {
            throw new FileNotFoundException("Required game settings file is missing.", gameConfigPath);
        }

        var gameJson = File.ReadAllText(gameConfigPath);
        var gameSettings = GameSettingsLoader.Parse(gameJson);
        var catalog = LoadRequiredJson(configDirectory, gameSettings.ResearchContentFile, ResearchContentLoader.Parse, "research catalog");
        var tiles = LoadRequiredJson(configDirectory, "tiles.json", TileContentLoader.Parse, "tile catalog");
        var entities = LoadRequiredJson(configDirectory, "entities.json", EntityContentLoader.Parse, "entity catalog");
        var map = LoadRequiredJson(configDirectory, gameSettings.MapContentFile, MapSettingsLoader.Parse, "map settings");
        var buildCosts = LoadRequiredJson(configDirectory, gameSettings.BuildCostsContentFile, BuildCostContentLoader.Parse, "build-cost catalog");

        // R34: reject dangling inter-catalog references before creating the match (fail-fast, all-or-nothing).
        ContentCrossValidator.Validate(catalog, buildCosts, entities, tiles);

        var creation = new GameCreationOptions(
            gameSettings.DefaultRandomSeed,
            gameSettings.ResearchProfileId,
            catalog,
            tiles,
            entities,
            map,
            buildCosts,
            gameSettings.TicksPerSecond);

        return new LoadedGameContent(gameJson, gameSettings, creation);
    }

    private static T LoadRequiredJson<T>(string configDirectory, string fileName, Func<string, T> parse, string label)
    {
        var path = Path.Combine(configDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required {label} file is missing.", path);
        }

        return parse(File.ReadAllText(path));
    }
}

/// <summary>
/// Result of <see cref="ContentBootstrap.Load"/>: the raw game.json (hosts still parse host-only
/// display options from it), the parsed <see cref="GameSettings"/>, and the ready-to-use
/// <see cref="GameCreationOptions"/> with all catalogs loaded and cross-validated.
/// </summary>
public sealed record LoadedGameContent(
    string GameJson,
    GameSettings Settings,
    GameCreationOptions CreationOptions);
