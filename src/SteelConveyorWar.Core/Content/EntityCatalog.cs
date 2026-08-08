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
    public static EntityCatalog Empty { get; } = new(1, new Dictionary<string, EntityDefinition>());
}
