namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// R02: default <see cref="IPlayerCommandSink"/>. Stamps each command with
/// <c>simulation.Tick + <see cref="InputDelayTicks"/></c> and a monotonic per-author sequence, then
/// enqueues it via <see cref="GameSimulation.EnqueueCommand"/>. This is the production input path:
/// local input becomes a tick-scheduled, replayable command log instead of an immediate mutation, so
/// the result no longer depends on which two ticks a window event happened to arrive between, and the
/// same log can be replayed to an identical state hash.
/// </summary>
public sealed class DeferredCommandSink : IPlayerCommandSink
{
    /// <summary>
    /// R02: one-tick input delay is the fixed local policy (commands land on the very next tick). A
    /// larger value models network input delay; see R32 for TPS/tick-assignment policy.
    /// </summary>
    public const int DefaultInputDelayTicks = 1;

    private readonly GameSimulation _simulation;
    private readonly Dictionary<int, long> _nextSequenceByActor = new();
    private readonly List<ISimulationCommand> _log = new();

    public DeferredCommandSink(GameSimulation simulation, int inputDelayTicks = DefaultInputDelayTicks)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (inputDelayTicks < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputDelayTicks),
                "Input delay must be at least one tick so commands land on a strictly future tick.");
        }

        _simulation = simulation;
        InputDelayTicks = inputDelayTicks;
    }

    public int InputDelayTicks { get; }

    public IReadOnlyList<ISimulationCommand> CommandLog => _log.AsReadOnly();

    public ISimulationCommand Enqueue(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is not SimulationCommandBase baseCommand)
        {
            throw new ArgumentException(
                $"Command '{command.GetType().Name}' must derive from {nameof(SimulationCommandBase)}.",
                nameof(command));
        }

        var targetTick = _simulation.Tick + InputDelayTicks;
        var sequence = NextSequence(command.Actor);
        var scheduled = baseCommand.WithScheduling(targetTick, sequence);
        _simulation.EnqueueCommand(scheduled);
        _log.Add(scheduled);
        return scheduled;
    }

    private long NextSequence(PlayerId actor)
    {
        // Sequence 0 is reserved for "unsequenced" (legacy/local); author sequences start at 1 so
        // every sink-issued command participates in canonical ordering and duplicate suppression (R03).
        var next = _nextSequenceByActor.TryGetValue(actor.Value, out var current) ? current + 1 : 1;
        _nextSequenceByActor[actor.Value] = next;
        return next;
    }
}
