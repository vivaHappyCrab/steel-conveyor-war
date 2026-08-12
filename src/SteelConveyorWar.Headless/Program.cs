using SteelConveyorWar.Core;
using SteelConveyorWar.Headless;
using SteelConveyorWar.Hosting;

var options = HeadlessHostOptions.Parse(args);

// R25/M09: shared Hosting bootstrap owns file I/O; Core stays parse/validate only.
var content = ContentBootstrap.Load(ContentBootstrap.ResolveConfigDirectory());

var simulation = GameSimulation.CreateNewGame(content.CreationOptions);
var result = HeadlessHostRunner.Run(simulation, options);

Console.WriteLine(
    $"headless ok ticks={result.Tick} commands={result.CommandsIssued} status={result.Status} winner={(result.WinnerId is null ? "none" : result.WinnerId.Value.Value.ToString())} hash={result.StateHash}");

return 0;
