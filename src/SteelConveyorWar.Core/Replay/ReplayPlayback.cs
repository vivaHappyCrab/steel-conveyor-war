using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Replay;

/// <summary>
/// Recreates a match from a <see cref="ReplayDocument"/>: fail-fast compatibility, enqueue stamped
/// commands, then advance until <see cref="ReplayDocument.DurationTicks"/>.
/// </summary>
public static class ReplayPlayback
{
    public static GameCreationOptions ApplySessionIdentity(GameCreationOptions options, ReplayDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);
        return options with
        {
            RandomSeed = document.Seed,
            TicksPerSecond = document.TicksPerSecond,
            ProfileId = document.ProfileId
        };
    }

    public static GameSimulation CreateReady(GameCreationOptions catalogs, ReplayDocument document)
    {
        var simulation = GameSimulation.CreateNewGame(ApplySessionIdentity(catalogs, document));
        Attach(simulation, document);
        return simulation;
    }

    public static void Attach(GameSimulation simulation, ReplayDocument document)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(document);
        EnsureCompatible(simulation, document);
        foreach (var command in document.Commands)
        {
            simulation.EnqueueCommand(command);
        }
    }

    public static void EnsureCompatible(GameSimulation simulation, ReplayDocument document)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(document);

        if (document.FormatVersion != ReplayDocument.CurrentFormatVersion)
        {
            throw new ReplayCompatibilityException(
                $"Unsupported replay format version {document.FormatVersion}; this build reads {ReplayDocument.CurrentFormatVersion}.");
        }

        if (document.ProtocolVersion < SimulationCommandSerializer.MinSupportedProtocolVersion
            || document.ProtocolVersion > SimulationCommandSerializer.ProtocolVersion)
        {
            throw new ReplayCompatibilityException(
                $"Unsupported command protocol version {document.ProtocolVersion}; " +
                $"this build decodes [{SimulationCommandSerializer.MinSupportedProtocolVersion}, {SimulationCommandSerializer.ProtocolVersion}].");
        }

        if (document.AlgorithmVersion != SimulationStateHasher.AlgorithmVersion)
        {
            throw new ReplayCompatibilityException(
                $"Replay algorithm version {document.AlgorithmVersion} does not match this build ({SimulationStateHasher.AlgorithmVersion}).");
        }

        if (simulation.RandomSeed != document.Seed)
        {
            throw new ReplayCompatibilityException(
                $"Seed mismatch: live={simulation.RandomSeed} replay={document.Seed}.");
        }

        if (simulation.TicksPerSecond != document.TicksPerSecond)
        {
            throw new ReplayCompatibilityException(
                $"TicksPerSecond mismatch: live={simulation.TicksPerSecond} replay={document.TicksPerSecond}.");
        }

        if (!string.Equals(simulation.ResearchProfile.Id, document.ProfileId, StringComparison.Ordinal))
        {
            throw new ReplayCompatibilityException(
                $"Research profile mismatch: live={simulation.ResearchProfile.Id} replay={document.ProfileId}.");
        }

        if (!string.Equals(simulation.Map.MapId, document.MapId, StringComparison.Ordinal))
        {
            throw new ReplayCompatibilityException(
                $"Map id mismatch: live={simulation.Map.MapId} replay={document.MapId}.");
        }

        var content = SimulationContentManifest.Compute(simulation);
        if (!string.Equals(content, document.ContentManifest, StringComparison.Ordinal))
        {
            throw new ContentManifestMismatchException(content, document.ContentManifest);
        }

        var session = SimulationSessionManifest.Compute(simulation);
        if (!string.Equals(session, document.SessionManifest, StringComparison.Ordinal))
        {
            throw new SessionManifestMismatchException(session, document.SessionManifest);
        }
    }

    /// <summary>
    /// Advances until <paramref name="durationTicks"/> or until <see cref="GameSimulation.AdvanceTick"/>
    /// stops progressing (match already over).
    /// </summary>
    public static string PlayToEnd(GameSimulation simulation, long durationTicks)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (durationTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationTicks), "Duration ticks cannot be negative.");
        }

        while (simulation.Tick < durationTicks)
        {
            var tickBefore = simulation.Tick;
            simulation.AdvanceTick();
            if (simulation.Tick == tickBefore)
            {
                break;
            }
        }

        return simulation.ComputeStateHash();
    }
}
