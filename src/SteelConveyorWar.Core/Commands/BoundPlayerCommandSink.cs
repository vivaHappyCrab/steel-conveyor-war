namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// H03: session-bound command ingress. Overwrites are not used — a mismatched
/// <see cref="ISimulationCommand.Actor"/> is rejected before the inner sink runs, so hosts never
/// trust a client-claimed identity for an authenticated seat.
/// </summary>
public sealed class BoundPlayerCommandSink : IPlayerCommandSink
{
    private readonly IPlayerCommandSink _inner;

    public BoundPlayerCommandSink(IPlayerCommandSink inner, PlayerId boundActor)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        BoundActor = boundActor;
    }

    public PlayerId BoundActor { get; }

    public int InputDelayTicks => _inner.InputDelayTicks;

    public IReadOnlyList<ISimulationCommand> CommandLog => _inner.CommandLog;

    public ISimulationCommand Enqueue(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor != BoundActor)
        {
            throw new InvalidOperationException(
                $"Command actor {command.Actor.Value} does not match bound session actor {BoundActor.Value}.");
        }

        return _inner.Enqueue(command);
    }
}
