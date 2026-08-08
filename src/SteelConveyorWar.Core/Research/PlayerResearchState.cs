namespace SteelConveyorWar.Core;

public sealed class TrackResearchState
{
    private readonly Dictionary<TechnologyId, int> _projectWeights = new();

    public string TrackId { get; }

    public TechnologyId? ActiveSerialTarget { get; internal set; }

    public IReadOnlyDictionary<TechnologyId, int> ProjectWeights => _projectWeights;

    internal Dictionary<TechnologyId, int> ProjectWeightsMutable => _projectWeights;

    public int AllocationBasisPoints { get; internal set; }

    public TrackResearchState(string trackId, int allocationBasisPoints)
    {
        TrackId = trackId;
        AllocationBasisPoints = allocationBasisPoints;
    }
}

public sealed class PlayerResearchState
{
    private readonly HashSet<TechnologyId> _completedTechnologies = new();
    private readonly Dictionary<TechnologyId, int> _progressWorkUnits = new();
    private readonly Dictionary<string, TrackResearchState> _tracks = new();
    private readonly HashSet<TechnologyId> _lockedTechnologies = new();
    private readonly HashSet<string> _confirmedExclusiveGroups = new();
    private readonly HashSet<string> _completedGateIds = new();
    private readonly HashSet<string> _milestones = new();
    private readonly HashSet<string> _appliedCapabilities = new();
    private readonly HashSet<string> _unlockedEntityKinds = new();
    private readonly HashSet<string> _unlockedRecipes = new();
    private readonly HashSet<string> _unlockedItemRecipes = new();
    private readonly List<AddModifierEffect> _appliedModifiers = new();

    public IReadOnlySet<TechnologyId> CompletedTechnologies => _completedTechnologies;

    internal HashSet<TechnologyId> CompletedTechnologiesMutable => _completedTechnologies;

    public IReadOnlyDictionary<TechnologyId, int> ProgressWorkUnits => _progressWorkUnits;

    internal Dictionary<TechnologyId, int> ProgressWorkUnitsMutable => _progressWorkUnits;

    public IReadOnlyDictionary<string, TrackResearchState> Tracks => _tracks;

    internal Dictionary<string, TrackResearchState> TracksMutable => _tracks;

    public IReadOnlySet<TechnologyId> LockedTechnologies => _lockedTechnologies;

    internal HashSet<TechnologyId> LockedTechnologiesMutable => _lockedTechnologies;

    public IReadOnlySet<string> ConfirmedExclusiveGroups => _confirmedExclusiveGroups;

    internal HashSet<string> ConfirmedExclusiveGroupsMutable => _confirmedExclusiveGroups;

    public string CurrentTierId { get; internal set; } = ResearchTierIds.T1;

    public IReadOnlySet<string> CompletedGateIds => _completedGateIds;

    internal HashSet<string> CompletedGateIdsMutable => _completedGateIds;

    public IReadOnlySet<string> Milestones => _milestones;

    internal HashSet<string> MilestonesMutable => _milestones;

    public IReadOnlySet<string> AppliedCapabilities => _appliedCapabilities;

    internal HashSet<string> AppliedCapabilitiesMutable => _appliedCapabilities;

    public IReadOnlySet<string> UnlockedEntityKinds => _unlockedEntityKinds;

    internal HashSet<string> UnlockedEntityKindsMutable => _unlockedEntityKinds;

    public IReadOnlySet<string> UnlockedRecipes => _unlockedRecipes;

    internal HashSet<string> UnlockedRecipesMutable => _unlockedRecipes;

    public IReadOnlySet<string> UnlockedItemRecipes => _unlockedItemRecipes;

    internal HashSet<string> UnlockedItemRecipesMutable => _unlockedItemRecipes;

    public IReadOnlyList<AddModifierEffect> AppliedModifiers => _appliedModifiers;

    internal List<AddModifierEffect> AppliedModifiersMutable => _appliedModifiers;

    public void EnsureTracks(ResearchProfileDefinition profile)
    {
        foreach (var track in profile.Schedule.Tracks)
        {
            if (!_tracks.ContainsKey(track.Id))
            {
                var allocation = profile.Schedule.Budget.DefaultAllocations.GetValueOrDefault(track.Id, 0);
                _tracks[track.Id] = new TrackResearchState(track.Id, allocation);
            }
        }
    }
}
