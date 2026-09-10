using SteelConveyorWar.Client;
using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Replay;
using SteelConveyorWar.Hosting;
using SteelConveyorWar.Sfml;

var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
var localPlayerId = LocalPlayerBinding.Resolve(args);
var replayCli = ReplayHostOptions.Parse(args);

// R25/M09: shared Hosting bootstrap owns file I/O; Core stays parse/validate only.
var content = ContentBootstrap.Load(ContentBootstrap.ResolveConfigDirectory());

GameSimulation simulation;
ReplayWatchSession? replayWatch = null;
var displayTicks = content.Settings.TicksPerSecond;

if (replayCli.ReplayPath is not null)
{
    var document = ReplayFileStore.Load(replayCli.ReplayPath);
    simulation = ReplayPlayback.CreateReady(content.CreationOptions, document);
    replayWatch = new ReplayWatchSession(document, content.CreationOptions);
    displayTicks = document.TicksPerSecond;
}
else
{
    simulation = GameSimulation.CreateNewGame(content.CreationOptions);
}

var display = HostDisplayOptionsLoader.Parse(content.GameJson, displayTicks, localPlayerId);
var result = new SfmlGameRunner().Run(simulation, smokeTest ? 3 : null, display, replayWatch);

if (replayCli.ShouldRecord)
{
    var document = ReplayDocument.Capture(result.Simulation);
    var path = replayCli.ResolveRecordPath(document);
    ReplayFileStore.Save(path, document);
    Console.WriteLine($"replay written path={path}");
}

return 0;
