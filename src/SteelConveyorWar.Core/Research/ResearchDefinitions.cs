namespace SteelConveyorWar.Core;

public sealed record SciencePackCost(ItemId Item, int Amount);

public sealed record ResearchCostDefinition(int EffortUnits, IReadOnlyList<SciencePackCost> SciencePacks);

public sealed record TechnologyDefinition(
    TechnologyId Id,
    string TierId,
    ResearchCostDefinition Cost,
    IReadOnlyList<ResearchEffect> Effects,
    IReadOnlyList<string> Tags,
    string DisplayName,
    string Description);

public sealed record TierDefinition(
    string Id,
    ItemId SciencePackItem,
    IReadOnlyList<ResearchEffect> UnlockContent);

public sealed record GateRequirementDefinition(
    string Id,
    int MinimumCompleted,
    IReadOnlyList<TechnologyId> CandidateTechnologyIds);

public sealed record TierGateDefinition(
    string Id,
    string FromTierId,
    string? TargetTierId,
    IReadOnlyList<GateRequirementDefinition> Requirements,
    IReadOnlyList<ResearchEffect> CompletionEffects);

public sealed record ResearchTrackDefinition(
    string Id,
    ResearchTrackMode Mode,
    int MaximumActiveProjects,
    IReadOnlyList<string> SourceKinds,
    int DefaultWeight);

public sealed record ExclusiveGroupDefinition(
    string Id,
    IReadOnlyList<TechnologyId> MemberTechnologyIds,
    int MaximumSelections,
    ExclusiveLockOn LockOn,
    bool ConfirmationRequired);

public sealed record OptionalPoolDefinition(
    string Id,
    IReadOnlyList<TechnologyId> TechnologyIds);

public sealed record ResearchBudgetDefinition(
    int Scale,
    IReadOnlyDictionary<string, int> DefaultAllocations,
    bool PlayerAdjustable);

public sealed record ResearchScheduleDefinition(
    IReadOnlyList<ResearchTrackDefinition> Tracks,
    ResearchBudgetDefinition Budget);

public sealed record ResearchProfileDefinition(
    string Id,
    ResearchScheduleDefinition Schedule,
    IReadOnlyDictionary<string, TierGateDefinition> GatesByFromTier,
    IReadOnlyList<ExclusiveGroupDefinition> ExclusiveGroups,
    IReadOnlyList<OptionalPoolDefinition> OptionalPools,
    int CatchUpMultiplierBasisPoints);

public sealed record ResearchCatalog(
    IReadOnlyDictionary<TechnologyId, TechnologyDefinition> Technologies,
    IReadOnlyDictionary<string, TierDefinition> Tiers,
    IReadOnlyDictionary<string, ResearchProfileDefinition> Profiles,
    IReadOnlyList<string> BaselineCapabilities,
    string ContentHash);

public sealed record GameCreationOptions(
    int RandomSeed,
    string ProfileId,
    ResearchCatalog Catalog,
    TileCatalog Tiles,
    EntityCatalog Entities,
    MapSettings? Map = null)
{
    public GameCreationOptions(int randomSeed, string profileId, ResearchCatalog catalog)
        : this(randomSeed, profileId, catalog, TileCatalog.Empty, EntityCatalog.Empty, null)
    {
    }

    public MapSettings ResolvedMap => Map ?? MapSettings.Default1v1;

    public static GameCreationOptions Default => new(
        RandomSeed: 1,
        ProfileId: ResearchProfileIds.MvpB,
        Catalog: MvpResearchCatalog.CreateEmbedded(),
        Tiles: TileCatalog.Empty,
        Entities: EntityCatalog.Empty,
        Map: MapSettings.Default1v1);
}
