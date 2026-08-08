namespace SteelConveyorWar.Core;

public abstract record ResearchEffect;

public sealed record UnlockContentEffect(string ContentKind, string ContentId) : ResearchEffect
{
    public static UnlockContentEffect Entity(EntityKind kind) => new("entity", kind.ToString());

    public static UnlockContentEffect Recipe(string recipeId) => new("recipe", recipeId);

    public static UnlockContentEffect ItemRecipe(ItemRecipeId recipeId) => new("item-recipe", recipeId.ToString());
}

public sealed record GrantCapabilityEffect(string CapabilityId) : ResearchEffect;

public sealed record AddModifierEffect(
    string StatId,
    ModifierOperation Operation,
    int ValueBasisPoints,
    string? Selector = null) : ResearchEffect;

public sealed record CompleteMilestoneEffect(string MilestoneId) : ResearchEffect;
