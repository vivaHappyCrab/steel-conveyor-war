using System.Collections.Immutable;

namespace SteelConveyorWar.Core.Commands;

/// <summary>Shared header fields for all gameplay commands.</summary>
public abstract record SimulationCommandBase(PlayerId Actor, long Tick) : ISimulationCommand
{
    public abstract SimulationCommandKind Kind { get; }

    /// <summary>
    /// R03: per-author ordering key. Init-only so it is not part of any positional constructor
    /// (keeps existing call sites source-compatible); the command source stamps it via
    /// <c>command with { Sequence = n }</c> or <see cref="GameSimulation.EnqueueCommand"/>.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// R02: re-stamps scheduling metadata (<see cref="ISimulationCommand.Tick"/> and
    /// <see cref="Sequence"/>) while preserving the concrete runtime command type and its payload.
    /// Used by <see cref="Commands.IPlayerCommandSink"/> so hosts can build a command without knowing
    /// the target tick, then have the sink assign the input-delayed tick and a monotonic sequence.
    /// </summary>
    public SimulationCommandBase WithScheduling(long tick, long sequence)
        => this with { Tick = tick, Sequence = sequence };

    /// <summary>H03: rebinds the authenticated session actor while preserving payload and scheduling.</summary>
    public SimulationCommandBase WithActor(PlayerId actor)
        => this with { Actor = actor };
}

public sealed record IssueMoveCommand(PlayerId Actor, long Tick, int EntityId, TilePosition Target)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.IssueMove;
}

public sealed record StopCommanderCommand(PlayerId Actor, long Tick, int CommanderId)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.StopCommander;
}

public sealed record QueueCommanderBuildCommand(
    PlayerId Actor,
    long Tick,
    int CommanderId,
    EntityKind TargetKind,
    TilePosition Position,
    Direction Direction = Direction.East,
    ItemRecipeId? SelectedItemRecipe = null,
    bool InserterLongReach = false) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.QueueCommanderBuild;
}

public sealed record QueueCommanderDemolishCommand(PlayerId Actor, long Tick, int CommanderId, int TargetEntityId)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.QueueCommanderDemolish;
}

public sealed record PlaceGhostBuildFromCommanderCommand(
    PlayerId Actor,
    long Tick,
    int CommanderId,
    EntityKind TargetKind,
    TilePosition Position,
    Direction Direction = Direction.East,
    ItemRecipeId? SelectedItemRecipe = null,
    bool InserterLongReach = false) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.PlaceGhostBuildFromCommander;
}

public sealed record RotateEntityCommand(PlayerId Actor, long Tick, int EntityId, bool Clockwise)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.RotateEntity;
}

public sealed record StartResearchCommand(PlayerId Actor, long Tick, TechnologyId Technology)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.StartResearch;
}

public sealed record CancelResearchCommand(PlayerId Actor, long Tick, TechnologyId Technology)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.CancelResearch;
}

public sealed record SetTrackAllocationCommand : SimulationCommandBase
{
    /// <summary>
    /// R12 / M01: freezes a deep copy of <paramref name="Allocations"/> so that mutating the caller's
    /// dictionary after constructing (or enqueuing) the command cannot change the applied intent.
    /// <see cref="Allocations"/> is get-only (no init) so <c>with { Allocations = ... }</c> cannot
    /// substitute a mutable dictionary. Positional parameter names are preserved for existing call sites.
    /// </summary>
    public SetTrackAllocationCommand(PlayerId Actor, long Tick, IReadOnlyDictionary<string, int> Allocations)
        : base(Actor, Tick)
    {
        this.Allocations = Allocations is null
            ? ImmutableDictionary<string, int>.Empty
            : Allocations.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, int> Allocations { get; }

    public override SimulationCommandKind Kind => SimulationCommandKind.SetTrackAllocation;
}

public sealed record SetFactoryProductionCommand(
    PlayerId Actor,
    long Tick,
    int FactoryId,
    EntityKind? OutputKind,
    int? BastionId = null) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.SetFactoryProduction;
}

/// <summary>Obsolete: factories no longer assign to bastions. Handler always returns false.</summary>
[Obsolete("Factories no longer assign to bastions; spawn picks a deficit bastion automatically.")]
public sealed record AssignFactoryBastionCommand(PlayerId Actor, long Tick, int FactoryId, int BastionId)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.AssignFactoryBastion;
}

public sealed record SetBastionTemplateCommand(
    PlayerId Actor,
    long Tick,
    int BastionId,
    EntityKind UnitKind,
    int Count) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.SetBastionTemplate;
}

public sealed record IssueBastionOrderCommand(PlayerId Actor, long Tick, int BastionId, BastionOrder Order)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.IssueBastionOrder;
}

public sealed record SetAssemblerRecipeCommand(PlayerId Actor, long Tick, int AssemblerId, ItemRecipeId RecipeId)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.SetAssemblerRecipe;
}

public sealed record CollectOutputBufferCommand(PlayerId Actor, long Tick, int CommanderId, int TargetEntityId)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.CollectOutputBuffer;
}

public sealed record WithdrawFromHubOrOutputCommand(PlayerId Actor, long Tick, int CommanderId, int TargetEntityId)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.WithdrawFromHubOrOutput;
}

public sealed record DepositToHubOrInputCommand(PlayerId Actor, long Tick, int CommanderId, int TargetEntityId)
    : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.DepositToHubOrInput;
}

public sealed record DepositItemTypeToHubOrInputCommand(
    PlayerId Actor,
    long Tick,
    int CommanderId,
    int TargetEntityId,
    ItemId Item) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.DepositItemTypeToHubOrInput;
}

public sealed record WithdrawItemTypeFromHubOrOutputCommand(
    PlayerId Actor,
    long Tick,
    int CommanderId,
    int TargetEntityId,
    ItemId Item) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.WithdrawItemTypeFromHubOrOutput;
}

/// <summary>
/// R02: research selection intent (previously issued via the immediate <c>TrySelectResearch</c>
/// convenience). Carries the optional exclusive-confirmation flag and preferred track so a queued
/// command reproduces the same selection as the direct call.
/// </summary>
public sealed record SelectResearchCommand(
    PlayerId Actor,
    long Tick,
    TechnologyId Technology,
    bool ConfirmExclusive = false,
    string? PreferredTrackId = null) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.SelectResearch;
}

/// <summary>
/// M02: sets a parallel-track project weight for one technology (distinct from
/// <see cref="SetTrackAllocationCommand"/> basis-point budgets across tracks).
/// </summary>
public sealed record SetProjectWeightCommand(
    PlayerId Actor,
    long Tick,
    string TrackId,
    TechnologyId Technology,
    int Weight) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.SetProjectWeight;
}

public sealed record SetInserterReachCommand(
    PlayerId Actor,
    long Tick,
    int EntityId,
    bool LongReach) : SimulationCommandBase(Actor, Tick)
{
    public override SimulationCommandKind Kind => SimulationCommandKind.SetInserterReach;
}
