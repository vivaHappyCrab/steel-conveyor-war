using System.Text.Json;
using System.Text.Json.Serialization;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Replay;

/// <summary>
/// JSON persistence for <see cref="ReplayDocument"/>. Command envelopes reuse
/// <see cref="SimulationCommandSerializer"/>; this type owns only the file header.
/// </summary>
public static class ReplayDocumentSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ReplayDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var commandsJson = SimulationCommandSerializer.SerializeMany(document.Commands);
        using var commands = JsonDocument.Parse(commandsJson);
        var dto = new ReplayFileDto
        {
            FormatVersion = document.FormatVersion,
            ProtocolVersion = document.ProtocolVersion,
            AlgorithmVersion = document.AlgorithmVersion,
            InputDelayTicks = document.InputDelayTicks,
            DurationTicks = document.DurationTicks,
            FinalStateHash = document.FinalStateHash,
            ContentManifest = document.ContentManifest,
            SessionManifest = document.SessionManifest,
            Seed = document.Seed,
            TicksPerSecond = document.TicksPerSecond,
            ProfileId = document.ProfileId,
            MapId = document.MapId,
            Commands = commands.RootElement.Clone()
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static ReplayDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ReplayFileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ReplayFileDto>(json, Options)
                ?? throw new InvalidOperationException("Replay JSON deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Replay JSON is malformed.", ex);
        }

        ValidateHeader(dto);
        if (dto.Commands.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Replay JSON is missing a commands array.");
        }

        var commands = SimulationCommandSerializer.DeserializeMany(dto.Commands.GetRawText());
        return new ReplayDocument(
            dto.FormatVersion,
            dto.ProtocolVersion,
            dto.AlgorithmVersion,
            dto.InputDelayTicks,
            dto.DurationTicks,
            dto.FinalStateHash,
            dto.ContentManifest,
            dto.SessionManifest,
            dto.Seed,
            dto.TicksPerSecond,
            dto.ProfileId,
            dto.MapId,
            commands);
    }

    private static void ValidateHeader(ReplayFileDto dto)
    {
        if (dto.DurationTicks < 0)
        {
            throw new InvalidOperationException("Replay durationTicks cannot be negative.");
        }

        if (dto.InputDelayTicks < 1)
        {
            throw new InvalidOperationException("Replay inputDelayTicks must be at least 1.");
        }

        RequireHex(dto.FinalStateHash, "finalStateHash");
        RequireHex(dto.ContentManifest, "contentManifest");
        RequireHex(dto.SessionManifest, "sessionManifest");
        if (string.IsNullOrWhiteSpace(dto.ProfileId))
        {
            throw new InvalidOperationException("Replay JSON is missing profileId.");
        }

        if (string.IsNullOrWhiteSpace(dto.MapId))
        {
            throw new InvalidOperationException("Replay JSON is missing mapId.");
        }
    }

    private static void RequireHex(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            throw new InvalidOperationException($"Replay JSON field '{name}' must be a 64-character hex digest.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return options;
    }

    private sealed class ReplayFileDto
    {
        public int FormatVersion { get; set; }
        public int ProtocolVersion { get; set; }
        public int AlgorithmVersion { get; set; }
        public int InputDelayTicks { get; set; }
        public long DurationTicks { get; set; }
        public string FinalStateHash { get; set; } = "";
        public string ContentManifest { get; set; } = "";
        public string SessionManifest { get; set; } = "";
        public int Seed { get; set; }
        public int TicksPerSecond { get; set; }
        public string ProfileId { get; set; } = "";
        public string MapId { get; set; } = "";
        public JsonElement Commands { get; set; }
    }
}
