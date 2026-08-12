namespace SteelConveyorWar.Core;

public sealed record SciencePackCost(ItemId Item, int Amount);

public sealed record ResearchCostDefinition(int EffortUnits, IReadOnlyList<SciencePackCost> SciencePacks)
{
    public IReadOnlyList<SciencePackCost> SciencePacks
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(SciencePacks);
}

public sealed record TechnologyDefinition(
    TechnologyId Id,
    string TierId,
    ResearchCostDefinition Cost,
    IReadOnlyList<ResearchEffect> Effects,
    IReadOnlyList<string> Tags,
    string DisplayName,
    string Description)
{
    public IReadOnlyList<ResearchEffect> Effects
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(Effects);

    public IReadOnlyList<string> Tags
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(Tags);
}

public sealed record TierDefinition(
    string Id,
    ItemId SciencePackItem,
    IReadOnlyList<ResearchEffect> UnlockContent)
{
    public IReadOnlyList<ResearchEffect> UnlockContent
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(UnlockContent);
}

public sealed record GateRequirementDefinition(
    string Id,
    int MinimumCompleted,
    IReadOnlyList<TechnologyId> CandidateTechnologyIds)
{
    public IReadOnlyList<TechnologyId> CandidateTechnologyIds
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(CandidateTechnologyIds);
}

public sealed record TierGateDefinition(
    string Id,
    string FromTierId,
    string? TargetTierId,
    IReadOnlyList<GateRequirementDefinition> Requirements,
    IReadOnlyList<ResearchEffect> CompletionEffects)
{
    public IReadOnlyList<GateRequirementDefinition> Requirements
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(Requirements);

    public IReadOnlyList<ResearchEffect> CompletionEffects
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(CompletionEffects);
}

public sealed record ResearchTrackDefinition(
    string Id,
    ResearchTrackMode Mode,
    int MaximumActiveProjects,
    IReadOnlyList<string> SourceKinds,
    int DefaultWeight)
{
    public IReadOnlyList<string> SourceKinds
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(SourceKinds);
}

public sealed record ExclusiveGroupDefinition(
    string Id,
    IReadOnlyList<TechnologyId> MemberTechnologyIds,
    int MaximumSelections,
    ExclusiveLockOn LockOn,
    bool ConfirmationRequired)
{
    public IReadOnlyList<TechnologyId> MemberTechnologyIds
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(MemberTechnologyIds);
}

public sealed record OptionalPoolDefinition(
    string Id,
    IReadOnlyList<TechnologyId> TechnologyIds)
{
    public IReadOnlyList<TechnologyId> TechnologyIds
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(TechnologyIds);
}

public sealed record ResearchBudgetDefinition(
    int Scale,
    IReadOnlyDictionary<string, int> DefaultAllocations,
    bool PlayerAdjustable)
{
    public IReadOnlyDictionary<string, int> DefaultAllocations
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value, StringComparer.Ordinal);
    } = ContentFreeze.Dictionary(DefaultAllocations, StringComparer.Ordinal);
}

public sealed record ResearchScheduleDefinition(
    IReadOnlyList<ResearchTrackDefinition> Tracks,
    ResearchBudgetDefinition Budget)
{
    public IReadOnlyList<ResearchTrackDefinition> Tracks
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(Tracks);
}

public sealed record ResearchProfileDefinition(
    string Id,
    ResearchScheduleDefinition Schedule,
    IReadOnlyDictionary<string, TierGateDefinition> GatesByFromTier,
    IReadOnlyList<ExclusiveGroupDefinition> ExclusiveGroups,
    IReadOnlyList<OptionalPoolDefinition> OptionalPools,
    int CatchUpMultiplierBasisPoints)
{
    public IReadOnlyDictionary<string, TierGateDefinition> GatesByFromTier
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value, StringComparer.Ordinal);
    } = ContentFreeze.Dictionary(GatesByFromTier, StringComparer.Ordinal);

    public IReadOnlyList<ExclusiveGroupDefinition> ExclusiveGroups
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(ExclusiveGroups);

    public IReadOnlyList<OptionalPoolDefinition> OptionalPools
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(OptionalPools);
}

public sealed record ResearchCatalog(
    IReadOnlyDictionary<TechnologyId, TechnologyDefinition> Technologies,
    IReadOnlyDictionary<string, TierDefinition> Tiers,
    IReadOnlyDictionary<string, ResearchProfileDefinition> Profiles,
    IReadOnlyList<string> BaselineCapabilities,
    string ContentHash)
{
    // H07: freeze top-level research graphs; nested collections freeze via definition property init.
    public IReadOnlyDictionary<TechnologyId, TechnologyDefinition> Technologies
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(Technologies);

    public IReadOnlyDictionary<string, TierDefinition> Tiers
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value, StringComparer.Ordinal);
    } = ContentFreeze.Dictionary(Tiers, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ResearchProfileDefinition> Profiles
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value, StringComparer.Ordinal);
    } = ContentFreeze.Dictionary(Profiles, StringComparer.Ordinal);

    public IReadOnlyList<string> BaselineCapabilities
    {
        get => field!;
        init => field = ContentFreeze.List(value);
    } = ContentFreeze.List(BaselineCapabilities);
}

public sealed record GameCreationOptions(
    int RandomSeed,
    string ProfileId,
    ResearchCatalog Catalog,
    TileCatalog Tiles,
    EntityCatalog Entities,
    MapSettings? Map = null,
    BuildCostCatalog? BuildCosts = null,
    GameplayTablesCatalog? GameplayTables = null,
    // R32: per-match tick rate that drives all Core duration-in-ticks conversions.
    // Defaults to GameSimulation.DefaultTicksPerSecond; hosts thread GameSettings.TicksPerSecond here.
    int TicksPerSecond = GameSimulation.DefaultTicksPerSecond)
{
    public GameCreationOptions(int randomSeed, string profileId, ResearchCatalog catalog)
        : this(randomSeed, profileId, catalog, TileCatalog.Empty, EntityCatalog.Empty, null, null)
    {
    }

    public MapSettings ResolvedMap => Map ?? MapSettings.Default1v1;

    public BuildCostCatalog ResolvedBuildCosts => BuildCosts ?? MvpBuildCostCatalog.Embedded;

    public GameplayTablesCatalog ResolvedGameplayTables =>
        GameplayTables ?? GameplayTablesCatalog.Embedded;

    public static GameCreationOptions Default => new(
        RandomSeed: 1,
        ProfileId: ResearchProfileIds.MvpB,
        Catalog: MvpResearchCatalog.CreateEmbedded(),
        Tiles: TileCatalog.Empty,
        Entities: EntityCatalog.Empty,
        Map: MapSettings.Default1v1,
        BuildCosts: MvpBuildCostCatalog.Embedded,
        GameplayTables: GameplayTablesCatalog.Embedded);
}
