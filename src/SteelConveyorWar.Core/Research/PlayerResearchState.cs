namespace SteelConveyorWar.Core;

public sealed class TrackResearchState
{
    private readonly Dictionary<TechnologyId, int> _projectWeights = new();

    public string TrackId { get; }

    public TechnologyId? ActiveSerialTarget { get; internal set; }

    public IReadOnlyDictionary<TechnologyId, int> ProjectWeights => _projectWeights.AsReadOnly();

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

    // R29: persistent largest-remainder accumulators so long-run pack allocation across tracks
    // (by allocation basis points) and across weighted projects (by weight) matches the configured
    // ratio instead of collapsing to a systematic last-entry bias. Integer-only => deterministic.
    private readonly Dictionary<string, int> _trackSelectionRemainder = new();
    private readonly Dictionary<TechnologyId, int> _projectSelectionRemainder = new();

    public IReadOnlySet<TechnologyId> CompletedTechnologies => _completedTechnologies.AsReadOnly();

    internal HashSet<TechnologyId> CompletedTechnologiesMutable => _completedTechnologies;

    public IReadOnlyDictionary<TechnologyId, int> ProgressWorkUnits => _progressWorkUnits.AsReadOnly();

    internal Dictionary<TechnologyId, int> ProgressWorkUnitsMutable => _progressWorkUnits;

    public IReadOnlyDictionary<string, TrackResearchState> Tracks => _tracks.AsReadOnly();

    internal Dictionary<string, TrackResearchState> TracksMutable => _tracks;

    public IReadOnlySet<TechnologyId> LockedTechnologies => _lockedTechnologies.AsReadOnly();

    internal HashSet<TechnologyId> LockedTechnologiesMutable => _lockedTechnologies;

    public IReadOnlySet<string> ConfirmedExclusiveGroups => _confirmedExclusiveGroups.AsReadOnly();

    internal HashSet<string> ConfirmedExclusiveGroupsMutable => _confirmedExclusiveGroups;

    public string CurrentTierId { get; internal set; } = ResearchTierIds.T1;

    public IReadOnlySet<string> CompletedGateIds => _completedGateIds.AsReadOnly();

    internal HashSet<string> CompletedGateIdsMutable => _completedGateIds;

    public IReadOnlySet<string> Milestones => _milestones.AsReadOnly();

    internal HashSet<string> MilestonesMutable => _milestones;

    public IReadOnlySet<string> AppliedCapabilities => _appliedCapabilities.AsReadOnly();

    internal HashSet<string> AppliedCapabilitiesMutable => _appliedCapabilities;

    public IReadOnlySet<string> UnlockedEntityKinds => _unlockedEntityKinds.AsReadOnly();

    internal HashSet<string> UnlockedEntityKindsMutable => _unlockedEntityKinds;

    public IReadOnlySet<string> UnlockedRecipes => _unlockedRecipes.AsReadOnly();

    internal HashSet<string> UnlockedRecipesMutable => _unlockedRecipes;

    public IReadOnlySet<string> UnlockedItemRecipes => _unlockedItemRecipes.AsReadOnly();

    internal HashSet<string> UnlockedItemRecipesMutable => _unlockedItemRecipes;

    public IReadOnlyList<AddModifierEffect> AppliedModifiers => _appliedModifiers.AsReadOnly();

    internal List<AddModifierEffect> AppliedModifiersMutable => _appliedModifiers;

    internal Dictionary<string, int> TrackSelectionRemainder => _trackSelectionRemainder;

    internal Dictionary<TechnologyId, int> ProjectSelectionRemainder => _projectSelectionRemainder;

    internal void EnsureTracks(ResearchProfileDefinition profile)
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
