namespace SteelConveyorWar.Core.Commands;

/// <summary>Shared header fields for all gameplay commands.</summary>
public abstract record SimulationCommandBase(PlayerId Actor, long Tick) : ISimulationCommand
{
    public abstract SimulationCommandKind Kind { get; }
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
    ItemRecipeId? SelectedItemRecipe = null) : SimulationCommandBase(Actor, Tick)
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
    ItemRecipeId? SelectedItemRecipe = null) : SimulationCommandBase(Actor, Tick)
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

public sealed record SetTrackAllocationCommand(
    PlayerId Actor,
    long Tick,
    IReadOnlyDictionary<string, int> Allocations) : SimulationCommandBase(Actor, Tick)
{
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
