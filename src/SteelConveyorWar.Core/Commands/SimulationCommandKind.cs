namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// Discriminator for serialized command payloads (JSON field <c>kind</c>).
/// </summary>
/// <remarks>
/// M02: <see cref="AssignFactoryBastion"/> is obsolete (always-false handler) — filtered from
/// <see cref="SimulationCommandVocabulary.AdvertisedKinds"/> and rejected on deserialize.
/// <see cref="SetProjectWeight"/> carries parallel-track project weights (distinct from
/// <see cref="SetTrackAllocation"/> basis-point budgets).
/// </remarks>
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

    /// <summary>Obsolete: factories no longer assign to bastions. Kept for wire-number stability.</summary>
    [Obsolete("Factories no longer assign to bastions; spawn picks a deficit bastion automatically.")]
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

    // M02: parallel-track project weight (not the same as SetTrackAllocation basis points).
    SetProjectWeight = 21,
}
