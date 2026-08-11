using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R11: the command wire format is protocol-grade — versioned envelope, canonical sequence carried over
/// the wire, round-trip coverage for every <see cref="SimulationCommandKind"/>, predictable rejection of
/// unknown protocol versions, and logged (not silently dropped) command rejections.
/// </summary>
public sealed class CommandProtocolTests
{
    // One representative instance per command kind, each stamped with a non-zero sequence so we also
    // prove the R03 ordering key survives serialization.
    public static IEnumerable<object[]> AllCommandKinds()
    {
        var actor = new PlayerId(1);
        const long tick = 42;

        SimulationCommandBase[] commands =
        {
            new IssueMoveCommand(actor, tick, 3, new TilePosition(8, 9)) { Sequence = 11 },
            new StopCommanderCommand(actor, tick, 3) { Sequence = 12 },
            new QueueCommanderBuildCommand(actor, tick, 3, EntityKind.Assembler, new TilePosition(2, 2),
                Direction.South, ItemRecipeId.IronGear) { Sequence = 13 },
            new QueueCommanderDemolishCommand(actor, tick, 3, 7) { Sequence = 14 },
            new PlaceGhostBuildFromCommanderCommand(actor, tick, 3, EntityKind.Wall, new TilePosition(4, 5),
                Direction.West, ItemRecipeId.Composite) { Sequence = 15 },
            new RotateEntityCommand(actor, tick, 3, Clockwise: false) { Sequence = 16 },
            new StartResearchCommand(actor, tick, new TechnologyId("mining-1")) { Sequence = 17 },
            new CancelResearchCommand(actor, tick, new TechnologyId("mining-1")) { Sequence = 18 },
            new SetTrackAllocationCommand(actor, tick, new Dictionary<string, int> { ["cycle"] = 2, ["burst"] = 1 })
                { Sequence = 19 },
            new SetFactoryProductionCommand(actor, tick, 5, EntityKind.BasicTank, 9) { Sequence = 20 },
            new AssignFactoryBastionCommand(actor, tick, 5, 9) { Sequence = 21 },
            new SetBastionTemplateCommand(actor, tick, 9, EntityKind.BasicTank, 3) { Sequence = 22 },
            new IssueBastionOrderCommand(actor, tick, 9, new BastionOrder(
                BastionOrderKind.Patrol,
                Target: new TilePosition(1, 2),
                Waypoints: new[] { new TilePosition(1, 2), new TilePosition(3, 4) },
                WaypointIndex: 1)) { Sequence = 23 },
            new SetAssemblerRecipeCommand(actor, tick, 5, ItemRecipeId.SciencePackT1) { Sequence = 24 },
            new CollectOutputBufferCommand(actor, tick, 3, 5) { Sequence = 25 },
            new WithdrawFromHubOrOutputCommand(actor, tick, 3, 5) { Sequence = 26 },
            new DepositToHubOrInputCommand(actor, tick, 3, 5) { Sequence = 27 },
            new DepositItemTypeToHubOrInputCommand(actor, tick, 3, 5, ItemId.Coal) { Sequence = 28 },
            new WithdrawItemTypeFromHubOrOutputCommand(actor, tick, 3, 5, ItemId.Steel) { Sequence = 29 },
            new SelectResearchCommand(actor, tick, new TechnologyId("logistics-1"),
                ConfirmExclusive: true, PreferredTrackId: "cycle") { Sequence = 30 },
        };

        return commands.Select(command => new object[] { command });
    }

    [Theory]
    [MemberData(nameof(AllCommandKinds))]
    public void EveryKind_RoundTripsThroughSerializer_PreservingHeader(SimulationCommandBase command)
    {
        var roundTrip = SimulationCommandSerializer.Deserialize(SimulationCommandSerializer.Serialize(command));

        Assert.Equal(command.Kind, roundTrip.Kind);
        Assert.Equal(command.Actor, roundTrip.Actor);
        Assert.Equal(command.Tick, roundTrip.Tick);
        // R11: canonical (R03) sequence must travel over the wire, not reset to 0.
        Assert.Equal(command.Sequence, roundTrip.Sequence);
        Assert.IsType(command.GetType(), roundTrip);
    }

    [Fact]
    public void AllTwentyKinds_AreExercised()
    {
        // Guards against a new SimulationCommandKind being added without a round-trip case here.
        var covered = AllCommandKinds().Select(row => ((SimulationCommandBase)row[0]).Kind).Distinct().Count();
        Assert.Equal(Enum.GetValues<SimulationCommandKind>().Length, covered);
    }

    [Fact]
    public void SerializedEnvelope_CarriesProtocolVersion()
    {
        var json = SimulationCommandSerializer.Serialize(
            new IssueMoveCommand(new PlayerId(1), 5, 3, new TilePosition(1, 1)));

        Assert.Contains("\"protocolVersion\":1", json);
    }

    [Fact]
    public void UnknownFutureProtocolVersion_IsRejectedPredictably()
    {
        // A future-versioned envelope must fail closed rather than silently mis-decode.
        var future = SimulationCommandSerializer.ProtocolVersion + 1;
        var json =
            $"{{\"kind\":\"issueMove\",\"actor\":1,\"tick\":5,\"protocolVersion\":{future}," +
            "\"payload\":{\"entityId\":3,\"x\":1,\"y\":1}}";

        Assert.Throws<NotSupportedException>(() => SimulationCommandSerializer.Deserialize(json));
        Assert.False(SimulationCommandSerializer.TryDeserialize(json, out var command));
        Assert.Null(command);
    }

    [Fact]
    public void LegacyEnvelope_WithoutProtocolVersion_IsAccepted()
    {
        // Pre-versioning envelopes (no protocolVersion field) remain decodable at the current version.
        const string json = "{\"kind\":\"issueMove\",\"actor\":1,\"tick\":5,\"payload\":{\"entityId\":3,\"x\":1,\"y\":1}}";

        Assert.True(SimulationCommandSerializer.TryDeserialize(json, out var command));
        var move = Assert.IsType<IssueMoveCommand>(command);
        Assert.Equal(3, move.EntityId);
    }

    [Fact]
    public void RejectedCommand_IsLoggedWithReason_NotSilentlyDropped()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 5);
        var actor = new PlayerId(1);
        // Move a non-existent entity id: the handler rejects (returns false) rather than throwing.
        simulation.EnqueueForNextTick(tick => new IssueMoveCommand(actor, tick, EntityId: 999_999, new TilePosition(3, 3)));

        simulation.AdvanceTick();

        var rejection = Assert.Single(simulation.LastTickCommandRejections);
        Assert.Equal(SimulationCommandKind.IssueMove, rejection.Kind);
        Assert.Equal(actor, rejection.Actor);
        Assert.False(string.IsNullOrWhiteSpace(rejection.Reason));
    }

    [Fact]
    public void AcceptedCommand_ProducesNoRejection()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 5);
        var actor = new PlayerId(1);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == actor && entity.Kind == EntityKind.Commander);
        simulation.EnqueueForNextTick(tick => new IssueMoveCommand(actor, tick, commander.Id, new TilePosition(6, 6)));

        simulation.AdvanceTick();

        Assert.Empty(simulation.LastTickCommandRejections);
    }
}
