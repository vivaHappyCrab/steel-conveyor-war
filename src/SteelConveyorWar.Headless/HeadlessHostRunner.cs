using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Headless;

/// <summary>
/// Minimal bot/net host loop: optional command step then <see cref="GameSimulation.AdvanceTick"/>,
/// with no window or SFML dependency.
/// R02: input flows only through an <see cref="IPlayerCommandSink"/> — the host never calls immediate
/// <c>Try*</c> mutators — so the run produces a replayable, tick-scheduled command log.
/// </summary>
public static class HeadlessHostRunner
{
    public static HeadlessHostResult Run(GameSimulation simulation, HeadlessHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(options);

        var playerId = new PlayerId(options.PlayerId);
        var sink = new BoundPlayerCommandSink(new DeferredCommandSink(simulation), playerId);
        // R19: the bot observes only through the fair observation contract — it never reads World/GetPlayer.
        var view = simulation.CreatePlayerView(playerId, PlayerObservationMode.Fair);
        var commandsIssued = 0;

        for (var step = 0; step < options.Ticks; step++)
        {
            if (TryApplyStubAiStep(view, sink, playerId))
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
            simulation.WinnerId,
            sink.CommandLog);
    }

    /// <summary>
    /// Placeholder "AI" step for harness/CI: re-issue a short commander move when idle.
    /// Real bot policy belongs in a follow-up (see issue #60).
    /// R02: enqueues an <see cref="IssueMoveCommand"/> through <paramref name="sink"/> instead of
    /// mutating the simulation immediately.
    /// R19: reads exclusively through the fair <see cref="IPlayerView"/> observation contract — it does
    /// not touch <c>GameSimulation.World</c>/<c>GetPlayer</c>, so the stub can never cheat past fog.
    /// </summary>
    internal static bool TryApplyStubAiStep(IPlayerView view, IPlayerCommandSink sink, PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(sink);

        var commander = view.GetVisibleEntities()
            .FirstOrDefault(entity => entity.IsOwn && entity.Kind == EntityKind.Commander);

        if (commander is null)
        {
            return false;
        }

        var own = commander.Own;
        if (own is null || own.MoveTarget is not null || own.QueuedBuildOrder is not null || own.QueuedDemolishOrder is not null)
        {
            return false;
        }

        var target = new TilePosition(commander.Position.X, Math.Max(0, commander.Position.Y - 2));
        if (target == commander.Position || !IsInside(target, view.WorldSize))
        {
            return false;
        }

        // Tick is a placeholder (0); the sink re-stamps it with the input-delayed tick + sequence.
        sink.Enqueue(new IssueMoveCommand(playerId, 0, commander.Id, target));
        return true;
    }

    private static bool IsInside(TilePosition position, WorldSize size)
    {
        return position.X >= 0
            && position.X < size.Width
            && position.Y >= 0
            && position.Y < size.Height;
    }
}

public sealed record HeadlessHostResult(
    long Tick,
    string StateHash,
    int CommandsIssued,
    GameStatus Status,
    PlayerId? WinnerId,
    IReadOnlyList<ISimulationCommand> CommandLog);
