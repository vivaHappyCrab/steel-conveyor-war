namespace SteelConveyorWar.Core;

public sealed class TrackResearchState
{
    public string TrackId { get; }

    public TechnologyId? ActiveSerialTarget { get; set; }

    public Dictionary<TechnologyId, int> ProjectWeights { get; } = new();

    public int AllocationBasisPoints { get; set; }

    public TrackResearchState(string trackId, int allocationBasisPoints)
    {
        TrackId = trackId;
        AllocationBasisPoints = allocationBasisPoints;
    }
}

public sealed class PlayerResearchState
{
    public HashSet<TechnologyId> CompletedTechnologies { get; } = new();

    public Dictionary<TechnologyId, int> ProgressWorkUnits { get; } = new();

    public Dictionary<string, TrackResearchState> Tracks { get; } = new();

    public HashSet<TechnologyId> LockedTechnologies { get; } = new();

    public HashSet<string> ConfirmedExclusiveGroups { get; } = new();

    public string CurrentTierId { get; set; } = ResearchTierIds.T1;

    public HashSet<string> CompletedGateIds { get; } = new();

    public HashSet<string> Milestones { get; } = new();

    public HashSet<string> AppliedCapabilities { get; } = new();

    public HashSet<string> UnlockedEntityKinds { get; } = new();

    public HashSet<string> UnlockedRecipes { get; } = new();

    public HashSet<string> UnlockedItemRecipes { get; } = new();

    public List<AddModifierEffect> AppliedModifiers { get; } = new();

    public void EnsureTracks(ResearchProfileDefinition profile)
    {
        foreach (var track in profile.Schedule.Tracks)
        {
            if (!Tracks.ContainsKey(track.Id))
            {
                var allocation = profile.Schedule.Budget.DefaultAllocations.GetValueOrDefault(track.Id, 0);
                Tracks[track.Id] = new TrackResearchState(track.Id, allocation);
            }
        }
    }
}
