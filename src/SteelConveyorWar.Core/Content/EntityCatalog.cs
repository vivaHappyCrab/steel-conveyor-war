namespace SteelConveyorWar.Core;

public sealed record EntityDefinition(
    string Id,
    string Name,
    string Kind,
    bool BuildsStructures,
    string? LossCondition);

public sealed record EntityCatalog(
    int SchemaVersion,
    IReadOnlyDictionary<string, EntityDefinition> Entities)
{
    // H07: freeze at construction / `with` so public graphs cannot be cast-mutated.
    public IReadOnlyDictionary<string, EntityDefinition> Entities
    {
        get => field!;
        init => field = ContentFreeze.Dictionary(value, StringComparer.Ordinal);
    } = ContentFreeze.Dictionary(Entities, StringComparer.Ordinal);

    public static EntityCatalog Empty { get; } = new(1, new Dictionary<string, EntityDefinition>(StringComparer.Ordinal));

    /// <summary>
    /// Entity kinds whose <see cref="EntityDefinition.LossCondition"/> is <c>Defeat</c>.
    /// Empty when the catalog declares no defeat loss conditions (or is <see cref="Empty"/>).
    /// Callers that need MVP fallback when the catalog is empty should supply it themselves.
    /// </summary>
    public IReadOnlySet<EntityKind> GetDefeatLossKinds()
    {
        var kinds = new HashSet<EntityKind>();
        foreach (var definition in Entities.Values)
        {
            if (!string.Equals(definition.LossCondition, "Defeat", StringComparison.Ordinal))
            {
                continue;
            }

            if (Enum.TryParse<EntityKind>(definition.Kind, ignoreCase: false, out var kind))
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }
}
