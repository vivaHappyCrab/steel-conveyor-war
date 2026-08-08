namespace SteelConveyorWar.Core;

public static class ResearchContentValidator
{
    public static void Validate(ResearchCatalog catalog)
    {
        if (catalog.Technologies.Count == 0)
        {
            throw new InvalidOperationException("Research catalog must define technologies.");
        }

        if (catalog.Tiers.Count == 0)
        {
            throw new InvalidOperationException("Research catalog must define tiers.");
        }

        if (catalog.Profiles.Count == 0)
        {
            throw new InvalidOperationException("Research catalog must define profiles.");
        }

        foreach (var technology in catalog.Technologies.Values)
        {
            if (!catalog.Tiers.ContainsKey(technology.TierId))
            {
                throw new InvalidOperationException($"Technology '{technology.Id}' references unknown tier '{technology.TierId}'.");
            }

            if (technology.Cost.EffortUnits <= 0)
            {
                throw new InvalidOperationException($"Technology '{technology.Id}' must have positive effortUnits.");
            }

            if (technology.Cost.SciencePacks.Count == 0)
            {
                throw new InvalidOperationException($"Technology '{technology.Id}' must declare science packs.");
            }
        }

        foreach (var profile in catalog.Profiles.Values)
        {
            ValidateProfile(catalog, profile);
        }
    }

    private static void ValidateProfile(ResearchCatalog catalog, ResearchProfileDefinition profile)
    {
        if (profile.Schedule.Tracks.Count == 0)
        {
            throw new InvalidOperationException($"Profile '{profile.Id}' must declare tracks.");
        }

        var allocationSum = profile.Schedule.Budget.DefaultAllocations.Values.Sum();
        if (allocationSum != profile.Schedule.Budget.Scale)
        {
            throw new InvalidOperationException(
                $"Profile '{profile.Id}' budget allocations sum to {allocationSum}, expected {profile.Schedule.Budget.Scale}.");
        }

        foreach (var trackId in profile.Schedule.Budget.DefaultAllocations.Keys)
        {
            if (profile.Schedule.Tracks.All(track => track.Id != trackId))
            {
                throw new InvalidOperationException($"Profile '{profile.Id}' allocates unknown track '{trackId}'.");
            }
        }

        foreach (var gate in profile.GatesByFromTier.Values)
        {
            if (!catalog.Tiers.ContainsKey(gate.FromTierId))
            {
                throw new InvalidOperationException($"Profile '{profile.Id}' gate '{gate.Id}' has unknown fromTier '{gate.FromTierId}'.");
            }

            if (gate.TargetTierId is not null && !catalog.Tiers.ContainsKey(gate.TargetTierId)
                && gate.TargetTierId != "tier.t3-boundary")
            {
                // Target may be a virtual boundary id for MVP end — allow milestones-only targets via null.
            }

            foreach (var requirement in gate.Requirements)
            {
                if (requirement.MinimumCompleted <= 0)
                {
                    throw new InvalidOperationException($"Gate requirement '{requirement.Id}' must require at least one completion.");
                }

                foreach (var technologyId in requirement.CandidateTechnologyIds)
                {
                    if (!catalog.Technologies.ContainsKey(technologyId))
                    {
                        throw new InvalidOperationException(
                            $"Profile '{profile.Id}' gate requirement '{requirement.Id}' references unknown technology '{technologyId}'.");
                    }
                }
            }
        }

        foreach (var group in profile.ExclusiveGroups)
        {
            foreach (var technologyId in group.MemberTechnologyIds)
            {
                if (!catalog.Technologies.ContainsKey(technologyId))
                {
                    throw new InvalidOperationException(
                        $"Profile '{profile.Id}' exclusive group '{group.Id}' references unknown technology '{technologyId}'.");
                }
            }
        }

        foreach (var pool in profile.OptionalPools)
        {
            foreach (var technologyId in pool.TechnologyIds)
            {
                if (!catalog.Technologies.ContainsKey(technologyId))
                {
                    throw new InvalidOperationException(
                        $"Profile '{profile.Id}' optional pool '{pool.Id}' references unknown technology '{technologyId}'.");
                }
            }
        }
    }
}
