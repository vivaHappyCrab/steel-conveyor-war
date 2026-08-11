namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// R11: outcome of applying a single command. Replaces the bare <c>bool</c> so the tick loop can record
/// <em>why</em> a command was rejected instead of silently dropping it. Presentation-only: rejection
/// reasons are diagnostics and are not part of the determinism hash.
/// </summary>
public readonly record struct CommandResult(bool Accepted, string? RejectionReason)
{
    public static CommandResult Ok { get; } = new(true, null);

    public static CommandResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// R11: a diagnostic record of a command the simulation refused to apply this tick (handler returned
/// false, threw, or the kind was unknown). Surfaced via <see cref="GameSimulation.LastTickCommandRejections"/>
/// for hosts/tests; not hashed.
/// </summary>
public readonly record struct CommandRejection(
    SimulationCommandKind Kind,
    PlayerId Actor,
    long Sequence,
    string Reason);
