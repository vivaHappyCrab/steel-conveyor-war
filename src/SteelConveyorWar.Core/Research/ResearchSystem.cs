namespace SteelConveyorWar.Core;

public sealed class ResearchSystem
{
    public const int LabCycleTicks = 30;
    public const int AllocationScale = 10_000;

    private readonly ResearchCatalog _catalog;
    private readonly ResearchProfileDefinition _profile;

    public ResearchSystem(ResearchCatalog catalog, ResearchProfileDefinition profile)
    {
        _catalog = catalog;
        _profile = profile;
    }

    public ResearchCatalog Catalog => _catalog;

    public ResearchProfileDefinition Profile => _profile;

    public void InitializePlayer(PlayerResearchState research)
    {
        research.CurrentTierId = ResearchTierIds.T1;
        research.EnsureTracks(_profile);
        foreach (var capability in _catalog.BaselineCapabilities)
        {
            research.AppliedCapabilitiesMutable.Add(capability);
        }

        ApplyTierBaselineUnlocks(research, ResearchTierIds.T1);
    }

    public ResearchCommandResult TrySelectResearch(
        PlayerResearchState research,
        TechnologyId technologyId,
        bool confirmExclusive = false,
        string? preferredTrackId = null)
    {
        if (!_catalog.Technologies.TryGetValue(technologyId, out var definition))
        {
            return ResearchCommandResult.UnknownTechnology;
        }

        if (research.CompletedTechnologies.Contains(technologyId))
        {
            return ResearchCommandResult.AlreadyCompleted;
        }

        if (research.LockedTechnologies.Contains(technologyId))
        {
            return ResearchCommandResult.Locked;
        }

        if (!IsTechnologyAvailable(research, definition))
        {
            return ResearchCommandResult.NotAvailable;
        }

        var exclusiveGroup = FindExclusiveGroup(technologyId);
        if (exclusiveGroup is not null
            && exclusiveGroup.ConfirmationRequired
            && !research.ConfirmedExclusiveGroups.Contains(exclusiveGroup.Id)
            && !confirmExclusive)
        {
            return ResearchCommandResult.ExclusiveConfirmationRequired;
        }

        var track = ResolveTrackForTechnology(research, definition, preferredTrackId);
        if (track is null)
        {
            return ResearchCommandResult.InvalidTrack;
        }

        var trackDefinition = _profile.Schedule.Tracks.Single(t => t.Id == track.TrackId);
        if (trackDefinition.Mode == ResearchTrackMode.Serial)
        {
            track.ActiveSerialTarget = technologyId;
        }
        else
        {
            if (!track.ProjectWeights.ContainsKey(technologyId))
            {
                if (CountActiveParallelProjects(track) >= trackDefinition.MaximumActiveProjects)
                {
                    return ResearchCommandResult.NotAvailable;
                }

                track.ProjectWeightsMutable[technologyId] = trackDefinition.DefaultWeight;
            }
        }

        if (exclusiveGroup is not null && exclusiveGroup.LockOn == ExclusiveLockOn.Start)
        {
            ApplyExclusiveLock(research, exclusiveGroup, technologyId);
        }

        if (exclusiveGroup is not null && confirmExclusive)
        {
            research.ConfirmedExclusiveGroupsMutable.Add(exclusiveGroup.Id);
        }

        return ResearchCommandResult.Ok;
    }

    public ResearchCommandResult TryConfirmExclusive(PlayerResearchState research, string exclusiveGroupId)
    {
        if (_profile.ExclusiveGroups.All(group => group.Id != exclusiveGroupId))
        {
            return ResearchCommandResult.NotAvailable;
        }

        research.ConfirmedExclusiveGroupsMutable.Add(exclusiveGroupId);
        return ResearchCommandResult.Ok;
    }

    public ResearchCommandResult TrySetTrackAllocation(
        PlayerResearchState research,
        IReadOnlyDictionary<string, int> allocations)
    {
        if (!_profile.Schedule.Budget.PlayerAdjustable)
        {
            return ResearchCommandResult.InvalidAllocation;
        }

        var total = 0;
        foreach (var track in _profile.Schedule.Tracks)
        {
            if (!allocations.TryGetValue(track.Id, out var value) || value < 0)
            {
                return ResearchCommandResult.InvalidAllocation;
            }

            total += value;
        }

        if (total != _profile.Schedule.Budget.Scale)
        {
            return ResearchCommandResult.InvalidAllocation;
        }

        research.EnsureTracks(_profile);
        foreach (var pair in allocations)
        {
            research.Tracks[pair.Key].AllocationBasisPoints = pair.Value;
        }

        return ResearchCommandResult.Ok;
    }

    public ResearchCommandResult TrySetProjectWeight(
        PlayerResearchState research,
        string trackId,
        TechnologyId technologyId,
        int weight)
    {
        if (weight < 0)
        {
            return ResearchCommandResult.InvalidWeight;
        }

        research.EnsureTracks(_profile);
        if (!research.Tracks.TryGetValue(trackId, out var track))
        {
            return ResearchCommandResult.InvalidTrack;
        }

        var trackDefinition = _profile.Schedule.Tracks.SingleOrDefault(t => t.Id == trackId);
        if (trackDefinition is null || trackDefinition.Mode != ResearchTrackMode.WeightedParallel)
        {
            return ResearchCommandResult.InvalidTrack;
        }

        if (!_catalog.Technologies.TryGetValue(technologyId, out var definition)
            || research.CompletedTechnologies.Contains(technologyId)
            || research.LockedTechnologies.Contains(technologyId)
            || !IsTechnologyAvailable(research, definition))
        {
            return ResearchCommandResult.NotAvailable;
        }

        if (!track.ProjectWeights.ContainsKey(technologyId)
            && CountActiveParallelProjects(track) >= trackDefinition.MaximumActiveProjects
            && weight > 0)
        {
            return ResearchCommandResult.NotAvailable;
        }

        if (weight == 0)
        {
            track.ProjectWeightsMutable.Remove(technologyId);
        }
        else
        {
            track.ProjectWeightsMutable[technologyId] = weight;
        }

        return ResearchCommandResult.Ok;
    }

    public void ProcessResearch(GameSimulation simulation, long tick)
    {
        foreach (var player in simulation.Players.OrderBy(player => player.Id.Value))
        {
            ProcessPlayerResearch(simulation, player, tick);
        }
    }

    public ResearchSnapshot GetSnapshot(PlayerResearchState research)
    {
        research.EnsureTracks(_profile);
        var technologies = _catalog.Technologies.Values
            .OrderBy(tech => tech.Id.Value, StringComparer.Ordinal)
            .Select(tech =>
            {
                var available = IsTechnologyAvailable(research, tech)
                    && !research.CompletedTechnologies.Contains(tech.Id)
                    && !research.LockedTechnologies.Contains(tech.Id);
                var exclusive = FindExclusiveGroup(tech.Id);
                var trackId = FindTrackIdForTechnology(research, tech);
                research.ProgressWorkUnits.TryGetValue(tech.Id, out var progress);
                return new ResearchTechnologySnapshot(
                    tech.Id,
                    tech.TierId,
                    tech.Cost.EffortUnits,
                    progress,
                    research.CompletedTechnologies.Contains(tech.Id),
                    research.LockedTechnologies.Contains(tech.Id),
                    available,
                    exclusive?.ConfirmationRequired == true && !research.ConfirmedExclusiveGroups.Contains(exclusive.Id),
                    trackId,
                    tech.Cost.SciencePacks,
                    tech.Tags);
            })
            .ToList();

        var tracks = _profile.Schedule.Tracks
            .OrderBy(track => track.Id, StringComparer.Ordinal)
            .Select(track =>
            {
                var state = research.Tracks[track.Id];
                return new ResearchTrackSnapshot(
                    track.Id,
                    track.Mode,
                    state.AllocationBasisPoints,
                    track.MaximumActiveProjects,
                    state.ActiveSerialTarget,
                    state.ProjectWeights.ToDictionary(pair => pair.Key, pair => pair.Value),
                    _profile.Schedule.Budget.PlayerAdjustable);
            })
            .ToList();

        var gates = _profile.GatesByFromTier.Values
            .OrderBy(gate => gate.Id, StringComparer.Ordinal)
            .Select(gate => new ResearchGateSnapshot(
                gate.Id,
                gate.FromTierId,
                gate.TargetTierId,
                research.CompletedGateIds.Contains(gate.Id),
                gate.Requirements.Select(requirement => new GateRequirementSnapshot(
                    requirement.Id,
                    requirement.MinimumCompleted,
                    requirement.CandidateTechnologyIds.Count(id => research.CompletedTechnologies.Contains(id)),
                    requirement.CandidateTechnologyIds)).ToList()))
            .ToList();

        return new ResearchSnapshot(
            _profile.Id,
            research.CurrentTierId,
            research.Milestones.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            tracks,
            technologies,
            gates,
            research.AppliedCapabilities.OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    private void ProcessPlayerResearch(GameSimulation simulation, PlayerState player, long tick)
    {
        _ = tick;
        var research = player.Research;
        research.EnsureTracks(_profile);

        var labs = simulation.World.Entities
            .Where(entity => entity.IsAlive && entity.OwnerId == player.Id && entity.Kind == EntityKind.Laboratory)
            .OrderBy(entity => entity.Id)
            .ToList();

        var activeProjects = CollectActiveProjects(research);
        var packConsumptions = 0;

        foreach (var lab in labs)
        {
            if (activeProjects.Count == 0)
            {
                lab.WorkTicksRemaining = 0;
                lab.WorkTicksTotal = 0;
                continue;
            }

            if (!activeProjects.Any(project => CanAfford(lab, project.Definition)))
            {
                lab.WorkTicksRemaining = 0;
                lab.WorkTicksTotal = 0;
                continue;
            }

            if (lab.WorkTicksRemaining <= 0)
            {
                lab.WorkTicksTotal = LabCycleTicks;
                lab.WorkTicksRemaining = LabCycleTicks;
            }

            lab.WorkTicksRemaining--;
            if (lab.WorkTicksRemaining > 0)
            {
                continue;
            }

            if (!simulation.TryConsumeBuildingEnergy(lab))
            {
                lab.WorkTicksTotal = 0;
                continue;
            }

            if (TryConsumePackForAnyActiveProject(lab, research, activeProjects))
            {
                packConsumptions++;
            }

            lab.WorkTicksTotal = 0;
        }

        if (packConsumptions > 0)
        {
            DistributeWork(research, activeProjects, packConsumptions);
        }

        EvaluateCompletions(research);
    }

    private List<(string TrackId, TechnologyId TechnologyId, TechnologyDefinition Definition)> CollectActiveProjects(
        PlayerResearchState research)
    {
        var result = new List<(string, TechnologyId, TechnologyDefinition)>();
        foreach (var trackDefinition in _profile.Schedule.Tracks.OrderBy(track => track.Id, StringComparer.Ordinal))
        {
            var track = research.Tracks[trackDefinition.Id];
            if (trackDefinition.Mode == ResearchTrackMode.Serial)
            {
                if (track.ActiveSerialTarget is null)
                {
                    continue;
                }

                var technologyId = track.ActiveSerialTarget.Value;
                if (!IsActiveCandidate(research, technologyId))
                {
                    track.ActiveSerialTarget = null;
                    continue;
                }

                result.Add((track.TrackId, technologyId, _catalog.Technologies[technologyId]));
            }
            else
            {
                foreach (var technologyId in track.ProjectWeights.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).ToList())
                {
                    if (!IsActiveCandidate(research, technologyId) || track.ProjectWeights[technologyId] <= 0)
                    {
                        track.ProjectWeightsMutable.Remove(technologyId);
                        continue;
                    }

                    result.Add((track.TrackId, technologyId, _catalog.Technologies[technologyId]));
                }
            }
        }

        return result;
    }

    private bool IsActiveCandidate(PlayerResearchState research, TechnologyId technologyId)
    {
        return _catalog.Technologies.TryGetValue(technologyId, out var definition)
            && !research.CompletedTechnologies.Contains(technologyId)
            && !research.LockedTechnologies.Contains(technologyId)
            && IsTechnologyAvailable(research, definition);
    }

    private static bool TryConsumePackForAnyActiveProject(
        WorldEntity lab,
        PlayerResearchState research,
        List<(string TrackId, TechnologyId TechnologyId, TechnologyDefinition Definition)> activeProjects)
    {
        foreach (var project in activeProjects.OrderBy(project => project.TechnologyId.Value, StringComparer.Ordinal))
        {
            if (CanAfford(lab, project.Definition) && TryConsume(lab, project.Definition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanAfford(WorldEntity lab, TechnologyDefinition definition)
    {
        return definition.Cost.SciencePacks.All(pack => lab.InputBuffer.Has(pack.Item, pack.Amount));
    }

    private static bool TryConsume(WorldEntity lab, TechnologyDefinition definition)
    {
        if (!CanAfford(lab, definition))
        {
            return false;
        }

        foreach (var pack in definition.Cost.SciencePacks)
        {
            if (!lab.InputBuffer.TryRemove(pack.Item, pack.Amount))
            {
                return false;
            }
        }

        return true;
    }

    private void DistributeWork(
        PlayerResearchState research,
        List<(string TrackId, TechnologyId TechnologyId, TechnologyDefinition Definition)> activeProjects,
        int packConsumptions)
    {
        var trackGroups = activeProjects
            .GroupBy(project => project.TrackId)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        var trackAllocations = new Dictionary<string, int>();
        var allocated = 0;
        for (var i = 0; i < trackGroups.Count; i++)
        {
            var trackId = trackGroups[i].Key;
            var track = research.Tracks[trackId];
            int share;
            if (i == trackGroups.Count - 1)
            {
                share = packConsumptions - allocated;
            }
            else
            {
                share = packConsumptions * track.AllocationBasisPoints / AllocationScale;
                allocated += share;
            }

            trackAllocations[trackId] = Math.Max(0, share);
        }

        foreach (var group in trackGroups)
        {
            var workBudget = trackAllocations[group.Key];
            if (workBudget <= 0)
            {
                continue;
            }

            var trackDefinition = _profile.Schedule.Tracks.Single(track => track.Id == group.Key);
            var projects = group.OrderBy(project => project.TechnologyId.Value, StringComparer.Ordinal).ToList();
            if (trackDefinition.Mode == ResearchTrackMode.Serial)
            {
                var project = projects[0];
                AwardWork(research, project.TechnologyId, project.Definition, workBudget);
                continue;
            }

            var weights = projects.Select(project =>
            {
                research.Tracks[group.Key].ProjectWeights.TryGetValue(project.TechnologyId, out var weight);
                return Math.Max(1, weight);
            }).ToList();
            var weightSum = weights.Sum();
            var awarded = 0;
            for (var i = 0; i < projects.Count; i++)
            {
                int share;
                if (i == projects.Count - 1)
                {
                    share = workBudget - awarded;
                }
                else
                {
                    share = workBudget * weights[i] / weightSum;
                    awarded += share;
                }

                if (share > 0)
                {
                    AwardWork(research, projects[i].TechnologyId, projects[i].Definition, share);
                }
            }
        }
    }

    private void AwardWork(
        PlayerResearchState research,
        TechnologyId technologyId,
        TechnologyDefinition definition,
        int workUnits)
    {
        var multiplier = BasisPointsForTechnology(research, definition);
        var awarded = (int)((long)workUnits * multiplier / AllocationScale);
        if (awarded <= 0)
        {
            awarded = workUnits;
        }

        research.ProgressWorkUnits.TryGetValue(technologyId, out var current);
        research.ProgressWorkUnitsMutable[technologyId] = current + awarded;
    }

    private int BasisPointsForTechnology(PlayerResearchState research, TechnologyDefinition definition)
    {
        if (definition.TierId == research.CurrentTierId)
        {
            return AllocationScale;
        }

        // Catch-up for unfinished optionals from previous tiers.
        if (IsOptional(definition.Id) && IsTierUnlockedOrPast(research, definition.TierId))
        {
            return _profile.CatchUpMultiplierBasisPoints;
        }

        return AllocationScale;
    }

    private bool IsOptional(TechnologyId technologyId)
    {
        return _profile.OptionalPools.Any(pool => pool.TechnologyIds.Contains(technologyId))
            || definitionHasTag(technologyId, "optional")
            || definitionHasTag(technologyId, "doctrine");
    }

    private bool definitionHasTag(TechnologyId technologyId, string tag)
    {
        return _catalog.Technologies.TryGetValue(technologyId, out var definition)
            && definition.Tags.Contains(tag);
    }

    private bool IsTierUnlockedOrPast(PlayerResearchState research, string tierId)
    {
        if (research.CurrentTierId == tierId)
        {
            return true;
        }

        if (tierId == ResearchTierIds.T1)
        {
            return research.CurrentTierId == ResearchTierIds.T2 || research.Milestones.Contains(ResearchMilestoneIds.T3Qualified);
        }

        return false;
    }

    internal void EvaluatePendingCompletions(PlayerResearchState research) => EvaluateCompletions(research);

    private void EvaluateCompletions(PlayerResearchState research)
    {
        foreach (var technologyId in research.ProgressWorkUnits.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).ToList())
        {
            if (research.CompletedTechnologies.Contains(technologyId))
            {
                continue;
            }

            var definition = _catalog.Technologies[technologyId];
            if (research.ProgressWorkUnits[technologyId] < definition.Cost.EffortUnits)
            {
                continue;
            }

            CompleteTechnology(research, definition);
        }

        EvaluateGates(research);
    }

    private void CompleteTechnology(PlayerResearchState research, TechnologyDefinition definition)
    {
        research.CompletedTechnologiesMutable.Add(definition.Id);
        research.ProgressWorkUnitsMutable.Remove(definition.Id);

        foreach (var track in research.Tracks.Values)
        {
            if (track.ActiveSerialTarget == definition.Id)
            {
                track.ActiveSerialTarget = null;
            }

            track.ProjectWeightsMutable.Remove(definition.Id);
        }

        var exclusiveGroup = FindExclusiveGroup(definition.Id);
        if (exclusiveGroup is not null && exclusiveGroup.LockOn == ExclusiveLockOn.Complete)
        {
            ApplyExclusiveLock(research, exclusiveGroup, definition.Id);
        }

        ApplyEffects(research, definition.Effects);
    }

    private void EvaluateGates(PlayerResearchState research)
    {
        if (!_profile.GatesByFromTier.TryGetValue(research.CurrentTierId, out var gate))
        {
            return;
        }

        if (research.CompletedGateIds.Contains(gate.Id))
        {
            return;
        }

        if (!gate.Requirements.All(requirement =>
                requirement.CandidateTechnologyIds.Count(id => research.CompletedTechnologies.Contains(id))
                >= requirement.MinimumCompleted))
        {
            return;
        }

        research.CompletedGateIdsMutable.Add(gate.Id);
        ApplyEffects(research, gate.CompletionEffects);

        if (gate.TargetTierId is not null)
        {
            research.CurrentTierId = gate.TargetTierId;
            ApplyTierBaselineUnlocks(research, gate.TargetTierId);
        }
    }

    private void ApplyExclusiveLock(
        PlayerResearchState research,
        ExclusiveGroupDefinition group,
        TechnologyId selected)
    {
        var selectedCount = group.MemberTechnologyIds.Count(id =>
            id == selected
            || research.CompletedTechnologies.Contains(id)
            || research.Tracks.Values.Any(track =>
                track.ActiveSerialTarget == id || track.ProjectWeights.ContainsKey(id)));

        if (selectedCount < group.MaximumSelections)
        {
            return;
        }

        foreach (var member in group.MemberTechnologyIds)
        {
            if (member == selected || research.CompletedTechnologies.Contains(member))
            {
                continue;
            }

            research.LockedTechnologiesMutable.Add(member);
            foreach (var track in research.Tracks.Values)
            {
                if (track.ActiveSerialTarget == member)
                {
                    track.ActiveSerialTarget = null;
                }

                track.ProjectWeightsMutable.Remove(member);
            }
        }
    }

    private void ApplyEffects(PlayerResearchState research, IReadOnlyList<ResearchEffect> effects)
    {
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case UnlockContentEffect unlock:
                    switch (unlock.ContentKind)
                    {
                        case "entity":
                            research.UnlockedEntityKindsMutable.Add(unlock.ContentId);
                            break;
                        case "recipe":
                            research.UnlockedRecipesMutable.Add(unlock.ContentId);
                            break;
                        case "item-recipe":
                            research.UnlockedItemRecipesMutable.Add(unlock.ContentId);
                            break;
                    }

                    break;
                case GrantCapabilityEffect grant:
                    research.AppliedCapabilitiesMutable.Add(grant.CapabilityId);
                    break;
                case AddModifierEffect modifier:
                    research.AppliedModifiersMutable.Add(modifier);
                    break;
                case CompleteMilestoneEffect milestone:
                    research.MilestonesMutable.Add(milestone.MilestoneId);
                    break;
            }
        }
    }

    private void ApplyTierBaselineUnlocks(PlayerResearchState research, string tierId)
    {
        if (!_catalog.Tiers.TryGetValue(tierId, out var tier))
        {
            return;
        }

        ApplyEffects(research, tier.UnlockContent);
    }

    private bool IsTechnologyAvailable(PlayerResearchState research, TechnologyDefinition definition)
    {
        if (!IsTechnologyInProfile(definition.Id))
        {
            return false;
        }

        if (definition.TierId == research.CurrentTierId)
        {
            return true;
        }

        // Previous-tier optionals remain available after tier-up.
        if (IsOptional(definition.Id) && IsTierUnlockedOrPast(research, definition.TierId))
        {
            return true;
        }

        // Mandatory techs from a later tier are not available early.
        return false;
    }

    private bool IsTechnologyInProfile(TechnologyId technologyId)
    {
        if (_profile.OptionalPools.Any(pool => pool.TechnologyIds.Contains(technologyId)))
        {
            return true;
        }

        if (_profile.ExclusiveGroups.Any(group => group.MemberTechnologyIds.Contains(technologyId)))
        {
            return true;
        }

        foreach (var gate in _profile.GatesByFromTier.Values)
        {
            if (gate.Requirements.Any(requirement => requirement.CandidateTechnologyIds.Contains(technologyId)))
            {
                return true;
            }
        }

        return false;
    }

    private ExclusiveGroupDefinition? FindExclusiveGroup(TechnologyId technologyId)
    {
        return _profile.ExclusiveGroups.FirstOrDefault(group => group.MemberTechnologyIds.Contains(technologyId));
    }

    private TrackResearchState? ResolveTrackForTechnology(
        PlayerResearchState research,
        TechnologyDefinition definition,
        string? preferredTrackId)
    {
        research.EnsureTracks(_profile);
        if (preferredTrackId is not null && research.Tracks.TryGetValue(preferredTrackId, out var preferred))
        {
            return preferred;
        }

        var trackId = FindTrackIdForTechnology(research, definition);
        return trackId is null ? null : research.Tracks[trackId];
    }

    private string? FindTrackIdForTechnology(PlayerResearchState research, TechnologyDefinition definition)
    {
        foreach (var track in _profile.Schedule.Tracks.OrderBy(track => track.Id, StringComparer.Ordinal))
        {
            if (TrackAccepts(track, definition))
            {
                return track.Id;
            }
        }

        // Fallback: first track.
        return _profile.Schedule.Tracks.FirstOrDefault()?.Id;
    }

    private bool TrackAccepts(ResearchTrackDefinition track, TechnologyDefinition definition)
    {
        if (track.SourceKinds.Count == 0)
        {
            return true;
        }

        return definition.Tags.Any(tag => track.SourceKinds.Contains(tag));
    }

    private static int CountActiveParallelProjects(TrackResearchState track)
    {
        return track.ProjectWeights.Count(pair => pair.Value > 0);
    }
}
