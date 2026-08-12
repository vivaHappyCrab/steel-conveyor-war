namespace SteelConveyorWar.Core;

/// <summary>
/// Construction balance: item costs, ghost-build duration, and optional tech gates per placeable <see cref="EntityKind"/>.
/// </summary>
public sealed record BuildCostCatalog(
    int SchemaVersion,
    IReadOnlyDictionary<EntityKind, IReadOnlyDictionary<ItemId, int>> Costs,
    IReadOnlyDictionary<EntityKind, int> BuildTicks,
    IReadOnlyDictionary<EntityKind, TechnologyId> Requirements)
{
    // H07: deep-freeze root maps and nested cost maps (also on record `with`).
    public IReadOnlyDictionary<EntityKind, IReadOnlyDictionary<ItemId, int>> Costs
    {
        get => field!;
        init => field = ContentFreeze.NestedDictionary(value);
    } = ContentFreeze.NestedDictionary(Costs);

    public IReadOnlyDictionary<EntityKind, int> BuildTicks
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(BuildTicks);

    public IReadOnlyDictionary<EntityKind, TechnologyId> Requirements
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value);
    } = ContentFreeze.Dictionary(Requirements);

    public static BuildCostCatalog Empty { get; } = new(
        SchemaVersion: 1,
        Costs: new Dictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>(),
        BuildTicks: new Dictionary<EntityKind, int>(),
        Requirements: new Dictionary<EntityKind, TechnologyId>());
}
