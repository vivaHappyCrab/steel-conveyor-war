namespace SteelConveyorWar.Core;

/// <summary>
/// R34 / M03: cross-catalog reference validation. Individual loaders only check their own internal
/// integrity, so a reference from one catalog into another is not caught at load time and would
/// silently misbehave at runtime. This validator runs once after all catalogs are loaded and rejects
/// the whole content set (all-or-nothing) if any inter-catalog reference is dangling.
/// </summary>
public static class ContentCrossValidator
{
    private static readonly HashSet<string> KnownUnlockContentKinds = new(StringComparer.Ordinal)
    {
        "entity",
        "recipe",
        "item-recipe",
    };

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
        GameplayTablesCatalog? gameplayTables = null,
        MapSettings? map = null)
    {
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(buildCosts);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(tiles);

        var tables = gameplayTables ?? GameplayTablesCatalog.Empty;
        var errors = new List<string>();

        ValidateBuildCostTechGates(research, buildCosts, errors);
        ValidateProductionRecipeTechGates(research, tables, errors);
        ValidateDefeatEntities(entities, errors);
        ValidateResearchUnlocks(research, tables, errors);
        ValidateRecipeDomains(tables, errors);
        ValidateBuildCostPlaceables(buildCosts, tables, errors);
        ValidateGameplayStatRanges(tables, errors);
        ValidateMapStarts(map, tables, errors);

        // tiles: presentation catalog is not yet referenced by map bootstrap; empty is allowed for
        // unit-test fixtures. Non-empty catalogs must stay self-consistent (loader-enforced).
        _ = tiles;

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Cross-catalog content validation failed; content was not applied:" +
                Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    private static void ValidateBuildCostTechGates(
        ResearchCatalog research,
        BuildCostCatalog buildCosts,
        List<string> errors)
    {
        foreach (var (kind, requiredTech) in buildCosts.Requirements)
        {
            if (!research.Technologies.ContainsKey(requiredTech))
            {
                errors.Add(
                    $"build-costs entry '{kind}' requires technology '{requiredTech.Value}', " +
                    "which is not present in the research catalog.");
            }
        }
    }

    private static void ValidateProductionRecipeTechGates(
        ResearchCatalog research,
        GameplayTablesCatalog tables,
        List<string> errors)
    {
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
    }

    private static void ValidateDefeatEntities(EntityCatalog entities, List<string> errors)
    {
        foreach (var definition in entities.Entities.Values)
        {
            if (string.Equals(definition.LossCondition, "Defeat", StringComparison.Ordinal)
                && (!Enum.TryParse<EntityKind>(definition.Kind, ignoreCase: false, out var kind)
                    || !Enum.IsDefined(kind)))
            {
                errors.Add(
                    $"entity '{definition.Id}' declares lossCondition 'Defeat' but its kind " +
                    $"'{definition.Kind}' is not a known entity kind.");
            }
        }
    }

    private static void ValidateResearchUnlocks(
        ResearchCatalog research,
        GameplayTablesCatalog tables,
        List<string> errors)
    {
        foreach (var effect in EnumerateUnlockEffects(research))
        {
            if (!KnownUnlockContentKinds.Contains(effect.ContentKind))
            {
                errors.Add(
                    $"research unlock contentKind '{effect.ContentKind}' is unknown " +
                    $"(contentId '{effect.ContentId}'); expected entity|recipe|item-recipe.");
                continue;
            }

            switch (effect.ContentKind)
            {
                case "entity":
                    if (!TryParseDefinedEntity(effect.ContentId, out _))
                    {
                        errors.Add(
                            $"research unlock entity '{effect.ContentId}' is not a known EntityKind.");
                    }

                    break;
                case "recipe":
                    if (!TryParseDefinedEntity(effect.ContentId, out var recipeKind)
                        || (tables.ProductionRecipes.Count > 0
                            && !tables.ProductionRecipes.ContainsKey(recipeKind)))
                    {
                        // When production recipes are empty (test fixtures), only require EntityKind.
                        if (!TryParseDefinedEntity(effect.ContentId, out _))
                        {
                            errors.Add(
                                $"research unlock recipe '{effect.ContentId}' is not a known EntityKind.");
                        }
                        else if (tables.ProductionRecipes.Count > 0)
                        {
                            errors.Add(
                                $"research unlock recipe '{effect.ContentId}' is not present in " +
                                "gameplay-tables productionRecipes.");
                        }
                    }

                    break;
                case "item-recipe":
                    if (!Enum.TryParse<ItemRecipeId>(effect.ContentId, ignoreCase: false, out var itemRecipe)
                        || !Enum.IsDefined(itemRecipe)
                        || (tables.ItemRecipes.Count > 0 && !tables.ItemRecipes.ContainsKey(itemRecipe)))
                    {
                        if (!Enum.TryParse<ItemRecipeId>(effect.ContentId, ignoreCase: false, out itemRecipe)
                            || !Enum.IsDefined(itemRecipe))
                        {
                            errors.Add(
                                $"research unlock item-recipe '{effect.ContentId}' is not a known ItemRecipeId.");
                        }
                        else if (tables.ItemRecipes.Count > 0)
                        {
                            errors.Add(
                                $"research unlock item-recipe '{effect.ContentId}' is not present in " +
                                "gameplay-tables itemRecipes.");
                        }
                    }

                    break;
            }
        }
    }

    private static IEnumerable<UnlockContentEffect> EnumerateUnlockEffects(ResearchCatalog research)
    {
        foreach (var tech in research.Technologies.Values)
        {
            foreach (var effect in tech.Effects.OfType<UnlockContentEffect>())
            {
                yield return effect;
            }
        }

        foreach (var tier in research.Tiers.Values)
        {
            foreach (var effect in tier.UnlockContent.OfType<UnlockContentEffect>())
            {
                yield return effect;
            }
        }

        foreach (var profile in research.Profiles.Values)
        {
            foreach (var gate in profile.GatesByFromTier.Values)
            {
                foreach (var effect in gate.CompletionEffects.OfType<UnlockContentEffect>())
                {
                    yield return effect;
                }
            }
        }
    }

    private static void ValidateRecipeDomains(GameplayTablesCatalog tables, List<string> errors)
    {
        foreach (var (kind, recipe) in tables.ProductionRecipes)
        {
            foreach (var item in recipe.Inputs.Keys)
            {
                if (!Enum.IsDefined(item))
                {
                    errors.Add($"productionRecipes '{kind}' has undefined ItemId '{item}'.");
                }
            }

            if (!Enum.IsDefined(recipe.OutputKind))
            {
                errors.Add($"productionRecipes '{kind}' has undefined outputKind '{recipe.OutputKind}'.");
            }
        }

        foreach (var (recipeId, recipe) in tables.ItemRecipes)
        {
            foreach (var item in recipe.Inputs.Keys)
            {
                if (!Enum.IsDefined(item))
                {
                    errors.Add($"itemRecipes '{recipeId}' has undefined ItemId '{item}'.");
                }
            }

            if (!Enum.IsDefined(recipe.OutputItem))
            {
                errors.Add($"itemRecipes '{recipeId}' has undefined outputItem '{recipe.OutputItem}'.");
            }
        }
    }

    private static void ValidateBuildCostPlaceables(
        BuildCostCatalog buildCosts,
        GameplayTablesCatalog tables,
        List<string> errors)
    {
        if (tables.EntityStats.Count == 0)
        {
            return;
        }

        foreach (var kind in buildCosts.Costs.Keys)
        {
            if (!tables.EntityStats.ContainsKey(kind))
            {
                errors.Add(
                    $"build-costs entry '{kind}' is not placeable: missing entityStats entry in gameplay-tables.");
            }
        }
    }

    private static void ValidateGameplayStatRanges(GameplayTablesCatalog tables, List<string> errors)
    {
        foreach (var (kind, stats) in tables.EntityStats)
        {
            if (stats.MaxHealth <= 0)
            {
                errors.Add($"entityStats '{kind}' must have maxHealth > 0.");
            }

            if (stats.AttackDamage < 0
                || stats.AttackRange < 0
                || stats.AttackCooldownTicks < 0
                || stats.MoveEveryTicks < 0
                || stats.VisionRadius < 0
                || stats.Armor < 0
                || stats.SplashRadius < 0)
            {
                errors.Add(
                    $"entityStats '{kind}' has an out-of-range combat/move field " +
                    "(damage/range/cooldowns/vision/armor/splash must be >= 0).");
            }

            if (!Enum.IsDefined(stats.ProjectileKind))
            {
                errors.Add($"entityStats '{kind}' has undefined projectileKind '{stats.ProjectileKind}'.");
            }
        }
    }

    private static void ValidateMapStarts(
        MapSettings? map,
        GameplayTablesCatalog tables,
        List<string> errors)
    {
        if (map is null)
        {
            return;
        }

        var world = new WorldSize(
            MapPlayerDefinition.DefaultWorldWidth,
            MapPlayerDefinition.DefaultWorldHeight);
        var occupied = new List<(int PlayerId, string Role, TilePosition Origin, WorldSize Footprint)>();

        foreach (var player in map.Players)
        {
            TilePosition commander;
            TilePosition bastion;
            TilePosition hub;
            try
            {
                (commander, bastion, hub) = player.ResolveStartPositions(world);
            }
            catch (Exception ex)
            {
                errors.Add($"map player '{player.Id}' start positions invalid: {ex.Message}");
                continue;
            }

            ValidateStartInside(player.Id, "startCommander", commander, world, errors);
            ValidateStartInside(player.Id, "startBastion", bastion, world, errors);
            ValidateStartInside(player.Id, "startHub", hub, world, errors);

            occupied.Add((player.Id, "Commander", commander, FootprintOrDefault(tables, EntityKind.Commander)));
            occupied.Add((player.Id, "Bastion", bastion, FootprintOrDefault(tables, EntityKind.Bastion)));
            occupied.Add((player.Id, "Hub", hub, FootprintOrDefault(tables, EntityKind.Hub)));
        }

        for (var i = 0; i < occupied.Count; i++)
        {
            for (var j = i + 1; j < occupied.Count; j++)
            {
                var left = occupied[i];
                var right = occupied[j];
                if (RectsOverlap(left.Origin, left.Footprint, right.Origin, right.Footprint))
                {
                    errors.Add(
                        $"map start footprints overlap: player {left.PlayerId} {left.Role} @ {left.Origin} " +
                        $"and player {right.PlayerId} {right.Role} @ {right.Origin}.");
                }
            }
        }
    }

    private static void ValidateStartInside(
        int playerId,
        string field,
        TilePosition tile,
        WorldSize world,
        List<string> errors)
    {
        if (tile.X < 0 || tile.Y < 0 || tile.X >= world.Width || tile.Y >= world.Height)
        {
            errors.Add(
                $"map player '{playerId}' {field} ({tile.X},{tile.Y}) is outside world " +
                $"{world.Width}x{world.Height}.");
        }
    }

    private static WorldSize FootprintOrDefault(GameplayTablesCatalog tables, EntityKind kind)
        => tables.Footprints.TryGetValue(kind, out var size) ? size : new WorldSize(1, 1);

    private static bool RectsOverlap(TilePosition aOrigin, WorldSize aSize, TilePosition bOrigin, WorldSize bSize)
    {
        var aRight = aOrigin.X + aSize.Width;
        var aBottom = aOrigin.Y + aSize.Height;
        var bRight = bOrigin.X + bSize.Width;
        var bBottom = bOrigin.Y + bSize.Height;
        return aOrigin.X < bRight && aRight > bOrigin.X && aOrigin.Y < bBottom && aBottom > bOrigin.Y;
    }

    private static bool TryParseDefinedEntity(string raw, out EntityKind kind)
    {
        if (Enum.TryParse(raw, ignoreCase: false, out kind) && Enum.IsDefined(kind))
        {
            return true;
        }

        kind = default;
        return false;
    }
}
