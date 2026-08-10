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

    /// <summary>Commands waiting for a future tick (FIFO within a tick).</summary>
    public IReadOnlyList<ISimulationCommand> PendingCommands => _commandBuffer;

    /// <summary>
    /// Enqueues a command for application at the start of <see cref="AdvanceTick"/> when
    /// simulation <see cref="Tick"/> equals <see cref="ISimulationCommand.Tick"/>.
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

        _commandBuffer.Add(command);
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
                c.CommanderId, c.TargetKind, c.Position, c.Direction, c.SelectedItemRecipe),
            QueueCommanderDemolishCommand c => TryQueueCommanderDemolish(c.CommanderId, c.TargetEntityId),
            PlaceGhostBuildFromCommanderCommand c => TryPlaceGhostBuildFromCommander(
                c.CommanderId, c.TargetKind, c.Position, out _, c.Direction, c.SelectedItemRecipe),
            RotateEntityCommand c => TryRotateEntity(c.EntityId, c.Actor, c.Clockwise),
            StartResearchCommand c => TryStartResearch(c.Actor, c.Technology),
            CancelResearchCommand c => TryCancelResearch(c.Actor, c.Technology),
            SetTrackAllocationCommand c => TrySetTrackAllocation(c.Actor, c.Allocations) == ResearchCommandResult.Ok,
            SetFactoryProductionCommand c => TrySetFactoryProduction(
                c.FactoryId, c.Actor, c.OutputKind, c.BastionId),
            AssignFactoryBastionCommand c => TryAssignFactoryBastion(c.FactoryId, c.BastionId),
            SetBastionTemplateCommand c => TrySetBastionTemplate(c.BastionId, c.Actor, c.UnitKind, c.Count),
            IssueBastionOrderCommand c => TryIssueBastionOrder(c.BastionId, c.Actor, c.Order),
            SetAssemblerRecipeCommand c => TrySetAssemblerRecipe(c.AssemblerId, c.Actor, c.RecipeId),
            CollectOutputBufferCommand c => TryCollectOutputBuffer(c.CommanderId, c.TargetEntityId),
            WithdrawFromHubOrOutputCommand c => TryWithdrawFromHubOrOutput(c.CommanderId, c.TargetEntityId),
            DepositToHubOrInputCommand c => TryDepositToHubOrInput(c.CommanderId, c.TargetEntityId),
            DepositItemTypeToHubOrInputCommand c => TryDepositItemTypeToHubOrInput(
                c.CommanderId, c.TargetEntityId, c.Item),
            WithdrawItemTypeFromHubOrOutputCommand c => TryWithdrawItemTypeFromHubOrOutput(
                c.CommanderId, c.TargetEntityId, c.Item),
            _ => throw new NotSupportedException($"Unsupported command kind '{command.Kind}'."),
        };
    }

    private void ApplyQueuedCommandsForCurrentTick()
    {
        if (_commandBuffer.Count == 0)
        {
            return;
        }

        List<ISimulationCommand>? deferred = null;
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
                ApplyCommand(command);
            }
            // Tick < current: stale (should not happen with EnqueueCommand guards); drop.
        }

        _commandBuffer.Clear();
        if (deferred is { Count: > 0 })
        {
            _commandBuffer.AddRange(deferred);
        }
    }
}
