using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R11 / M02: the command wire format is protocol-grade — versioned envelope, canonical sequence,
/// full payload equality round-trips for every advertised kind, obsolete kinds filtered/rejected,
/// and batch serialize/deserialize coverage.
/// </summary>
public sealed class CommandProtocolTests
{
    // One representative instance per advertised command kind, each stamped with a non-zero sequence.
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
            new SetProjectWeightCommand(actor, tick, ResearchTrackIds.Cycle,
                new TechnologyId("technology.t1.automated-base"), Weight: 150) { Sequence = 31 },
        };

        return commands.Select(command => new object[] { command });
    }

    [Theory]
    [MemberData(nameof(AllCommandKinds))]
    public void EveryKind_RoundTripsThroughSerializer_PreservingFullPayload(SimulationCommandBase command)
    {
        var roundTrip = SimulationCommandSerializer.Deserialize(SimulationCommandSerializer.Serialize(command));

        Assert.Equal(command.Kind, roundTrip.Kind);
        Assert.Equal(command.Actor, roundTrip.Actor);
        Assert.Equal(command.Tick, roundTrip.Tick);
        Assert.Equal(command.Sequence, roundTrip.Sequence);
        Assert.IsType(command.GetType(), roundTrip);
        // M02: full payload equality, not just header/runtime type.
        Assert.True(CommandPayloadEquality.AreEqual(command, roundTrip), CommandPayloadEquality.Snapshot(command));
    }

    [Fact]
    public void AllAdvertisedKinds_AreExercised()
    {
        // Guards against a new advertised kind being added without a round-trip case here.
        var covered = AllCommandKinds().Select(row => ((SimulationCommandBase)row[0]).Kind).Distinct().ToHashSet();
        Assert.Equal(SimulationCommandVocabulary.AdvertisedKinds.Count, covered.Count);
        foreach (var kind in SimulationCommandVocabulary.AdvertisedKinds)
        {
            Assert.Contains(kind, covered);
        }
    }

    [Fact]
    public void AvailableKinds_NeverContainObsoleteAlwaysFalseCommands()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 5);
        var view = simulation.CreatePlayerView(new PlayerId(1), PlayerObservationMode.Fair);

#pragma warning disable CS0618
        Assert.DoesNotContain(SimulationCommandKind.AssignFactoryBastion, view.GetAvailableCommandKinds());
#pragma warning restore CS0618
        Assert.Equal(SimulationCommandVocabulary.AdvertisedKinds, view.GetAvailableCommandKinds());
    }

    [Fact]
    public void ObsoleteAssignFactoryBastion_IsRejectedOnDeserialize()
    {
        const string json =
            "{\"kind\":\"assignFactoryBastion\",\"actor\":1,\"tick\":5,\"protocolVersion\":1," +
            "\"payload\":{\"factoryId\":5,\"bastionId\":9}}";

        Assert.Throws<NotSupportedException>(() => SimulationCommandSerializer.Deserialize(json));
        Assert.False(SimulationCommandSerializer.TryDeserialize(json, out var command));
        Assert.Null(command);
    }

    [Fact]
    public void SetProjectWeight_Apply_UpdatesParallelTrackWeight_AndRejectsWrongActor()
    {
        var simulation = GameSimulation.CreateNewGame(
            new GameCreationOptions(5, ResearchProfileIds.MvpC, MvpResearchCatalog.CreateEmbedded()));
        var actor = new PlayerId(1);
        var tech = new TechnologyId("technology.t1.automated-base");
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(actor, tech));

        Assert.True(simulation.ApplyCommand(
            new SetProjectWeightCommand(actor, simulation.Tick, ResearchTrackIds.Cycle, tech, Weight: 250)));

        var cycle = simulation.GetResearchSnapshot(actor).Tracks
            .Single(track => track.Id == ResearchTrackIds.Cycle);
        Assert.Equal(250, cycle.ProjectWeights[tech]);

        // Other player's SetProjectWeight must not change this player's weights.
        Assert.True(simulation.ApplyCommand(
            new SetProjectWeightCommand(new PlayerId(2), simulation.Tick, ResearchTrackIds.Cycle, tech, Weight: 1)));
        Assert.Equal(250, simulation.GetResearchSnapshot(actor).Tracks
            .Single(track => track.Id == ResearchTrackIds.Cycle).ProjectWeights[tech]);
    }

    [Fact]
    public void SerializeMany_DeserializeMany_RoundTripsPayloadEquality()
    {
        var batch = AllCommandKinds()
            .Select(row => (SimulationCommandBase)row[0])
            .Take(5)
            .Cast<ISimulationCommand>()
            .ToArray();

        var roundTrip = SimulationCommandSerializer.DeserializeMany(
            SimulationCommandSerializer.SerializeMany(batch));

        Assert.Equal(batch.Length, roundTrip.Count);
        for (var i = 0; i < batch.Length; i++)
        {
            Assert.True(CommandPayloadEquality.AreEqual(batch[i], roundTrip[i]));
        }
    }

    [Fact]
    public void TryDeserializeMany_ReturnsFalse_OnMalformedOrTruncatedJson()
    {
        Assert.False(SimulationCommandSerializer.TryDeserializeMany(null, out var empty));
        Assert.Empty(empty);

        Assert.False(SimulationCommandSerializer.TryDeserializeMany("", out _));
        Assert.False(SimulationCommandSerializer.TryDeserializeMany("[", out _));
        Assert.False(SimulationCommandSerializer.TryDeserializeMany(
            "[{\"kind\":\"issueMove\",\"actor\":1,\"tick\":5,\"payload\":{}}]", out _));
    }

    [Fact]
    public void TryDeserializeMany_Succeeds_OnValidBatch()
    {
        var json = SimulationCommandSerializer.SerializeMany(new ISimulationCommand[]
        {
            new IssueMoveCommand(new PlayerId(1), 5, 3, new TilePosition(1, 1)) { Sequence = 1 },
            new StopCommanderCommand(new PlayerId(1), 5, 3) { Sequence = 2 },
        });

        Assert.True(SimulationCommandSerializer.TryDeserializeMany(json, out var commands));
        Assert.Equal(2, commands.Count);
    }

    [Fact]
    public void SerializedEnvelope_CarriesProtocolVersion()
    {
        var json = SimulationCommandSerializer.Serialize(
            new IssueMoveCommand(new PlayerId(1), 5, 3, new TilePosition(1, 1)));

        Assert.Contains($"\"protocolVersion\":{SimulationCommandSerializer.ProtocolVersion}", json);
        Assert.Equal(2, SimulationCommandSerializer.ProtocolVersion);
    }

    [Fact]
    public void ObsoleteAssignFactoryBastion_CannotBeEnqueued()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 5);
#pragma warning disable CS0618
        var obsolete = new AssignFactoryBastionCommand(new PlayerId(1), simulation.Tick + 1, FactoryId: 1, BastionId: 2);
#pragma warning restore CS0618

        Assert.Throws<ArgumentException>(() => simulation.EnqueueCommand(obsolete));
        Assert.Empty(simulation.PendingCommands);
        // Pending hash must remain callable (no Serialize throw from obsolete kind in buffer).
        _ = simulation.ComputePendingCommandsHash();
    }

    [Fact]
    public void UnknownFutureProtocolVersion_IsRejectedPredictably()
    {
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
