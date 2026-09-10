using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core;

/// <summary>
/// Tick-stamped command buffer: hosts enqueue intents; <see cref="AdvanceTick"/> applies
/// matching-tick commands before systems. Legacy <c>Try*</c> APIs still mutate immediately
/// for SFML/test compatibility — lockstep/replay hosts should use <see cref="EnqueueCommand"/>.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly List<ISimulationCommand> _commandBuffer = new();

    // R11: diagnostics of commands rejected during the most recent AdvanceTick. Presentation-only
    // (not hashed): lets hosts/tests observe *why* an intent did not apply instead of a silent drop.
    private readonly List<CommandRejection> _commandRejections = new();

    // H03: when EnqueueCommand receives Sequence 0 (legacy/test helpers), assign a positive per-actor
    // sequence so production apply never depends on unstable equal-key ordering.
    private readonly Dictionary<int, long> _enqueueSequenceByActor = new();

    // Append-only ledger of every command accepted by EnqueueCommand (already tick/sequence stamped).
    // Immediate Try* mutators never appear here — they are not replayable.
    private readonly List<ISimulationCommand> _recordedCommands = new();

    /// <summary>Commands waiting for a future tick (FIFO within a tick).</summary>
    // R05: ReadOnlyCollection wrapper so external code cannot mutate the pending buffer via downcast.
    public IReadOnlyList<ISimulationCommand> PendingCommands => _commandBuffer.AsReadOnly();

    /// <summary>
    /// Every command successfully accepted by <see cref="EnqueueCommand"/> this match, in enqueue order.
    /// Source of truth for durable replay capture. Immediate <c>Try*</c> calls are not recorded.
    /// </summary>
    public IReadOnlyList<ISimulationCommand> RecordedCommands => _recordedCommands.AsReadOnly();

    /// <summary>
    /// R11: commands the simulation refused to apply during the most recent tick (handler rejected,
    /// threw, or unknown kind), with a reason each. Cleared at the start of every
    /// <see cref="ApplyQueuedCommandsForCurrentTick"/>. Not part of the determinism hash.
    /// </summary>
    public IReadOnlyList<CommandRejection> LastTickCommandRejections => _commandRejections.AsReadOnly();

    /// <summary>
    /// Enqueues a command for application at the start of <see cref="AdvanceTick"/> when
    /// simulation <see cref="Tick"/> equals <see cref="ISimulationCommand.Tick"/>.
    /// H03: <see cref="ISimulationCommand.Sequence"/> must be &gt; 0 after enqueue — Sequence 0 is
    /// auto-stamped with a positive per-actor counter for legacy helpers.
    /// </summary>
    public void EnqueueCommand(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Tick <= Tick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                $"Command tick {command.Tick} must be greater than current simulation tick {Tick}.");
        }

        // M02/H03: obsolete kinds are not wire-serializable — keep them out of the pending buffer so
        // canonical sort / pending-command hash never call Serialize on a rejected vocabulary entry.
        if (SimulationCommandVocabulary.IsObsolete(command.Kind))
        {
            throw new ArgumentException(
                $"Command kind '{command.Kind}' is obsolete and cannot be enqueued (M02).",
                nameof(command));
        }

        if (command is SimulationCommandBase baseCommand && baseCommand.Sequence <= 0)
        {
            command = baseCommand.WithScheduling(baseCommand.Tick, NextEnqueueSequence(baseCommand.Actor));
        }

        _commandBuffer.Add(command);
        _recordedCommands.Add(command);
    }

    private long NextEnqueueSequence(PlayerId actor)
    {
        var next = _enqueueSequenceByActor.TryGetValue(actor.Value, out var current) ? current + 1 : 1;
        _enqueueSequenceByActor[actor.Value] = next;
        return next;
    }

    /// <summary>Convenience: stamps <paramref name="factory"/> with <c>Tick + 1</c> and enqueues.</summary>
    public T EnqueueForNextTick<T>(Func<long, T> factory)
        where T : ISimulationCommand
    {
        ArgumentNullException.ThrowIfNull(factory);
        var command = factory(Tick + 1);
        EnqueueCommand(command);
        return command;
    }

    /// <summary>
    /// Applies a command immediately (same handlers as the per-tick buffer).
    /// Prefer <see cref="EnqueueCommand"/> for lockstep/replay; this exists for tests and hosts
    /// that already hold a DTO.
    /// </summary>
    public bool ApplyCommand(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command switch
        {
            IssueMoveCommand c => TryIssueMoveCommand(c.EntityId, c.Actor, c.Target),
            StopCommanderCommand c => TryStopCommander(c.CommanderId, c.Actor),
            QueueCommanderBuildCommand c => TryQueueCommanderBuild(
                c.CommanderId, c.TargetKind, c.Position, c.Direction, c.SelectedItemRecipe, c.Actor, c.InserterLongReach),
            QueueCommanderDemolishCommand c => TryQueueCommanderDemolish(c.CommanderId, c.TargetEntityId, c.Actor),
            PlaceGhostBuildFromCommanderCommand c => TryPlaceGhostBuildFromCommander(
                c.CommanderId, c.TargetKind, c.Position, out _, c.Direction, c.SelectedItemRecipe, c.Actor, c.InserterLongReach),
            RotateEntityCommand c => TryRotateEntity(c.EntityId, c.Actor, c.Clockwise),
            StartResearchCommand c => TryStartResearch(c.Actor, c.Technology, c.Actor),
            CancelResearchCommand c => TryCancelResearch(c.Actor, c.Technology),
            // R02/R26: actor == owning player (research is keyed per-player); pass actor for defense-in-depth.
            SelectResearchCommand c => TrySelectResearch(
                c.Actor, c.Technology, c.ConfirmExclusive, c.PreferredTrackId, c.Actor) == ResearchCommandResult.Ok,
            SetTrackAllocationCommand c => TrySetTrackAllocation(c.Actor, c.Allocations) == ResearchCommandResult.Ok,
            SetProjectWeightCommand c => TrySetProjectWeight(
                c.Actor, c.TrackId, c.Technology, c.Weight) == ResearchCommandResult.Ok,
            SetFactoryProductionCommand c => TrySetFactoryProduction(
                c.FactoryId, c.Actor, c.OutputKind, c.BastionId),
#pragma warning disable CS0618 // Obsolete AssignFactoryBastion retained for apply-path rejection diagnostics.
            AssignFactoryBastionCommand c => TryAssignFactoryBastion(c.FactoryId, c.BastionId),
#pragma warning restore CS0618
            SetBastionTemplateCommand c => TrySetBastionTemplate(c.BastionId, c.Actor, c.UnitKind, c.Count),
            IssueBastionOrderCommand c => TryIssueBastionOrder(c.BastionId, c.Actor, c.Order),
            SetAssemblerRecipeCommand c => TrySetAssemblerRecipe(c.AssemblerId, c.Actor, c.RecipeId),
            CollectOutputBufferCommand c => TryCollectOutputBuffer(c.CommanderId, c.TargetEntityId, c.Actor),
            WithdrawFromHubOrOutputCommand c => TryWithdrawFromHubOrOutput(c.CommanderId, c.TargetEntityId, c.Actor),
            DepositToHubOrInputCommand c => TryDepositToHubOrInput(c.CommanderId, c.TargetEntityId, c.Actor),
            DepositItemTypeToHubOrInputCommand c => TryDepositItemTypeToHubOrInput(
                c.CommanderId, c.TargetEntityId, c.Item, c.Actor),
            WithdrawItemTypeFromHubOrOutputCommand c => TryWithdrawItemTypeFromHubOrOutput(
                c.CommanderId, c.TargetEntityId, c.Item, c.Actor),
            SetInserterReachCommand c => TrySetInserterReach(c.EntityId, c.Actor, c.LongReach),
            // R09: unknown/unsupported kinds are rejected (untrusted input must not throw out of the tick loop).
            _ => false,
        };
    }

    private void ApplyQueuedCommandsForCurrentTick()
    {
        // R11: reset per-tick rejection diagnostics even when nothing is queued this tick.
        _commandRejections.Clear();
        if (_commandBuffer.Count == 0)
        {
            return;
        }

        List<ISimulationCommand>? deferred = null;
        List<ISimulationCommand>? currentTick = null;
        foreach (var command in _commandBuffer)
        {
            if (command.Tick > Tick)
            {
                deferred ??= new List<ISimulationCommand>();
                deferred.Add(command);
                continue;
            }

            if (command.Tick == Tick)
            {
                currentTick ??= new List<ISimulationCommand>();
                currentTick.Add(command);
            }
            // Tick < current: stale (should not happen with EnqueueCommand guards); drop.
        }

        if (currentTick is { Count: > 0 })
        {
            // H03: List.Sort is unstable — equal keys must not rely on insertion order. Canonical key is
            // (Actor, Sequence, Kind, payload JSON). Duplicate (Actor, Sequence) after the first are rejected.
            currentTick.Sort(CompareCanonical);

            HashSet<(int Actor, long Sequence)>? appliedKeys = null;
            foreach (var command in currentTick)
            {
                appliedKeys ??= new HashSet<(int, long)>();
                if (!appliedKeys.Add((command.Actor.Value, command.Sequence)))
                {
                    _commandRejections.Add(new CommandRejection(
                        command.Kind,
                        command.Actor,
                        command.Sequence,
                        "duplicate (Actor, Sequence)"));
                    continue;
                }

                // R09: a single malformed/hostile command must never abort the whole tick.
                // Isolate any handler exception; the command is dropped as rejected.
                // R11: record the outcome so rejections are logged with a reason instead of silently dropped.
                CommandResult result;
                try
                {
                    result = ApplyCommand(command)
                        ? CommandResult.Ok
                        : CommandResult.Rejected("handler rejected (ownership/validation/unknown kind)");
                }
                catch (Exception ex)
                {
                    // Rejected: swallow so untrusted input cannot deny service to the tick loop.
                    result = CommandResult.Rejected($"handler threw {ex.GetType().Name}");
                }

                if (!result.Accepted)
                {
                    _commandRejections.Add(new CommandRejection(
                        command.Kind, command.Actor, command.Sequence, result.RejectionReason ?? "rejected"));
                }
            }
        }

        _commandBuffer.Clear();
        if (deferred is { Count: > 0 })
        {
            _commandBuffer.AddRange(deferred);
        }
    }

    /// <summary>
    /// H03: canonical cross-peer command comparison for a single tick:
    /// (Actor, Sequence, Kind, stable payload JSON). Does not assume <see cref="List{T}.Sort"/> stability.
    /// </summary>
    private static int CompareCanonical(ISimulationCommand left, ISimulationCommand right)
    {
        var byActor = left.Actor.Value.CompareTo(right.Actor.Value);
        if (byActor != 0)
        {
            return byActor;
        }

        var bySequence = left.Sequence.CompareTo(right.Sequence);
        if (bySequence != 0)
        {
            return bySequence;
        }

        var byKind = left.Kind.CompareTo(right.Kind);
        if (byKind != 0)
        {
            return byKind;
        }

        return string.CompareOrdinal(
            SimulationCommandSerializer.Serialize(left),
            SimulationCommandSerializer.Serialize(right));
    }
}
