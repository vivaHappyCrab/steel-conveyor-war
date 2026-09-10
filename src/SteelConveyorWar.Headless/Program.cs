using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Replay;
using SteelConveyorWar.Headless;
using SteelConveyorWar.Hosting;

var options = HeadlessHostOptions.Parse(args);

// R25/M09: shared Hosting bootstrap owns file I/O; Core stays parse/validate only.
var content = ContentBootstrap.Load(ContentBootstrap.ResolveConfigDirectory());

if (options.Replay.ReplayPath is not null)
{
    var document = ReplayFileStore.Load(options.Replay.ReplayPath);
    var simulation = ReplayPlayback.CreateReady(content.CreationOptions, document);
    var hash = ReplayPlayback.PlayToEnd(simulation, document.DurationTicks);
    if (simulation.Tick != document.DurationTicks)
    {
        Console.Error.WriteLine(
            $"headless replay truncated ticks={simulation.Tick} expectedDuration={document.DurationTicks} status={simulation.Status}");
        return 1;
    }

    if (!string.Equals(hash, document.FinalStateHash, StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"headless replay hash mismatch ticks={simulation.Tick} expected={document.FinalStateHash} actual={hash}");
        return 1;
    }

    Console.WriteLine(
        $"headless replay ok ticks={simulation.Tick} status={simulation.Status} winner={(simulation.WinnerId is null ? "none" : simulation.WinnerId.Value.Value.ToString())} hash={hash}");
    return 0;
}

var live = GameSimulation.CreateNewGame(content.CreationOptions);
var result = HeadlessHostRunner.Run(live, options);

if (options.Replay.ShouldRecord)
{
    var document = ReplayDocument.Capture(live);
    var path = options.Replay.ResolveRecordPath(document);
    ReplayFileStore.Save(path, document);
    Console.WriteLine($"replay written path={path}");
}

Console.WriteLine(
    $"headless ok ticks={result.Tick} commands={result.CommandsIssued} status={result.Status} winner={(result.WinnerId is null ? "none" : result.WinnerId.Value.Value.ToString())} hash={result.StateHash}");

return 0;
