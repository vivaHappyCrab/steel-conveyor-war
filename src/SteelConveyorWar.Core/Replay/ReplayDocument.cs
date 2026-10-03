using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Replay;

/// <summary>
/// Durable command-log replay: session identity plus the stamped commands needed to recreate a match.
/// Catalogs are not embedded — playback requires the same content on disk.
/// </summary>
public sealed record ReplayDocument(
    int FormatVersion,
    int ProtocolVersion,
    int AlgorithmVersion,
    int InputDelayTicks,
    long DurationTicks,
    string FinalStateHash,
    string ContentManifest,
    string SessionManifest,
    int Seed,
    int TicksPerSecond,
    string ProfileId,
    string MapId,
    IReadOnlyList<ISimulationCommand> Commands)
{
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Upper bound for <see cref="DurationTicks"/> on load. Blocks pathological files from
    /// spinning <see cref="ReplayPlayback.PlayToEnd"/> for effectively forever while still
    /// InProgress (~92h at 30 TPS).
    /// </summary>
    public const long MaxDurationTicks = 10_000_000;

    public static ReplayDocument Capture(
        GameSimulation simulation,
        int inputDelayTicks = DeferredCommandSink.DefaultInputDelayTicks)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (inputDelayTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(inputDelayTicks), "Input delay must be at least one tick.");
        }

        return new ReplayDocument(
            CurrentFormatVersion,
            SimulationCommandSerializer.ProtocolVersion,
            SimulationStateHasher.AlgorithmVersion,
            inputDelayTicks,
            simulation.Tick,
            simulation.ComputeStateHash(),
            SimulationContentManifest.Compute(simulation),
            SimulationSessionManifest.Compute(simulation),
            simulation.RandomSeed,
            simulation.TicksPerSecond,
            simulation.ResearchProfile.Id,
            simulation.Map.MapId,
            simulation.RecordedCommands.ToArray());
    }
}
