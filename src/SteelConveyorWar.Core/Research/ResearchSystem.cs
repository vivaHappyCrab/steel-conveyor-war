namespace SteelConveyorWar.Core;

/// <summary>
/// R14: <c>internal</c> — accepts and mutates live authoritative research/simulation state, so it must
/// not be reachable from Sfml/Headless/plugin hosts. Held only via <see cref="GameSimulation"/>'s private
/// field; all research mutation goes through the command pipeline. Core.Tests keeps access via InternalsVisibleTo.
/// </summary>
internal sealed class ResearchSystem
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

    public ResearchCommandResult TryCancelResearch(PlayerResearchState research, TechnologyId technologyId)
    {
        research.EnsureTracks(_profile);
        var cancelled = false;
        foreach (var track in research.Tracks.Values)
        {
            if (track.ActiveSerialTarget == technologyId)
            {
                track.ActiveSerialTarget = null;
                cancelled = true;
            }

            if (track.ProjectWeightsMutable.Remove(technologyId))
            {
                cancelled = true;
            }
        }

        // ProgressWorkUnits intentionally preserved so restart continues from prior progress.
        return cancelled ? ResearchCommandResult.Ok : ResearchCommandResult.NotAvailable;
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
            // H04: exclusive start-locks change available research/content outcomes.
            research.BumpCapabilityEpoch();
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

        // R09: reject untrusted payloads carrying keys that are not schedule tracks,
        // instead of indexing research.Tracks[...] (which would throw KeyNotFoundException).
        foreach (var key in allocations.Keys)
        {
            if (_profile.Schedule.Tracks.All(track => track.Id != key))
            {
                return ResearchCommandResult.InvalidAllocation;
            }
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
                    tech.Tags,
                    tech.DisplayName,
                    tech.Description);
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

            // Drain each active work tick; empty buffer pauses the lab cycle.
            if (!simulation.TryConsumeBuildingEnergy(lab))
            {
                continue;
            }

            lab.WorkTicksRemaining--;
            if (lab.WorkTicksRemaining > 0)
            {
                continue;
            }

            // R29: pick the project this pack advances (identity-preserving) using the persistent
            // largest-remainder accumulators, then award the work directly to that project.
            ConsumeAndAwardForCycle(lab, research, activeProjects);

            lab.WorkTicksTotal = 0;
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

    // R29: choose which active project a completed lab cycle advances, preserving pack identity.
    // Track is chosen proportionally to its allocation basis points, then the project within the
    // track proportionally to its weight — both via a persistent largest-remainder rotation so the
    // long-run distribution matches the configured ratio (no last-entry bias). The consumed pack
    // pays for the chosen project's cost and the work is awarded to that same project.
    private void ConsumeAndAwardForCycle(
        WorldEntity lab,
        PlayerResearchState research,
        List<(string TrackId, TechnologyId TechnologyId, TechnologyDefinition Definition)> activeProjects)
    {
        var affordable = activeProjects
            .Where(project => CanAfford(lab, project.Definition))
            .OrderBy(project => project.TrackId, StringComparer.Ordinal)
            .ThenBy(project => project.TechnologyId.Value, StringComparer.Ordinal)
            .ToList();
        if (affordable.Count == 0)
        {
            return;
        }

        var trackCandidates = affordable
            .Select(project => project.TrackId)
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        var chosenTrack = SelectByLargestRemainder(
            trackCandidates,
            trackId => Math.Max(0, research.Tracks[trackId].AllocationBasisPoints),
            research.TrackSelectionRemainder);

        var trackProjects = affordable
            .Where(project => project.TrackId == chosenTrack)
            .OrderBy(project => project.TechnologyId.Value, StringComparer.Ordinal)
            .ToList();
        var trackDefinition = _profile.Schedule.Tracks.Single(track => track.Id == chosenTrack);

        (string TrackId, TechnologyId TechnologyId, TechnologyDefinition Definition) target;
        if (trackDefinition.Mode == ResearchTrackMode.Serial || trackProjects.Count == 1)
        {
            target = trackProjects[0];
        }
        else
        {
            var chosenTech = SelectByLargestRemainder(
                trackProjects.Select(project => project.TechnologyId).ToList(),
                techId => Math.Max(1, research.Tracks[chosenTrack].ProjectWeights.GetValueOrDefault(techId)),
                research.ProjectSelectionRemainder);
            target = trackProjects.Single(project => project.TechnologyId == chosenTech);
        }

        if (!TryConsume(lab, target.Definition))
        {
            return;
        }

        AwardWork(research, target.TechnologyId, target.Definition, 1);
    }

    // Stride / largest-remainder rotation: add each candidate's weight to its persistent
    // accumulator, pick the largest (ties broken by candidate order), then subtract the total
    // weight from the winner. Over many calls the selection frequency converges to the weights.
    // Integer arithmetic only => fully deterministic across runs.
    internal static TKey SelectByLargestRemainder<TKey>(
        IReadOnlyList<TKey> candidates,
        Func<TKey, int> weight,
        Dictionary<TKey, int> accumulator)
        where TKey : notnull
    {
        var total = 0;
        foreach (var candidate in candidates)
        {
            var w = Math.Max(0, weight(candidate));
            total += w;
            accumulator[candidate] = accumulator.GetValueOrDefault(candidate) + w;
        }

        if (total <= 0)
        {
            return candidates[0];
        }

        var chosen = candidates[0];
        var best = int.MinValue;
        foreach (var candidate in candidates)
        {
            var acc = accumulator.GetValueOrDefault(candidate);
            if (acc > best)
            {
                best = acc;
                chosen = candidate;
            }
        }

        accumulator[chosen] = accumulator.GetValueOrDefault(chosen) - total;
        return chosen;
    }

    // R28: aggregate duplicate science-pack entries by item so affordability is checked cumulatively
    // and consumption is atomic (all-or-nothing). The content validator already rejects duplicates,
    // but aggregating here keeps the runtime correct even if that guarantee is ever weakened.
    private static IReadOnlyDictionary<ItemId, int> AggregateCost(TechnologyDefinition definition)
    {
        var aggregated = new Dictionary<ItemId, int>();
        foreach (var pack in definition.Cost.SciencePacks)
        {
            aggregated[pack.Item] = aggregated.GetValueOrDefault(pack.Item) + pack.Amount;
        }

        return aggregated;
    }

    private static bool CanAfford(WorldEntity lab, TechnologyDefinition definition)
    {
        return lab.InputBuffer.HasAll(AggregateCost(definition));
    }

    private static bool TryConsume(WorldEntity lab, TechnologyDefinition definition)
    {
        // TryRemoveAll checks affordability atomically before removing anything, so there is no
        // partial consumption even if the caller skipped the CanAfford pre-check.
        return lab.InputBuffer.TryRemoveAll(AggregateCost(definition));
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
        // H04: completion changes unlocks/modifiers/locks — invalidate factory idle-skip keys.
        research.BumpCapabilityEpoch();
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

        // H04: gate/tier unlocks change production capability.
        research.BumpCapabilityEpoch();
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
