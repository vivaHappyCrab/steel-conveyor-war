using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);

var configDirectory = ResolveConfigDirectory();
if (!Directory.Exists(configDirectory))
{
    throw new InvalidOperationException($"Config directory not found. Looked under '{configDirectory}'.");
}

var gameConfigPath = Path.Combine(configDirectory, "game.json");
if (!File.Exists(gameConfigPath))
{
    throw new FileNotFoundException("Required game settings file is missing.", gameConfigPath);
}

var gameSettings = GameSettingsLoader.Parse(File.ReadAllText(gameConfigPath));
var catalog = LoadRequiredJson(configDirectory, gameSettings.ResearchContentFile, ResearchContentLoader.Parse, "research catalog");
var tiles = LoadRequiredJson(configDirectory, "tiles.json", TileContentLoader.Parse, "tile catalog");
var entities = LoadRequiredJson(configDirectory, "entities.json", EntityContentLoader.Parse, "entity catalog");
var map = LoadRequiredJson(configDirectory, gameSettings.MapContentFile, MapSettingsLoader.Parse, "map settings");

var options = new GameCreationOptions(
    gameSettings.DefaultRandomSeed,
    gameSettings.ResearchProfileId,
    catalog,
    tiles,
    entities,
    map);

var simulation = GameSimulation.CreateNewGame(options);
var display = new SfmlDisplayOptions(
    gameSettings.Window.Width,
    gameSettings.Window.Height,
    gameSettings.Window.Title);
new SfmlGameRunner().Run(simulation, smokeTest ? 3 : null, display);

static string ResolveConfigDirectory()
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "config"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config")),
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config"))
    };

    foreach (var candidate in candidates)
    {
        if (Directory.Exists(candidate))
        {
            return candidate;
        }
    }

    return Path.Combine(AppContext.BaseDirectory, "config");
}

static T LoadRequiredJson<T>(string configDirectory, string fileName, Func<string, T> parse, string label)
{
    var path = Path.Combine(configDirectory, fileName);
    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"Required {label} file is missing.", path);
    }

    return parse(File.ReadAllText(path));
}
