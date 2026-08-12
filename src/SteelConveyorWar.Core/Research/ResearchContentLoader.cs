using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteelConveyorWar.Core;

public static class ResearchContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = ContentJsonOptions.CreateStrict(includeStringEnums: true);

    public static string Serialize(ResearchCatalog catalog)
    {
        var dto = ToDto(catalog);
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        });
    }

    public static ResearchCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<ResearchCatalogDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Research catalog JSON deserialized to null.");

        ContentSchema.RequireSupportedVersion("research", dto.SchemaVersion);

        var technologies = dto.Technologies.ToDictionary(
            tech => new TechnologyId(tech.Id),
            tech =>
            {
                var id = new TechnologyId(tech.Id);
                var tags = tech.Tags ?? new List<string>();
                return new TechnologyDefinition(
                    id,
                    tech.TierId,
                    new ResearchCostDefinition(
                        tech.Cost.EffortUnits,
                        tech.Cost.SciencePacks.Select(pack => new SciencePackCost(ParseItemId(pack.Item), pack.Amount)).ToList()),
                    tech.Effects.Select(ParseEffect).ToList(),
                    tags,
                    string.IsNullOrWhiteSpace(tech.DisplayName) ? ResearchDisplayNames.GetDisplayName(id) : tech.DisplayName,
                    string.IsNullOrWhiteSpace(tech.Description) ? ResearchDisplayNames.GetDescription(id, tags) : tech.Description);
            });

        var tiers = dto.Tiers.ToDictionary(
            tier => tier.Id,
            tier => new TierDefinition(
                tier.Id,
                ParseItemId(tier.SciencePackItem),
                (tier.UnlockContent ?? new List<EffectDto>()).Select(ParseEffect).ToList()));

        var profiles = dto.Profiles.ToDictionary(
            profile => profile.Id,
            profile => new ResearchProfileDefinition(
                profile.Id,
                new ResearchScheduleDefinition(
                    profile.Schedule.Tracks.Select(track => new ResearchTrackDefinition(
                        track.Id,
                        Enum.Parse<ResearchTrackMode>(track.Mode, ignoreCase: true),
                        track.MaximumActiveProjects,
                        track.SourceKinds ?? new List<string>(),
                        track.DefaultWeight)).ToList(),
                    new ResearchBudgetDefinition(
                        profile.Schedule.Budget.Scale,
                        profile.Schedule.Budget.DefaultAllocations,
                        profile.Schedule.Budget.PlayerAdjustable)),
                profile.Gates.ToDictionary(
                    gate => gate.FromTierId,
                    gate => new TierGateDefinition(
                        gate.Id,
                        gate.FromTierId,
                        gate.TargetTierId,
                        gate.Requirements.Select(requirement => new GateRequirementDefinition(
                            requirement.Id,
                            requirement.MinimumCompleted,
                            requirement.CandidateTechnologyIds.Select(id => new TechnologyId(id)).ToList())).ToList(),
                        (gate.CompletionEffects ?? new List<EffectDto>()).Select(ParseEffect).ToList())),
                (profile.ExclusiveGroups ?? new List<ExclusiveGroupDto>()).Select(group => new ExclusiveGroupDefinition(
                    group.Id,
                    group.MemberTechnologyIds.Select(id => new TechnologyId(id)).ToList(),
                    group.MaximumSelections,
                    Enum.Parse<ExclusiveLockOn>(group.LockOn, ignoreCase: true),
                    group.ConfirmationRequired)).ToList(),
                (profile.OptionalPools ?? new List<OptionalPoolDto>()).Select(pool => new OptionalPoolDefinition(
                    pool.Id,
                    pool.TechnologyIds.Select(id => new TechnologyId(id)).ToList())).ToList(),
                profile.CatchUpMultiplierBasisPoints));

        var catalog = new ResearchCatalog(
            technologies,
            tiers,
            profiles,
            dto.BaselineCapabilities ?? new List<string>(),
            ComputeContentHash(dto));

        ResearchContentValidator.Validate(catalog);
        return catalog;
    }

    public static string ComputeContentHash(string json)
    {
        var dto = JsonSerializer.Deserialize<ResearchCatalogDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Research catalog JSON deserialized to null.");
        return ComputeContentHash(dto);
    }

    private static string ComputeContentHash(ResearchCatalogDto dto)
    {
        var canonical = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ResearchEffect ParseEffect(EffectDto dto)
    {
        return dto.Type.ToLowerInvariant() switch
        {
            "unlockcontent" => ParseUnlock(dto),
            "grantcapability" => new GrantCapabilityEffect(dto.CapabilityId ?? throw Missing("capabilityId")),
            "addmodifier" => new AddModifierEffect(
                dto.StatId ?? throw Missing("statId"),
                Enum.Parse<ModifierOperation>(dto.Operation ?? "Multiply", ignoreCase: true),
                dto.ValueBasisPoints,
                dto.Selector),
            "completemilestone" => new CompleteMilestoneEffect(dto.MilestoneId ?? throw Missing("milestoneId")),
            _ => throw new InvalidOperationException($"Unknown research effect type '{dto.Type}'.")
        };
    }

    private static UnlockContentEffect ParseUnlock(EffectDto dto)
    {
        return new UnlockContentEffect(
            dto.ContentKind ?? throw Missing("contentKind"),
            dto.ContentId ?? throw Missing("contentId"));
    }

    private static ItemId ParseItemId(string value)
        => ContentJsonOptions.ParseDefinedEnum<ItemId>(value, "science pack item");

    private static Exception Missing(string name) => new InvalidOperationException($"Missing effect field '{name}'.");

    private static ResearchCatalogDto ToDto(ResearchCatalog catalog)
    {
        return new ResearchCatalogDto
        {
            SchemaVersion = ContentSchema.CurrentVersion,
            Technologies = catalog.Technologies.Values
                .OrderBy(tech => tech.Id.Value, StringComparer.Ordinal)
                .Select(tech => new TechnologyDto
                {
                    Id = tech.Id.Value,
                    TierId = tech.TierId,
                    DisplayName = tech.DisplayName,
                    Description = tech.Description,
                    Cost = new CostDto
                    {
                        EffortUnits = tech.Cost.EffortUnits,
                        SciencePacks = tech.Cost.SciencePacks
                            .Select(pack => new PackDto { Item = pack.Item.ToString(), Amount = pack.Amount })
                            .ToList()
                    },
                    Effects = tech.Effects.Select(ToEffectDto).ToList(),
                    Tags = tech.Tags.ToList()
                })
                .ToList(),
            Tiers = catalog.Tiers.Values
                .OrderBy(tier => tier.Id, StringComparer.Ordinal)
                .Select(tier => new TierDto
                {
                    Id = tier.Id,
                    SciencePackItem = tier.SciencePackItem.ToString(),
                    UnlockContent = tier.UnlockContent.Select(ToEffectDto).ToList()
                })
                .ToList(),
            Profiles = catalog.Profiles.Values
                .OrderBy(profile => profile.Id, StringComparer.Ordinal)
                .Select(profile => new ProfileDto
                {
                    Id = profile.Id,
                    Schedule = new ScheduleDto
                    {
                        Tracks = profile.Schedule.Tracks.Select(track => new TrackDto
                        {
                            Id = track.Id,
                            Mode = track.Mode.ToString(),
                            MaximumActiveProjects = track.MaximumActiveProjects,
                            SourceKinds = track.SourceKinds.ToList(),
                            DefaultWeight = track.DefaultWeight
                        }).ToList(),
                        Budget = new BudgetDto
                        {
                            Scale = profile.Schedule.Budget.Scale,
                            DefaultAllocations = profile.Schedule.Budget.DefaultAllocations
                                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                .ToDictionary(pair => pair.Key, pair => pair.Value),
                            PlayerAdjustable = profile.Schedule.Budget.PlayerAdjustable
                        }
                    },
                    Gates = profile.GatesByFromTier.Values
                        .OrderBy(gate => gate.FromTierId, StringComparer.Ordinal)
                        .Select(gate => new GateDto
                        {
                            Id = gate.Id,
                            FromTierId = gate.FromTierId,
                            TargetTierId = gate.TargetTierId,
                            Requirements = gate.Requirements.Select(requirement => new RequirementDto
                            {
                                Id = requirement.Id,
                                MinimumCompleted = requirement.MinimumCompleted,
                                CandidateTechnologyIds = requirement.CandidateTechnologyIds
                                    .Select(id => id.Value)
                                    .OrderBy(id => id, StringComparer.Ordinal)
                                    .ToList()
                            }).ToList(),
                            CompletionEffects = gate.CompletionEffects.Select(ToEffectDto).ToList()
                        })
                        .ToList(),
                    ExclusiveGroups = profile.ExclusiveGroups.Select(group => new ExclusiveGroupDto
                    {
                        Id = group.Id,
                        MemberTechnologyIds = group.MemberTechnologyIds.Select(id => id.Value).ToList(),
                        MaximumSelections = group.MaximumSelections,
                        LockOn = group.LockOn.ToString(),
                        ConfirmationRequired = group.ConfirmationRequired
                    }).ToList(),
                    OptionalPools = profile.OptionalPools.Select(pool => new OptionalPoolDto
                    {
                        Id = pool.Id,
                        TechnologyIds = pool.TechnologyIds.Select(id => id.Value).ToList()
                    }).ToList(),
                    CatchUpMultiplierBasisPoints = profile.CatchUpMultiplierBasisPoints
                })
                .ToList(),
            BaselineCapabilities = catalog.BaselineCapabilities.ToList()
        };
    }

    private static EffectDto ToEffectDto(ResearchEffect effect)
    {
        return effect switch
        {
            UnlockContentEffect unlock => new EffectDto
            {
                Type = "unlockContent",
                ContentKind = unlock.ContentKind,
                ContentId = unlock.ContentId
            },
            GrantCapabilityEffect grant => new EffectDto
            {
                Type = "grantCapability",
                CapabilityId = grant.CapabilityId
            },
            AddModifierEffect modifier => new EffectDto
            {
                Type = "addModifier",
                StatId = modifier.StatId,
                Operation = modifier.Operation.ToString(),
                ValueBasisPoints = modifier.ValueBasisPoints,
                Selector = modifier.Selector
            },
            CompleteMilestoneEffect milestone => new EffectDto
            {
                Type = "completeMilestone",
                MilestoneId = milestone.MilestoneId
            },
            _ => throw new InvalidOperationException($"Unsupported effect type '{effect.GetType().Name}'.")
        };
    }

    private sealed class ResearchCatalogDto
    {
        public int SchemaVersion { get; set; }
        public List<TechnologyDto> Technologies { get; set; } = new();
        public List<TierDto> Tiers { get; set; } = new();
        public List<ProfileDto> Profiles { get; set; } = new();
        public List<string>? BaselineCapabilities { get; set; }
    }

    private sealed class TechnologyDto
    {
        public string Id { get; set; } = "";
        public string TierId { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public CostDto Cost { get; set; } = new();
        public List<EffectDto> Effects { get; set; } = new();
        public List<string>? Tags { get; set; }
    }

    private sealed class CostDto
    {
        public int EffortUnits { get; set; }
        public List<PackDto> SciencePacks { get; set; } = new();
    }

    private sealed class PackDto
    {
        public string Item { get; set; } = "";
        public int Amount { get; set; } = 1;
    }

    private sealed class EffectDto
    {
        public string Type { get; set; } = "";
        public string? ContentKind { get; set; }
        public string? ContentId { get; set; }
        public string? CapabilityId { get; set; }
        public string? StatId { get; set; }
        public string? Operation { get; set; }
        public int ValueBasisPoints { get; set; }
        public string? Selector { get; set; }
        public string? MilestoneId { get; set; }
    }

    private sealed class TierDto
    {
        public string Id { get; set; } = "";
        public string SciencePackItem { get; set; } = "";
        public List<EffectDto>? UnlockContent { get; set; }
    }

    private sealed class ProfileDto
    {
        public string Id { get; set; } = "";
        public ScheduleDto Schedule { get; set; } = new();
        public List<GateDto> Gates { get; set; } = new();
        public List<ExclusiveGroupDto>? ExclusiveGroups { get; set; }
        public List<OptionalPoolDto>? OptionalPools { get; set; }
        public int CatchUpMultiplierBasisPoints { get; set; } = 15_000;
    }

    private sealed class ScheduleDto
    {
        public List<TrackDto> Tracks { get; set; } = new();
        public BudgetDto Budget { get; set; } = new();
    }

    private sealed class TrackDto
    {
        public string Id { get; set; } = "";
        public string Mode { get; set; } = "Serial";
        public int MaximumActiveProjects { get; set; } = 1;
        public List<string>? SourceKinds { get; set; }
        public int DefaultWeight { get; set; } = 100;
    }

    private sealed class BudgetDto
    {
        public int Scale { get; set; } = 10_000;
        public Dictionary<string, int> DefaultAllocations { get; set; } = new();
        public bool PlayerAdjustable { get; set; }
    }

    private sealed class GateDto
    {
        public string Id { get; set; } = "";
        public string FromTierId { get; set; } = "";
        public string? TargetTierId { get; set; }
        public List<RequirementDto> Requirements { get; set; } = new();
        public List<EffectDto>? CompletionEffects { get; set; }
    }

    private sealed class RequirementDto
    {
        public string Id { get; set; } = "";
        public int MinimumCompleted { get; set; }
        public List<string> CandidateTechnologyIds { get; set; } = new();
    }

    private sealed class ExclusiveGroupDto
    {
        public string Id { get; set; } = "";
        public List<string> MemberTechnologyIds { get; set; } = new();
        public int MaximumSelections { get; set; } = 1;
        public string LockOn { get; set; } = "Complete";
        public bool ConfirmationRequired { get; set; } = true;
    }

    private sealed class OptionalPoolDto
    {
        public string Id { get; set; } = "";
        public List<string> TechnologyIds { get; set; } = new();
    }
}
