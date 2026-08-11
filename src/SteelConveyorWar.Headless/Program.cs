using SteelConveyorWar.Core;
using SteelConveyorWar.Headless;

var options = HeadlessHostOptions.Parse(args);

// R25: shared bootstrap resolves config, loads + cross-validates all catalogs, builds creation options.
var content = ContentBootstrap.Load(ContentBootstrap.ResolveConfigDirectory());

var simulation = GameSimulation.CreateNewGame(content.CreationOptions);
var result = HeadlessHostRunner.Run(simulation, options);

Console.WriteLine(
    $"headless ok ticks={result.Tick} commands={result.CommandsIssued} status={result.Status} winner={(result.WinnerId is null ? "none" : result.WinnerId.Value.Value.ToString())} hash={result.StateHash}");

return 0;
