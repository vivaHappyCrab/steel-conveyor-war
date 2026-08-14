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
    IReadOnlyList<ResistanceEntry> Resistances,
    ResearchBonusTables ResearchBonuses)
{
    // H07: freeze table graphs (including after H01 match-scoped wiring). Nested recipe Inputs are
    // frozen by ProductionRecipe / ItemRecipeDefinition property init.
    public IReadOnlyDictionary<EntityKind, int> PowerDemand
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(PowerDemand);

    public IReadOnlyDictionary<EntityKind, int> PowerProduction
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(PowerProduction);

    public IReadOnlyDictionary<ItemId, int> ItemStackSizes
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(ItemStackSizes);

    public IReadOnlyDictionary<EntityKind, WorldSize> Footprints
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(Footprints);

    public IReadOnlyDictionary<EntityKind, long> CollisionRadius
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(CollisionRadius);

    public IReadOnlyDictionary<EntityKind, int> TechSignatureIntensity
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(TechSignatureIntensity);

    public IReadOnlyDictionary<EntityKind, ProductionRecipe> ProductionRecipes
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(ProductionRecipes);

    public IReadOnlyDictionary<ItemRecipeId, ItemRecipeDefinition> ItemRecipes
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(ItemRecipes);

    public IReadOnlyDictionary<EntityKind, EntityStats> EntityStats
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(EntityStats);

    public IReadOnlyList<ResistanceEntry> Resistances
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(Resistances);

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
        Resistances: Array.Empty<ResistanceEntry>(),
        ResearchBonuses: ResearchBonusTables.Default);

    public EntityStats GetStats(EntityKind kind) =>
        EntityStats.TryGetValue(kind, out var stats)
            ? stats
            : new EntityStats(60, VisionRadius: 2, Armor: 1);

    public WorldSize GetFootprint(EntityKind kind) =>
        Footprints.TryGetValue(kind, out var size) ? size : new WorldSize(1, 1);

    public CollisionSize GetCollisionSize(EntityKind kind) =>
        new(CollisionRadius.GetValueOrDefault(kind));

    public int GetPowerDemand(EntityKind kind) => PowerDemand.GetValueOrDefault(kind);

    /// <summary>Per-building energy buffer capacity = demand × <see cref="MvpDefinitions.EnergyBufferCapacityFactor"/>.</summary>
    public int GetEnergyBufferCapacity(EntityKind kind)
    {
        var demand = GetPowerDemand(kind);
        return demand <= 0 ? 0 : demand * MvpDefinitions.EnergyBufferCapacityFactor;
    }

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

/// <summary>
/// Tuneable T1 optional combat/bastion bonus magnitudes. Attack and armor are absolute adds
/// per ground combat unit. Starter attack is 10% of that unit's default (rounded half-up);
/// starter armor is +1.
/// </summary>
public readonly record struct ResearchBonusTables(
    IReadOnlyDictionary<EntityKind, int> GroundUnitAttackBonus,
    IReadOnlyDictionary<EntityKind, int> GroundUnitArmorBonus)
{
    public IReadOnlyDictionary<EntityKind, int> GroundUnitAttackBonus
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(GroundUnitAttackBonus);

    public IReadOnlyDictionary<EntityKind, int> GroundUnitArmorBonus
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(GroundUnitArmorBonus);

    public static ResearchBonusTables Default { get; } = new(
        new Dictionary<EntityKind, int>(),
        new Dictionary<EntityKind, int>());

    public int AttackAddBasisPoints(EntityKind kind) =>
        GroundUnitAttackBonus.GetValueOrDefault(kind) * ModifierResolver.BasisPointsScale;

    public int ArmorAddBasisPoints(EntityKind kind) =>
        GroundUnitArmorBonus.GetValueOrDefault(kind) * ModifierResolver.BasisPointsScale;

    /// <summary>Starter absolute attack bonus: 10% of base attack, rounded half away from zero.</summary>
    public static int TenPercentOfAttack(int attackDamage) =>
        attackDamage <= 0 ? 0 : (attackDamage + 5) / 10;

    public const int DefaultArmorBonus = 1;
}
