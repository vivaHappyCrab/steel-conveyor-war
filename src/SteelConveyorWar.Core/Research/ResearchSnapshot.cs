namespace SteelConveyorWar.Core;

public sealed record ResearchTechnologySnapshot(
    TechnologyId Id,
    string TierId,
    int EffortUnits,
    int ProgressWorkUnits,
    bool IsCompleted,
    bool IsLocked,
    bool IsAvailable,
    bool RequiresExclusiveConfirmation,
    string? TrackId,
    IReadOnlyList<SciencePackCost> SciencePacks,
    IReadOnlyList<string> Tags,
    string DisplayName,
    string Description,
    IReadOnlyList<TechnologyId> Prerequisites);

public sealed record ResearchTrackSnapshot(
    string Id,
    ResearchTrackMode Mode,
    int AllocationBasisPoints,
    int MaximumActiveProjects,
    TechnologyId? ActiveSerialTarget,
    IReadOnlyDictionary<TechnologyId, int> ProjectWeights,
    bool PlayerAdjustableAllocation);

public sealed record ResearchGateSnapshot(
    string Id,
    string FromTierId,
    string? TargetTierId,
    bool IsCompleted,
    IReadOnlyList<GateRequirementSnapshot> Requirements);

public sealed record GateRequirementSnapshot(
    string Id,
    int MinimumCompleted,
    int CompletedCount,
    IReadOnlyList<TechnologyId> CandidateTechnologyIds);

public sealed record ResearchSnapshot(
    string ProfileId,
    string CurrentTierId,
    IReadOnlyList<string> Milestones,
    IReadOnlyList<ResearchTrackSnapshot> Tracks,
    IReadOnlyList<ResearchTechnologySnapshot> Technologies,
    IReadOnlyList<ResearchGateSnapshot> Gates,
    IReadOnlyList<string> Capabilities);
