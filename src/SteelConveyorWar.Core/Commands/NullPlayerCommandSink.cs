namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// Spectator/no-op sink: accepts enqueue calls and ignores them so UI mappers can stay wired
/// during replay watch without mutating the simulation.
/// </summary>
public sealed class NullPlayerCommandSink : IPlayerCommandSink
{
    public int InputDelayTicks => DeferredCommandSink.DefaultInputDelayTicks;

    public IReadOnlyList<ISimulationCommand> CommandLog { get; } = Array.Empty<ISimulationCommand>();

    public ISimulationCommand Enqueue(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command;
    }
}
