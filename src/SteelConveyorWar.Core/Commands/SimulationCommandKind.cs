namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// Discriminator for serialized command payloads (JSON field <c>kind</c>).
/// </summary>
public enum SimulationCommandKind
{
    IssueMove = 1,
    StopCommander = 2,
    QueueCommanderBuild = 3,
    QueueCommanderDemolish = 4,
    PlaceGhostBuildFromCommander = 5,
    RotateEntity = 6,
    StartResearch = 7,
    CancelResearch = 8,
    SetTrackAllocation = 9,
    SetFactoryProduction = 10,
    AssignFactoryBastion = 11,
    SetBastionTemplate = 12,
    IssueBastionOrder = 13,
    SetAssemblerRecipe = 14,
    CollectOutputBuffer = 15,
    WithdrawFromHubOrOutput = 16,
    DepositToHubOrInput = 17,
    DepositItemTypeToHubOrInput = 18,
    WithdrawItemTypeFromHubOrOutput = 19,

    // R02: research selection as a queued command so SFML research input no longer bypasses the queue.
    SelectResearch = 20,
}
