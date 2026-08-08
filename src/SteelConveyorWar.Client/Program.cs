using System.Text.Json;
using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);

var configDirectory = ResolveConfigDirectory();
var gameConfigPath = Path.Combine(configDirectory, "game.json");
var gameConfig = LoadGameConfig(gameConfigPath);

var catalog = LoadResearchCatalog(configDirectory, gameConfig.Research?.Content);
var options = new GameCreationOptions(
    gameConfig.Simulation?.DefaultRandomSeed ?? 42,
    gameConfig.Research?.Profile ?? ResearchProfileIds.MvpB,
    catalog);

var simulation = GameSimulation.CreateNewGame(options);
new SfmlGameRunner().Run(simulation, smokeTest ? 3 : null);

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

static GameConfigDto LoadGameConfig(string path)
{
    if (!File.Exists(path))
    {
        return new GameConfigDto();
    }

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<GameConfigDto>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? new GameConfigDto();
}

static ResearchCatalog LoadResearchCatalog(string configDirectory, string? contentFile)
{
    var embedded = MvpResearchCatalog.CreateEmbedded();
    if (string.IsNullOrWhiteSpace(contentFile))
    {
        return embedded;
    }

    var path = Path.Combine(configDirectory, contentFile);
    if (!File.Exists(path))
    {
        return embedded;
    }

    var fromFile = ResearchContentLoader.Parse(File.ReadAllText(path));
    return fromFile.Technologies.Count >= embedded.Technologies.Count
        && fromFile.Profiles.Count >= embedded.Profiles.Count
            ? fromFile
            : embedded;
}

sealed class GameConfigDto
{
    public SimulationConfigDto? Simulation { get; set; }
    public ResearchConfigDto? Research { get; set; }
}

sealed class SimulationConfigDto
{
    public int DefaultRandomSeed { get; set; } = 42;
}

sealed class ResearchConfigDto
{
    public string Content { get; set; } = "research.json";
    public string Profile { get; set; } = ResearchProfileIds.MvpB;
}
