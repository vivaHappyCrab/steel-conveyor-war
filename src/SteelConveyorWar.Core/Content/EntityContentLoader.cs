using System.Text.Json;

namespace SteelConveyorWar.Core;

public static class EntityContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static EntityCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<EntityCatalogDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Entity catalog JSON deserialized to null.");

        ContentSchema.RequireSupportedVersion("entities", dto.SchemaVersion);

        if (dto.Entities is null || dto.Entities.Count == 0)
        {
            throw new InvalidOperationException("Entity catalog must declare at least one entity.");
        }

        var entities = new Dictionary<string, EntityDefinition>(StringComparer.Ordinal);
        foreach (var entity in dto.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Id))
            {
                throw new InvalidOperationException("Entity entry is missing an id.");
            }

            if (string.IsNullOrWhiteSpace(entity.Name))
            {
                throw new InvalidOperationException($"Entity '{entity.Id}' is missing a name.");
            }

            if (string.IsNullOrWhiteSpace(entity.Kind))
            {
                throw new InvalidOperationException($"Entity '{entity.Id}' is missing a kind.");
            }

            if (!Enum.TryParse<EntityKind>(entity.Kind, ignoreCase: false, out _))
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Id}' has unknown kind '{entity.Kind}' (must match EntityKind).");
            }

            if (entity.LossCondition is not null
                && !string.Equals(entity.LossCondition, "Defeat", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Id}' has unsupported lossCondition '{entity.LossCondition}' (supported: Defeat).");
            }

            if (!entities.TryAdd(
                    entity.Id,
                    new EntityDefinition(
                        entity.Id,
                        entity.Name,
                        entity.Kind,
                        entity.BuildsStructures,
                        entity.LossCondition)))
            {
                throw new InvalidOperationException($"Duplicate entity id '{entity.Id}'.");
            }
        }

        return new EntityCatalog(dto.SchemaVersion, entities);
    }

    private sealed class EntityCatalogDto
    {
        public int SchemaVersion { get; set; }
        public List<EntityDto>? Entities { get; set; }
    }

    private sealed class EntityDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public bool BuildsStructures { get; set; }
        public string? LossCondition { get; set; }
    }
}
