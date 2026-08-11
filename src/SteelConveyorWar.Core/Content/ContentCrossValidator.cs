namespace SteelConveyorWar.Core;

/// <summary>
/// R34: cross-catalog reference validation. Individual loaders only check their own internal
/// integrity, so a reference from one catalog into another (e.g. a build-cost tech gate that names
/// a technology which does not exist in the research catalog) is not caught at load time and would
/// silently misbehave at runtime. This validator runs once after all catalogs are loaded and rejects
/// the whole content set (all-or-nothing) if any inter-catalog reference is dangling.
/// </summary>
public static class ContentCrossValidator
{
    /// <summary>
    /// Validates every inter-catalog reference. Throws <see cref="InvalidOperationException"/> listing
    /// all problems if any reference is unresolved; returns normally when the set is consistent.
    /// Empty catalogs (unit-test / MVP fallback) contribute no references and therefore always pass.
    /// </summary>
    public static void Validate(
        ResearchCatalog research,
        BuildCostCatalog buildCosts,
        EntityCatalog entities,
        TileCatalog tiles,
        GameplayTablesCatalog? gameplayTables = null)
    {
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(buildCosts);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(tiles);

        var tables = gameplayTables ?? GameplayTablesCatalog.Empty;
        var errors = new List<string>();

        // build-costs → research: every tech gate must resolve to a known technology, otherwise the
        // build would be permanently un-gateable (or gated by a non-existent prerequisite).
        foreach (var (kind, requiredTech) in buildCosts.Requirements)
        {
            if (!research.Technologies.ContainsKey(requiredTech))
            {
                errors.Add(
                    $"build-costs entry '{kind}' requires technology '{requiredTech.Value}', " +
                    "which is not present in the research catalog.");
            }
        }

        // gameplay-tables → research: production recipe tech gates must resolve.
        foreach (var (kind, recipe) in tables.ProductionRecipes)
        {
            if (recipe.RequiredTechnology is { } requiredTech
                && !research.Technologies.ContainsKey(requiredTech))
            {
                errors.Add(
                    $"gameplay-tables productionRecipes '{kind}' requires technology '{requiredTech.Value}', " +
                    "which is not present in the research catalog.");
            }
        }

        // entities → EntityKind: an entity that declares a Defeat loss condition must name a kind the
        // engine recognizes; otherwise the victory/defeat rule silently never fires for it.
        foreach (var definition in entities.Entities.Values)
        {
            if (string.Equals(definition.LossCondition, "Defeat", StringComparison.Ordinal)
                && !Enum.TryParse<EntityKind>(definition.Kind, ignoreCase: false, out _))
            {
                errors.Add(
                    $"entity '{definition.Id}' declares lossCondition 'Defeat' but its kind " +
                    $"'{definition.Kind}' is not a known entity kind.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Cross-catalog content validation failed; content was not applied:" +
                Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }
}
