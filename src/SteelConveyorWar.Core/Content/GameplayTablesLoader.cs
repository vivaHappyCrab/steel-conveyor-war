using System.Text.Json;

namespace SteelConveyorWar.Core;

public static class GameplayTablesLoader
{
    private static readonly JsonSerializerOptions JsonOptions = ContentJsonOptions.CreateStrict();

    public static GameplayTablesCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<GameplayTablesDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Gameplay-tables catalog JSON deserialized to null.");

        ContentSchema.RequireSupportedVersion("gameplay-tables", dto.SchemaVersion);

        var powerDemand = ParseKindIntMap(dto.PowerDemand, "powerDemand");
        var powerProduction = ParseKindIntMap(dto.PowerProduction, "powerProduction");
        var itemStackSizes = ParseItemIntMap(dto.ItemStackSizes, "itemStackSizes");
        var footprints = ParseFootprints(dto.Footprints);
        var collisionRadius = ParseCollisionRadius(dto.CollisionRadius);
        var techSignature = ParseKindIntMap(dto.TechSignatureIntensity, "techSignatureIntensity");
        var productionRecipes = ParseProductionRecipes(dto.ProductionRecipes);
        var itemRecipes = ParseItemRecipes(dto.ItemRecipes);
        var entityStats = ParseEntityStats(dto.EntityStats);
        var resistances = ParseResistances(dto.Resistances);
        var researchBonuses = ParseResearchBonuses(dto.ResearchBonuses);

        if (entityStats.Count == 0)
        {
            throw new InvalidOperationException("Gameplay-tables catalog must declare at least one entityStats entry.");
        }

        // H07: GameplayTablesCatalog compact ctor freezes all table graphs (incl. nested recipe Inputs).
        return new GameplayTablesCatalog(
            dto.SchemaVersion,
            powerDemand,
            powerProduction,
            itemStackSizes,
            footprints,
            collisionRadius,
            techSignature,
            productionRecipes,
            itemRecipes,
            entityStats,
            resistances,
            researchBonuses);
    }

    private static Dictionary<EntityKind, int> ParseKindIntMap(Dictionary<string, int>? source, string section)
    {
        var result = new Dictionary<EntityKind, int>();
        if (source is null)
        {
            return result;
        }

        foreach (var (key, value) in source)
        {
            var kind = ParseEntityKind(key, section);
            if (!result.TryAdd(kind, value))
            {
                throw new InvalidOperationException($"Duplicate {section} kind '{kind}'.");
            }
        }

        return result;
    }

    private static Dictionary<ItemId, int> ParseItemIntMap(Dictionary<string, int>? source, string section)
    {
        var result = new Dictionary<ItemId, int>();
        if (source is null)
        {
            return result;
        }

        foreach (var (key, value) in source)
        {
            if (!Enum.TryParse<ItemId>(key, ignoreCase: true, out var item) || !Enum.IsDefined(item))
            {
                throw new InvalidOperationException($"{section} has unknown item '{key}'.");
            }

            if (value <= 0)
            {
                throw new InvalidOperationException($"{section} stack size for '{item}' must be > 0.");
            }

            if (!result.TryAdd(item, value))
            {
                throw new InvalidOperationException($"Duplicate {section} item '{item}'.");
            }
        }

        return result;
    }

    private static Dictionary<EntityKind, WorldSize> ParseFootprints(Dictionary<string, FootprintDto>? source)
    {
        var result = new Dictionary<EntityKind, WorldSize>();
        if (source is null)
        {
            return result;
        }

        foreach (var (key, footprint) in source)
        {
            var kind = ParseEntityKind(key, "footprints");
            if (footprint.Width <= 0 || footprint.Height <= 0)
            {
                throw new InvalidOperationException($"footprints '{kind}' must have width > 0 and height > 0.");
            }

            if (!result.TryAdd(kind, new WorldSize(footprint.Width, footprint.Height)))
            {
                throw new InvalidOperationException($"Duplicate footprints kind '{kind}'.");
            }
        }

        return result;
    }

    private static Dictionary<EntityKind, long> ParseCollisionRadius(Dictionary<string, long>? source)
    {
        var result = new Dictionary<EntityKind, long>();
        if (source is null)
        {
            return result;
        }

        foreach (var (key, radius) in source)
        {
            var kind = ParseEntityKind(key, "collisionRadius");
            if (radius < 0)
            {
                throw new InvalidOperationException($"collisionRadius '{kind}' must be >= 0.");
            }

            if (!result.TryAdd(kind, radius))
            {
                throw new InvalidOperationException($"Duplicate collisionRadius kind '{kind}'.");
            }
        }

        return result;
    }

    private static Dictionary<EntityKind, ProductionRecipe> ParseProductionRecipes(
        Dictionary<string, ProductionRecipeDto>? source)
    {
        var result = new Dictionary<EntityKind, ProductionRecipe>();
        if (source is null)
        {
            return result;
        }

        foreach (var (key, recipe) in source)
        {
            var kind = ParseEntityKind(key, "productionRecipes");
            var outputKind = string.IsNullOrWhiteSpace(recipe.OutputKind)
                ? kind
                : ParseEntityKind(recipe.OutputKind, "productionRecipes.outputKind");

            if (recipe.WorkTicks <= 0)
            {
                throw new InvalidOperationException($"productionRecipes '{kind}' must have workTicks > 0.");
            }

            TechnologyId? requiredTech = null;
            if (!string.IsNullOrWhiteSpace(recipe.RequiredTechnology))
            {
                requiredTech = new TechnologyId(recipe.RequiredTechnology.Trim());
            }

            var inputs = ParseCostLines(recipe.Inputs, $"productionRecipes '{kind}'");
            if (!result.TryAdd(kind, new ProductionRecipe(inputs, outputKind, recipe.WorkTicks, requiredTech)))
            {
                throw new InvalidOperationException($"Duplicate productionRecipes kind '{kind}'.");
            }
        }

        return result;
    }

    private static Dictionary<ItemRecipeId, ItemRecipeDefinition> ParseItemRecipes(
        Dictionary<string, ItemRecipeDto>? source)
    {
        var result = new Dictionary<ItemRecipeId, ItemRecipeDefinition>();
        if (source is null)
        {
            return result;
        }

        foreach (var (key, recipe) in source)
        {
            if (!Enum.TryParse<ItemRecipeId>(key, ignoreCase: true, out var recipeId) || !Enum.IsDefined(recipeId))
            {
                throw new InvalidOperationException($"itemRecipes has unknown recipe id '{key}'.");
            }

            if (!Enum.TryParse<ItemId>(recipe.OutputItem, ignoreCase: true, out var outputItem) || !Enum.IsDefined(outputItem))
            {
                throw new InvalidOperationException($"itemRecipes '{recipeId}' has unknown outputItem '{recipe.OutputItem}'.");
            }

            if (recipe.OutputAmount <= 0)
            {
                throw new InvalidOperationException($"itemRecipes '{recipeId}' must have outputAmount > 0.");
            }

            if (recipe.WorkTicks <= 0)
            {
                throw new InvalidOperationException($"itemRecipes '{recipeId}' must have workTicks > 0.");
            }

            var inputs = ParseCostLines(recipe.Inputs, $"itemRecipes '{recipeId}'");
            if (!result.TryAdd(recipeId, new ItemRecipeDefinition(recipeId, inputs, outputItem, recipe.OutputAmount, recipe.WorkTicks)))
            {
                throw new InvalidOperationException($"Duplicate itemRecipes id '{recipeId}'.");
            }
        }

        return result;
    }

    private static Dictionary<EntityKind, EntityStats> ParseEntityStats(Dictionary<string, EntityStatsDto>? source)
    {
        var result = new Dictionary<EntityKind, EntityStats>();
        if (source is null)
        {
            return result;
        }

        foreach (var (key, stats) in source)
        {
            var kind = ParseEntityKind(key, "entityStats");
            if (stats.MaxHealth <= 0)
            {
                throw new InvalidOperationException($"entityStats '{kind}' must have maxHealth > 0.");
            }

            if (stats.AttackDamage < 0
                || stats.AttackRange < 0
                || stats.AttackCooldownTicks < 0
                || stats.MoveEveryTicks < 0
                || stats.VisionRadius < 0
                || stats.Armor < 0
                || stats.SplashRadius < 0)
            {
                throw new InvalidOperationException(
                    $"entityStats '{kind}' combat/move fields must be >= 0 " +
                    "(damage/range/cooldown/move/vision/armor/splash).");
            }

            var projectile = ProjectileKind.GroundToGround;
            if (!string.IsNullOrWhiteSpace(stats.ProjectileKind))
            {
                projectile = ContentJsonOptions.ParseDefinedEnum<ProjectileKind>(
                    stats.ProjectileKind,
                    $"entityStats '{kind}' projectileKind");
            }

            var movement = MovementType.Ground;
            if (!string.IsNullOrWhiteSpace(stats.MovementType))
            {
                movement = ContentJsonOptions.ParseDefinedEnum<MovementType>(
                    stats.MovementType,
                    $"entityStats '{kind}' movementType");
            }

            var entityStats = new EntityStats(
                stats.MaxHealth,
                stats.AttackDamage,
                stats.AttackRange,
                stats.AttackCooldownTicks,
                stats.MoveEveryTicks,
                stats.VisionRadius,
                stats.Armor,
                projectile,
                stats.SplashRadius,
                movement);

            if (!result.TryAdd(kind, entityStats))
            {
                throw new InvalidOperationException($"Duplicate entityStats kind '{kind}'.");
            }
        }

        return result;
    }

    private static IReadOnlyList<ResistanceEntry> ParseResistances(List<ResistanceDto>? source)
    {
        if (source is null || source.Count == 0)
        {
            return Array.Empty<ResistanceEntry>();
        }

        var result = new List<ResistanceEntry>(source.Count);
        var seen = new HashSet<(ProjectileKind, CombatTargetCategory)>();
        foreach (var entry in source)
        {
            if (!Enum.TryParse<ProjectileKind>(entry.Projectile, ignoreCase: true, out var projectile)
                || !Enum.IsDefined(projectile))
            {
                throw new InvalidOperationException($"resistances has unknown projectile '{entry.Projectile}'.");
            }

            if (!Enum.TryParse<CombatTargetCategory>(entry.Category, ignoreCase: true, out var category)
                || !Enum.IsDefined(category))
            {
                throw new InvalidOperationException($"resistances has unknown category '{entry.Category}'.");
            }

            if (entry.BasisPoints < 0)
            {
                throw new InvalidOperationException(
                    $"resistances ({projectile}, {category}) must have basisPoints >= 0.");
            }

            if (!seen.Add((projectile, category)))
            {
                throw new InvalidOperationException(
                    $"Duplicate resistances entry for ({projectile}, {category}).");
            }

            result.Add(new ResistanceEntry(projectile, category, entry.BasisPoints));
        }

        return result;
    }

    private static ResearchBonusTables ParseResearchBonuses(ResearchBonusesDto? source)
    {
        if (source is null)
        {
            return ResearchBonusTables.Default;
        }

        if (source.GroundUnitAttackBonusPercent < 0)
        {
            throw new InvalidOperationException("researchBonuses.groundUnitAttackBonusPercent must be >= 0.");
        }

        if (source.GroundUnitArmorBonus < 0)
        {
            throw new InvalidOperationException("researchBonuses.groundUnitArmorBonus must be >= 0.");
        }

        return new ResearchBonusTables(source.GroundUnitAttackBonusPercent, source.GroundUnitArmorBonus);
    }

    private static IReadOnlyDictionary<ItemId, int> ParseCostLines(List<CostLineDto>? cost, string label)
    {
        if (cost is null || cost.Count == 0)
        {
            throw new InvalidOperationException($"{label} must declare a non-empty inputs cost.");
        }

        var parsed = new Dictionary<ItemId, int>();
        foreach (var line in cost)
        {
            if (!Enum.TryParse<ItemId>(line.Item, ignoreCase: true, out var item) || !Enum.IsDefined(item))
            {
                throw new InvalidOperationException($"{label} has unknown item '{line.Item}'.");
            }

            if (line.Amount <= 0)
            {
                throw new InvalidOperationException($"{label} cost for '{item}' must be > 0.");
            }

            if (!parsed.TryAdd(item, line.Amount))
            {
                throw new InvalidOperationException($"{label} has duplicate item '{item}'.");
            }
        }

        return parsed;
    }

    private static EntityKind ParseEntityKind(string raw, string section)
        => ContentJsonOptions.ParseDefinedEnum<EntityKind>(raw, $"{section} entity kind");

    private sealed class GameplayTablesDto
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, int>? PowerDemand { get; set; }
        public Dictionary<string, int>? PowerProduction { get; set; }
        public Dictionary<string, int>? ItemStackSizes { get; set; }
        public Dictionary<string, FootprintDto>? Footprints { get; set; }
        public Dictionary<string, long>? CollisionRadius { get; set; }
        public Dictionary<string, int>? TechSignatureIntensity { get; set; }
        public Dictionary<string, ProductionRecipeDto>? ProductionRecipes { get; set; }
        public Dictionary<string, ItemRecipeDto>? ItemRecipes { get; set; }
        public Dictionary<string, EntityStatsDto>? EntityStats { get; set; }
        public List<ResistanceDto>? Resistances { get; set; }
        public ResearchBonusesDto? ResearchBonuses { get; set; }
    }

    private sealed class ResearchBonusesDto
    {
        public int GroundUnitAttackBonusPercent { get; set; } = 10;
        public int GroundUnitArmorBonus { get; set; } = 1;
    }

    private sealed class FootprintDto
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private sealed class CostLineDto
    {
        public string Item { get; set; } = "";
        public int Amount { get; set; }
    }

    private sealed class ProductionRecipeDto
    {
        public List<CostLineDto>? Inputs { get; set; }
        public string OutputKind { get; set; } = "";
        public int WorkTicks { get; set; }
        public string? RequiredTechnology { get; set; }
    }

    private sealed class ItemRecipeDto
    {
        public List<CostLineDto>? Inputs { get; set; }
        public string OutputItem { get; set; } = "";
        public int OutputAmount { get; set; } = 1;
        public int WorkTicks { get; set; }
    }

    private sealed class EntityStatsDto
    {
        public int MaxHealth { get; set; }
        public int AttackDamage { get; set; }
        public int AttackRange { get; set; }
        public int AttackCooldownTicks { get; set; } = 30;
        public int MoveEveryTicks { get; set; } = 10;
        public int VisionRadius { get; set; } = 4;
        public int Armor { get; set; }
        public string? ProjectileKind { get; set; }
        public int SplashRadius { get; set; }
        public string? MovementType { get; set; }
    }

    private sealed class ResistanceDto
    {
        public string Projectile { get; set; } = "";
        public string Category { get; set; } = "";
        public int BasisPoints { get; set; }
    }
}
