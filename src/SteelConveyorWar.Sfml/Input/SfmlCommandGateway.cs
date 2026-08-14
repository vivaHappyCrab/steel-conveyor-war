using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// R02: the SFML host's single gameplay-mutation entry point. Every player input that used to call an
/// immediate <c>simulation.Try*</c> mutator now builds the matching command DTO and enqueues it through
/// an <see cref="IPlayerCommandSink"/>, so local input takes the same tick-scheduled, replayable path
/// as remote input. Commands are built with a placeholder tick (0); the sink re-stamps the real
/// input-delayed tick and a monotonic sequence.
/// </summary>
/// <remarks>
/// Each method returns <c>true</c> once the intent was enqueued. Because application is deferred to a
/// future tick, there is no synchronous success/fail result; callers that previously branched on a
/// <c>Try*</c> boolean use this "was the input consumed" semantic instead. Pre-conditions that gate UI
/// flow (ownership, entity kind) are still checked by the caller before enqueueing.
/// </remarks>
internal sealed class SfmlCommandGateway
{
    private readonly IPlayerCommandSink _sink;

    internal SfmlCommandGateway(IPlayerCommandSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    internal IReadOnlyList<ISimulationCommand> CommandLog => _sink.CommandLog;

    internal bool IssueMove(int entityId, PlayerId actor, TilePosition target)
        => Enqueue(new IssueMoveCommand(actor, 0, entityId, target));

    internal bool StopCommander(int commanderId, PlayerId actor)
        => Enqueue(new StopCommanderCommand(actor, 0, commanderId));

    internal bool QueueCommanderBuild(
        int commanderId,
        PlayerId actor,
        EntityKind targetKind,
        TilePosition position,
        Direction direction,
        ItemRecipeId? selectedItemRecipe,
        bool inserterLongReach = false)
        => Enqueue(new QueueCommanderBuildCommand(
            actor, 0, commanderId, targetKind, position, direction, selectedItemRecipe, inserterLongReach));

    internal bool QueueCommanderDemolish(int commanderId, PlayerId actor, int targetEntityId)
        => Enqueue(new QueueCommanderDemolishCommand(actor, 0, commanderId, targetEntityId));

    internal bool RotateEntity(int entityId, PlayerId actor, bool clockwise)
        => Enqueue(new RotateEntityCommand(actor, 0, entityId, clockwise));

    internal bool SetInserterReach(int entityId, PlayerId actor, bool longReach)
        => Enqueue(new SetInserterReachCommand(actor, 0, entityId, longReach));

    internal bool SetAssemblerRecipe(int assemblerId, PlayerId actor, ItemRecipeId recipeId)
        => Enqueue(new SetAssemblerRecipeCommand(actor, 0, assemblerId, recipeId));

    internal bool SetFactoryProduction(int factoryId, PlayerId actor, EntityKind? outputKind, int? bastionId = null)
        => Enqueue(new SetFactoryProductionCommand(actor, 0, factoryId, outputKind, bastionId));

    internal bool SetBastionTemplate(int bastionId, PlayerId actor, EntityKind unitKind, int count)
        => Enqueue(new SetBastionTemplateCommand(actor, 0, bastionId, unitKind, count));

    internal bool IssueBastionOrder(int bastionId, PlayerId actor, BastionOrder order)
        => Enqueue(new IssueBastionOrderCommand(actor, 0, bastionId, order));

    internal bool DepositToHubOrInput(int commanderId, PlayerId actor, int targetEntityId)
        => Enqueue(new DepositToHubOrInputCommand(actor, 0, commanderId, targetEntityId));

    internal bool WithdrawFromHubOrOutput(int commanderId, PlayerId actor, int targetEntityId)
        => Enqueue(new WithdrawFromHubOrOutputCommand(actor, 0, commanderId, targetEntityId));

    internal bool DepositItemTypeToHubOrInput(int commanderId, PlayerId actor, int targetEntityId, ItemId item)
        => Enqueue(new DepositItemTypeToHubOrInputCommand(actor, 0, commanderId, targetEntityId, item));

    internal bool WithdrawItemTypeFromHubOrOutput(int commanderId, PlayerId actor, int targetEntityId, ItemId item)
        => Enqueue(new WithdrawItemTypeFromHubOrOutputCommand(actor, 0, commanderId, targetEntityId, item));

    internal bool CancelResearch(PlayerId actor, TechnologyId technology)
        => Enqueue(new CancelResearchCommand(actor, 0, technology));

    internal bool SelectResearch(PlayerId actor, TechnologyId technology, bool confirmExclusive, string? preferredTrackId)
        => Enqueue(new SelectResearchCommand(actor, 0, technology, confirmExclusive, preferredTrackId));

    internal bool SetTrackAllocation(PlayerId actor, IReadOnlyDictionary<string, int> allocations)
        => Enqueue(new SetTrackAllocationCommand(actor, 0, allocations));

    private bool Enqueue(ISimulationCommand command)
    {
        _sink.Enqueue(command);
        return true;
    }
}
