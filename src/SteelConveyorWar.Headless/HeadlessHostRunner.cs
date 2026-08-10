using SteelConveyorWar.Core;

namespace SteelConveyorWar.Headless;

/// <summary>
/// Minimal bot/net host loop: optional command step then <see cref="GameSimulation.AdvanceTick"/>,
/// with no window or SFML dependency.
/// </summary>
public static class HeadlessHostRunner
{
    public static HeadlessHostResult Run(GameSimulation simulation, HeadlessHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(options);

        var playerId = new PlayerId(options.PlayerId);
        var commandsIssued = 0;

        for (var step = 0; step < options.Ticks; step++)
        {
            if (TryApplyStubAiStep(simulation, playerId))
            {
                commandsIssued++;
            }

            simulation.AdvanceTick();
        }

        return new HeadlessHostResult(
            simulation.Tick,
            simulation.ComputeStateHash(),
            commandsIssued,
            simulation.Status,
            simulation.WinnerId);
    }

    /// <summary>
    /// Placeholder "AI" step for harness/CI: re-issue a short commander move when idle.
    /// Real bot policy belongs in a follow-up (see issue #60).
    /// </summary>
    internal static bool TryApplyStubAiStep(GameSimulation simulation, PlayerId playerId)
    {
        var commander = simulation.World.Entities.FirstOrDefault(entity =>
            entity.IsAlive
            && entity.Kind == EntityKind.Commander
            && entity.OwnerId == playerId);

        if (commander is null)
        {
            return false;
        }

        if (commander.MoveTarget is not null || commander.QueuedBuildOrder is not null || commander.QueuedDemolishOrder is not null)
        {
            return false;
        }

        var target = new TilePosition(commander.Position.X, Math.Max(0, commander.Position.Y - 2));
        if (target == commander.Position || !simulation.World.IsInside(target))
        {
            return false;
        }

        return simulation.TryIssueMoveCommand(commander.Id, playerId, target);
    }
}

public sealed record HeadlessHostResult(
    long Tick,
    string StateHash,
    int CommandsIssued,
    GameStatus Status,
    PlayerId? WinnerId);
