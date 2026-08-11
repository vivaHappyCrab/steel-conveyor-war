namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// R02: the single input funnel for a host. Instead of calling immediate <c>Try*</c> mutators,
/// hosts (SFML, headless, future net clients) build a command DTO and hand it to a sink. The sink
/// assigns the scheduling metadata (a future tick + a monotonic per-author sequence) and enqueues it
/// on the simulation so all local input flows through the same replayable, tick-scheduled path as
/// remote input. Never mutates the simulation synchronously.
/// </summary>
public interface IPlayerCommandSink
{
    /// <summary>How many ticks ahead of the current simulation tick enqueued commands are scheduled.</summary>
    int InputDelayTicks { get; }

    /// <summary>
    /// Ordered log of every command this sink scheduled, with its final (Tick, Sequence). Replaying
    /// this log against a fresh simulation with the same seed reproduces the same state hash.
    /// </summary>
    IReadOnlyList<ISimulationCommand> CommandLog { get; }

    /// <summary>
    /// Schedules <paramref name="command"/> for a future tick and returns the re-stamped command that
    /// was actually enqueued (with its assigned <see cref="ISimulationCommand.Tick"/> and
    /// <see cref="ISimulationCommand.Sequence"/>). The incoming command's tick/sequence are ignored.
    /// </summary>
    ISimulationCommand Enqueue(ISimulationCommand command);
}
