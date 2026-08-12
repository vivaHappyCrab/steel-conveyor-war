using SteelConveyorWar.Client;
using SteelConveyorWar.Core;
using SteelConveyorWar.Hosting;
using SteelConveyorWar.Sfml;

var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
var localPlayerId = LocalPlayerBinding.Resolve(args);

// R25/M09: shared Hosting bootstrap owns file I/O; Core stays parse/validate only.
var content = ContentBootstrap.Load(ContentBootstrap.ResolveConfigDirectory());

var simulation = GameSimulation.CreateNewGame(content.CreationOptions);
var display = HostDisplayOptionsLoader.Parse(content.GameJson, content.Settings.TicksPerSecond, localPlayerId);
new SfmlGameRunner().Run(simulation, smokeTest ? 3 : null, display);
