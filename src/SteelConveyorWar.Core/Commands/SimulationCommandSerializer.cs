using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// JSON stub for command wire/log shape used by tests and future lockstep/replay hosts.
/// Not a network protocol — field names and kinds are the contract to stabilize.
/// </summary>
/// <remarks>
/// Envelope shape:
/// <code>
/// {
///   "kind": "IssueMove",
///   "actor": 1,
///   "tick": 42,
///   "payload": { ... kind-specific fields ... }
/// }
/// </code>
/// Enums serialize as strings. <see cref="TechnologyId"/> as its string value.
/// Bastion waypoints are ordered tile arrays. Track allocations are ordered by track id on write.
/// </remarks>
public static class SimulationCommandSerializer
{
    /// <summary>
    /// R11/M02: current command wire protocol version. Bump on any breaking envelope/payload change and
    /// widen <see cref="MinSupportedProtocolVersion"/> only when older shapes remain decodable.
    /// v2: <c>SetProjectWeight</c> added; <c>AssignFactoryBastion</c> removed from wire vocabulary.
    /// v3: <c>SetInserterReach</c> added; build commands carry optional <c>inserterLongReach</c>.
    /// </summary>
    public const int ProtocolVersion = 3;

    /// <summary>
    /// Oldest protocol version this build can still decode. v1 envelopes remain decodable for kinds
    /// that still exist; obsolete kinds are rejected by kind regardless of version.
    /// </summary>
    public const int MinSupportedProtocolVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return JsonSerializer.Serialize(ToDto(command), Options);
    }

    public static ISimulationCommand Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var dto = JsonSerializer.Deserialize<CommandEnvelopeDto>(json, Options)
            ?? throw new InvalidOperationException("Command JSON deserialized to null.");
        return FromDto(dto);
    }

    public static string SerializeMany(IEnumerable<ISimulationCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var envelopes = commands.Select(ToDto).ToArray();
        return JsonSerializer.Serialize(envelopes, Options);
    }

    public static IReadOnlyList<ISimulationCommand> DeserializeMany(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var envelopes = JsonSerializer.Deserialize<CommandEnvelopeDto[]>(json, Options)
            ?? throw new InvalidOperationException("Command list JSON deserialized to null.");
        return envelopes.Select(FromDto).ToArray();
    }

    /// <summary>
    /// R09: no-throw deserialization for untrusted wire input. Returns <c>false</c> on any
    /// malformed payload (invalid JSON, missing required fields, unknown kind) instead of throwing.
    /// </summary>
    public static bool TryDeserialize(string? json, out ISimulationCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            command = Deserialize(json);
            return true;
        }
        catch (Exception)
        {
            command = null;
            return false;
        }
    }

    /// <summary>R09: no-throw batch deserialization for untrusted wire input.</summary>
    public static bool TryDeserializeMany(string? json, out IReadOnlyList<ISimulationCommand> commands)
    {
        commands = Array.Empty<ISimulationCommand>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            commands = DeserializeMany(json);
            return true;
        }
        catch (Exception)
        {
            commands = Array.Empty<ISimulationCommand>();
            return false;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static CommandEnvelopeDto ToDto(ISimulationCommand command)
    {
        var actor = command.Actor.Value;
        var tick = command.Tick;
        var envelope = command switch
        {
            IssueMoveCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { EntityId = c.EntityId, X = c.Target.X, Y = c.Target.Y }),
            StopCommanderCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { CommanderId = c.CommanderId }),
            QueueCommanderBuildCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto
                {
                    CommanderId = c.CommanderId,
                    TargetKind = c.TargetKind,
                    X = c.Position.X,
                    Y = c.Position.Y,
                    Direction = c.Direction,
                    SelectedItemRecipe = c.SelectedItemRecipe,
                    InserterLongReach = c.InserterLongReach ? true : null,
                }),
            QueueCommanderDemolishCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { CommanderId = c.CommanderId, TargetEntityId = c.TargetEntityId }),
            PlaceGhostBuildFromCommanderCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto
                {
                    CommanderId = c.CommanderId,
                    TargetKind = c.TargetKind,
                    X = c.Position.X,
                    Y = c.Position.Y,
                    Direction = c.Direction,
                    SelectedItemRecipe = c.SelectedItemRecipe,
                    InserterLongReach = c.InserterLongReach ? true : null,
                }),
            RotateEntityCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { EntityId = c.EntityId, Clockwise = c.Clockwise }),
            StartResearchCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { Technology = c.Technology.Value }),
            CancelResearchCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { Technology = c.Technology.Value }),
            SetTrackAllocationCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto
                {
                    Allocations = c.Allocations
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                }),
            SetFactoryProductionCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { FactoryId = c.FactoryId, OutputKind = c.OutputKind, BastionId = c.BastionId }),
#pragma warning disable CS0618 // M02: obsolete kind rejected, not serialized
            AssignFactoryBastionCommand => throw new NotSupportedException(
                "AssignFactoryBastion is obsolete and removed from the command protocol vocabulary (M02)."),
#pragma warning restore CS0618
            SetBastionTemplateCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { BastionId = c.BastionId, UnitKind = c.UnitKind, Count = c.Count }),
            IssueBastionOrderCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto
                {
                    BastionId = c.BastionId,
                    OrderKind = c.Order.Kind,
                    X = c.Order.Target?.X,
                    Y = c.Order.Target?.Y,
                    Waypoints = c.Order.WaypointList.Select(tile => new TileDto(tile.X, tile.Y)).ToArray(),
                    WaypointIndex = c.Order.WaypointIndex,
                }),
            SetAssemblerRecipeCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { AssemblerId = c.AssemblerId, RecipeId = c.RecipeId }),
            CollectOutputBufferCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { CommanderId = c.CommanderId, TargetEntityId = c.TargetEntityId }),
            WithdrawFromHubOrOutputCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { CommanderId = c.CommanderId, TargetEntityId = c.TargetEntityId }),
            DepositToHubOrInputCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { CommanderId = c.CommanderId, TargetEntityId = c.TargetEntityId }),
            DepositItemTypeToHubOrInputCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { CommanderId = c.CommanderId, TargetEntityId = c.TargetEntityId, Item = c.Item }),
            WithdrawItemTypeFromHubOrOutputCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { CommanderId = c.CommanderId, TargetEntityId = c.TargetEntityId, Item = c.Item }),
            SelectResearchCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto
                {
                    Technology = c.Technology.Value,
                    ConfirmExclusive = c.ConfirmExclusive,
                    PreferredTrackId = c.PreferredTrackId,
                }),
            SetProjectWeightCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto
                {
                    TrackId = c.TrackId,
                    Technology = c.Technology.Value,
                    Weight = c.Weight,
                }),
            SetInserterReachCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { EntityId = c.EntityId, InserterLongReach = c.LongReach }),
            _ => throw new NotSupportedException($"Unknown command type '{command.GetType().Name}'."),
        };

        // R11: stamp every envelope with the protocol version and carry the canonical (R03) sequence
        // over the wire so replay/network peers reconstruct the exact ordering key.
        return envelope with { ProtocolVersion = ProtocolVersion, Sequence = command.Sequence };
    }

    private static ISimulationCommand FromDto(CommandEnvelopeDto dto)
    {
        ValidateProtocol(dto.ProtocolVersion);
        var actor = new PlayerId(dto.Actor);
        var tick = dto.Tick;
        var p = dto.Payload ?? new CommandPayloadDto();
        ISimulationCommand command = dto.Kind switch
        {
            SimulationCommandKind.IssueMove => new IssueMoveCommand(
                actor, tick, Require(p.EntityId, "entityId"), new TilePosition(Require(p.X, "x"), Require(p.Y, "y"))),
            SimulationCommandKind.StopCommander => new StopCommanderCommand(
                actor, tick, Require(p.CommanderId, "commanderId")),
            SimulationCommandKind.QueueCommanderBuild => new QueueCommanderBuildCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetKind, "targetKind"),
                new TilePosition(Require(p.X, "x"), Require(p.Y, "y")),
                p.Direction ?? Direction.East, p.SelectedItemRecipe, p.InserterLongReach ?? false),
            SimulationCommandKind.QueueCommanderDemolish => new QueueCommanderDemolishCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetEntityId, "targetEntityId")),
            SimulationCommandKind.PlaceGhostBuildFromCommander => new PlaceGhostBuildFromCommanderCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetKind, "targetKind"),
                new TilePosition(Require(p.X, "x"), Require(p.Y, "y")),
                p.Direction ?? Direction.East, p.SelectedItemRecipe, p.InserterLongReach ?? false),
            SimulationCommandKind.RotateEntity => new RotateEntityCommand(
                actor, tick, Require(p.EntityId, "entityId"), p.Clockwise ?? true),
            SimulationCommandKind.StartResearch => new StartResearchCommand(
                actor, tick, new TechnologyId(Require(p.Technology, "technology"))),
            SimulationCommandKind.CancelResearch => new CancelResearchCommand(
                actor, tick, new TechnologyId(Require(p.Technology, "technology"))),
            SimulationCommandKind.SetTrackAllocation => new SetTrackAllocationCommand(
                actor, tick, p.Allocations ?? new Dictionary<string, int>()),
            SimulationCommandKind.SetFactoryProduction => new SetFactoryProductionCommand(
                actor, tick, Require(p.FactoryId, "factoryId"), p.OutputKind, p.BastionId),
#pragma warning disable CS0618 // M02: obsolete kind rejected on deserialize
            SimulationCommandKind.AssignFactoryBastion => throw new NotSupportedException(
                "AssignFactoryBastion is obsolete and rejected on deserialize (M02)."),
#pragma warning restore CS0618
            SimulationCommandKind.SetBastionTemplate => new SetBastionTemplateCommand(
                actor, tick, Require(p.BastionId, "bastionId"), Require(p.UnitKind, "unitKind"), Require(p.Count, "count")),
            SimulationCommandKind.IssueBastionOrder => new IssueBastionOrderCommand(
                actor, tick, Require(p.BastionId, "bastionId"),
                new BastionOrder(
                    Require(p.OrderKind, "orderKind"),
                    p.X is int tx && p.Y is int ty ? new TilePosition(tx, ty) : null,
                    p.Waypoints?.Select(tile => new TilePosition(tile.X, tile.Y)).ToArray(),
                    p.WaypointIndex ?? 0)),
            SimulationCommandKind.SetAssemblerRecipe => new SetAssemblerRecipeCommand(
                actor, tick, Require(p.AssemblerId, "assemblerId"), Require(p.RecipeId, "recipeId")),
            SimulationCommandKind.CollectOutputBuffer => new CollectOutputBufferCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetEntityId, "targetEntityId")),
            SimulationCommandKind.WithdrawFromHubOrOutput => new WithdrawFromHubOrOutputCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetEntityId, "targetEntityId")),
            SimulationCommandKind.DepositToHubOrInput => new DepositToHubOrInputCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetEntityId, "targetEntityId")),
            SimulationCommandKind.DepositItemTypeToHubOrInput => new DepositItemTypeToHubOrInputCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetEntityId, "targetEntityId"),
                Require(p.Item, "item")),
            SimulationCommandKind.WithdrawItemTypeFromHubOrOutput => new WithdrawItemTypeFromHubOrOutputCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetEntityId, "targetEntityId"),
                Require(p.Item, "item")),
            SimulationCommandKind.SelectResearch => new SelectResearchCommand(
                actor, tick, new TechnologyId(Require(p.Technology, "technology")),
                p.ConfirmExclusive ?? false, p.PreferredTrackId),
            SimulationCommandKind.SetProjectWeight => new SetProjectWeightCommand(
                actor, tick, Require(p.TrackId, "trackId"),
                new TechnologyId(Require(p.Technology, "technology")),
                Require(p.Weight, "weight")),
            SimulationCommandKind.SetInserterReach => new SetInserterReachCommand(
                actor, tick, Require(p.EntityId, "entityId"), p.InserterLongReach ?? false),
            _ => throw new NotSupportedException($"Unknown command kind '{dto.Kind}'."),
        };

        // R11/R03: reattach the wire-carried sequence (0 = unsequenced legacy). WithScheduling preserves
        // the concrete runtime command type and its tick while re-stamping the ordering key.
        return dto.Sequence != 0 && command is SimulationCommandBase based
            ? based.WithScheduling(based.Tick, dto.Sequence)
            : command;
    }

    /// <summary>
    /// R11: reject envelopes from an unknown protocol version predictably (before touching payload). A
    /// null version is a legacy pre-versioning envelope and is accepted at the current version.
    /// </summary>
    private static void ValidateProtocol(int? protocolVersion)
    {
        if (protocolVersion is null)
        {
            return;
        }

        if (protocolVersion.Value < MinSupportedProtocolVersion || protocolVersion.Value > ProtocolVersion)
        {
            throw new NotSupportedException(
                $"Unsupported command protocol version {protocolVersion.Value}; " +
                $"this build decodes [{MinSupportedProtocolVersion}, {ProtocolVersion}].");
        }
    }

    private static T Require<T>(T? value, string name) where T : struct
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Command payload missing required field '{name}'.");
        }

        return value.Value;
    }

    private static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Command payload missing required field '{name}'.");
        }

        return value;
    }

    private sealed record CommandEnvelopeDto(
        SimulationCommandKind Kind,
        int Actor,
        long Tick,
        CommandPayloadDto? Payload)
    {
        /// <summary>R11: wire protocol version. Null on legacy pre-versioning envelopes.</summary>
        public int? ProtocolVersion { get; init; }

        /// <summary>R03/R11: canonical per-author ordering key carried over the wire.</summary>
        public long Sequence { get; init; }
    }

    private sealed class CommandPayloadDto
    {
        public int? EntityId { get; set; }
        public int? CommanderId { get; set; }
        public int? TargetEntityId { get; set; }
        public int? FactoryId { get; set; }
        public int? BastionId { get; set; }
        public int? AssemblerId { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Count { get; set; }
        public int? WaypointIndex { get; set; }
        public bool? Clockwise { get; set; }
        public bool? ConfirmExclusive { get; set; }
        public string? PreferredTrackId { get; set; }
        public EntityKind? TargetKind { get; set; }
        public EntityKind? OutputKind { get; set; }
        public EntityKind? UnitKind { get; set; }
        public Direction? Direction { get; set; }
        public ItemRecipeId? SelectedItemRecipe { get; set; }
        public ItemRecipeId? RecipeId { get; set; }
        public ItemId? Item { get; set; }
        public BastionOrderKind? OrderKind { get; set; }
        public string? Technology { get; set; }
        public string? TrackId { get; set; }
        public int? Weight { get; set; }
        public bool? InserterLongReach { get; set; }
        public Dictionary<string, int>? Allocations { get; set; }
        public TileDto[]? Waypoints { get; set; }
    }

    private sealed record TileDto(int X, int Y);
}
