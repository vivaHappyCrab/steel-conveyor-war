namespace SteelConveyorWar.Core;

/// <summary>
/// R18: versioned gameplay balance tables (power, stacks, footprints, recipes, combat stats, resistances).
/// Hosts load <c>config/gameplay-tables.json</c>; unit tests use <see cref="Embedded"/>.
/// </summary>
public sealed partial record GameplayTablesCatalog(
    int SchemaVersion,
    IReadOnlyDictionary<EntityKind, int> PowerDemand,
    IReadOnlyDictionary<EntityKind, int> PowerProduction,
    IReadOnlyDictionary<ItemId, int> ItemStackSizes,
    IReadOnlyDictionary<EntityKind, WorldSize> Footprints,
    IReadOnlyDictionary<EntityKind, long> CollisionRadius,
    IReadOnlyDictionary<EntityKind, int> TechSignatureIntensity,
    IReadOnlyDictionary<EntityKind, ProductionRecipe> ProductionRecipes,
    IReadOnlyDictionary<ItemRecipeId, ItemRecipeDefinition> ItemRecipes,
    IReadOnlyDictionary<EntityKind, EntityStats> EntityStats,
    IReadOnlyList<ResistanceEntry> Resistances)
{
    public static GameplayTablesCatalog Empty { get; } = new(
        SchemaVersion: 1,
        PowerDemand: new Dictionary<EntityKind, int>(),
        PowerProduction: new Dictionary<EntityKind, int>(),
        ItemStackSizes: new Dictionary<ItemId, int>(),
        Footprints: new Dictionary<EntityKind, WorldSize>(),
        CollisionRadius: new Dictionary<EntityKind, long>(),
        TechSignatureIntensity: new Dictionary<EntityKind, int>(),
        ProductionRecipes: new Dictionary<EntityKind, ProductionRecipe>(),
        ItemRecipes: new Dictionary<ItemRecipeId, ItemRecipeDefinition>(),
        EntityStats: new Dictionary<EntityKind, EntityStats>(),
        Resistances: Array.Empty<ResistanceEntry>());

    public EntityStats GetStats(EntityKind kind) =>
        EntityStats.TryGetValue(kind, out var stats)
            ? stats
            : new EntityStats(60, VisionRadius: 2, Armor: 1);

    public WorldSize GetFootprint(EntityKind kind) =>
        Footprints.TryGetValue(kind, out var size) ? size : new WorldSize(1, 1);

    public CollisionSize GetCollisionSize(EntityKind kind) =>
        new(CollisionRadius.GetValueOrDefault(kind));

    public int GetPowerDemand(EntityKind kind) => PowerDemand.GetValueOrDefault(kind);

    public int GetMaxStackSize(ItemId item) => ItemStackSizes.GetValueOrDefault(item, 50);

    /// <summary>Resistance multipliers in basis points (10_000 = 1.0). Missing pairs default to 10_000.</summary>
    public int GetResistanceBasisPoints(ProjectileKind projectileKind, CombatTargetCategory targetCategory)
    {
        foreach (var entry in Resistances)
        {
            if (entry.Projectile == projectileKind && entry.Category == targetCategory)
            {
                return entry.BasisPoints;
            }
        }

        return 10_000;
    }
}

/// <summary>One ProjectileKind × CombatTargetCategory resistance multiplier in basis points (10_000 = 1.0).</summary>
public readonly record struct ResistanceEntry(
    ProjectileKind Projectile,
    CombatTargetCategory Category,
    int BasisPoints);
