namespace SteelConveyorWar.Core;

public static class ModifierResolver
{
    public const int BasisPointsScale = 10_000;

    public static int Resolve(
        int baseValue,
        IEnumerable<AddModifierEffect> modifiers,
        string statId,
        string? selector = null,
        int? minValue = null,
        int? maxValue = null)
    {
        var additive = 0;
        var multiplicative = BasisPointsScale;

        foreach (var modifier in modifiers.Where(m => m.StatId == statId && MatchesSelector(m.Selector, selector)))
        {
            if (modifier.Operation == ModifierOperation.Add)
            {
                additive += modifier.ValueBasisPoints;
            }
            else
            {
                multiplicative = (int)((long)multiplicative * modifier.ValueBasisPoints / BasisPointsScale);
            }
        }

        var value = (long)baseValue * BasisPointsScale + additive;
        value = value * multiplicative / BasisPointsScale;
        value /= BasisPointsScale;

        var rounded = (int)value;
        if (minValue is not null)
        {
            rounded = Math.Max(minValue.Value, rounded);
        }

        if (maxValue is not null)
        {
            rounded = Math.Min(maxValue.Value, rounded);
        }

        return rounded;
    }

    private static bool MatchesSelector(string? modifierSelector, string? requestedSelector)
    {
        if (modifierSelector is null || requestedSelector is null || modifierSelector == requestedSelector)
        {
            return true;
        }

        if (modifierSelector == ResearchSelectorIds.GroundUnit
            && Enum.TryParse<EntityKind>(requestedSelector, out var kind)
            && MvpDefinitions.IsGroundCombatUnit(kind))
        {
            return true;
        }

        return false;
    }
}

public static class CapabilityResolver
{
    public static bool HasCapability(PlayerResearchState research, string capabilityId)
    {
        return research.AppliedCapabilities.Contains(capabilityId);
    }

    public static bool IsEntityUnlocked(PlayerResearchState research, EntityKind kind, ResearchCatalog catalog, ResearchProfileDefinition profile)
    {
        var key = kind.ToString();
        if (research.UnlockedEntityKinds.Contains(key))
        {
            return true;
        }

        if (catalog.Tiers.TryGetValue(research.CurrentTierId, out var currentTier)
            && currentTier.UnlockContent.OfType<UnlockContentEffect>().Any(effect => effect.ContentKind == "entity" && effect.ContentId == key))
        {
            return true;
        }

        foreach (var completedGateId in research.CompletedGateIds)
        {
            foreach (var gate in profile.GatesByFromTier.Values)
            {
                if (gate.Id != completedGateId)
                {
                    continue;
                }

                if (gate.CompletionEffects.OfType<UnlockContentEffect>()
                    .Any(effect => effect.ContentKind == "entity" && effect.ContentId == key))
                {
                    return true;
                }

                if (gate.TargetTierId is not null
                    && catalog.Tiers.TryGetValue(gate.TargetTierId, out var unlockedTier)
                    && unlockedTier.UnlockContent.OfType<UnlockContentEffect>().Any(effect => effect.ContentKind == "entity" && effect.ContentId == key))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsItemRecipeUnlocked(PlayerResearchState research, ItemRecipeId recipeId)
    {
        return research.UnlockedItemRecipes.Contains(recipeId.ToString());
    }

    public static bool IsRecipeUnlocked(PlayerResearchState research, string recipeId)
    {
        return research.UnlockedRecipes.Contains(recipeId);
    }
}
