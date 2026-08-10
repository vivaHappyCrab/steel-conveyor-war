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
        return command switch
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
            AssignFactoryBastionCommand c => new CommandEnvelopeDto(
                c.Kind, actor, tick,
                new CommandPayloadDto { FactoryId = c.FactoryId, BastionId = c.BastionId }),
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
            _ => throw new NotSupportedException($"Unknown command type '{command.GetType().Name}'."),
        };
    }

    private static ISimulationCommand FromDto(CommandEnvelopeDto dto)
    {
        var actor = new PlayerId(dto.Actor);
        var tick = dto.Tick;
        var p = dto.Payload ?? new CommandPayloadDto();
        return dto.Kind switch
        {
            SimulationCommandKind.IssueMove => new IssueMoveCommand(
                actor, tick, Require(p.EntityId, "entityId"), new TilePosition(Require(p.X, "x"), Require(p.Y, "y"))),
            SimulationCommandKind.StopCommander => new StopCommanderCommand(
                actor, tick, Require(p.CommanderId, "commanderId")),
            SimulationCommandKind.QueueCommanderBuild => new QueueCommanderBuildCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetKind, "targetKind"),
                new TilePosition(Require(p.X, "x"), Require(p.Y, "y")),
                p.Direction ?? Direction.East, p.SelectedItemRecipe),
            SimulationCommandKind.QueueCommanderDemolish => new QueueCommanderDemolishCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetEntityId, "targetEntityId")),
            SimulationCommandKind.PlaceGhostBuildFromCommander => new PlaceGhostBuildFromCommanderCommand(
                actor, tick, Require(p.CommanderId, "commanderId"), Require(p.TargetKind, "targetKind"),
                new TilePosition(Require(p.X, "x"), Require(p.Y, "y")),
                p.Direction ?? Direction.East, p.SelectedItemRecipe),
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
            SimulationCommandKind.AssignFactoryBastion => new AssignFactoryBastionCommand(
                actor, tick, Require(p.FactoryId, "factoryId"), Require(p.BastionId, "bastionId")),
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
            _ => throw new NotSupportedException($"Unknown command kind '{dto.Kind}'."),
        };
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
        CommandPayloadDto? Payload);

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
        public EntityKind? TargetKind { get; set; }
        public EntityKind? OutputKind { get; set; }
        public EntityKind? UnitKind { get; set; }
        public Direction? Direction { get; set; }
        public ItemRecipeId? SelectedItemRecipe { get; set; }
        public ItemRecipeId? RecipeId { get; set; }
        public ItemId? Item { get; set; }
        public BastionOrderKind? OrderKind { get; set; }
        public string? Technology { get; set; }
        public Dictionary<string, int>? Allocations { get; set; }
        public TileDto[]? Waypoints { get; set; }
    }

    private sealed record TileDto(int X, int Y);
}
