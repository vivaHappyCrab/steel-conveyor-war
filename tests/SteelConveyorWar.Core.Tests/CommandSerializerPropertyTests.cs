using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R22 / M02: property-style round-trips over randomized payloads for every advertised
/// <see cref="SimulationCommandKind"/>. Compares full wire snapshots, not just headers.
/// </summary>
public sealed class CommandSerializerPropertyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(21)]
    [InlineData(34)]
    public void RandomizedCommands_RoundTrip_PreserveFullPayload(int seed)
    {
        var rng = new Random(seed);
        foreach (var kind in SimulationCommandVocabulary.AdvertisedKinds)
        {
            var command = CreateRandomCommand(kind, rng);
            var roundTrip = SimulationCommandSerializer.Deserialize(SimulationCommandSerializer.Serialize(command));
            Assert.Equal(command.Kind, roundTrip.Kind);
            Assert.Equal(command.Actor, roundTrip.Actor);
            Assert.Equal(command.Tick, roundTrip.Tick);
            Assert.Equal(command.Sequence, roundTrip.Sequence);
            Assert.IsType(command.GetType(), roundTrip);
            Assert.True(CommandPayloadEquality.AreEqual(command, roundTrip));
        }
    }

    private static SimulationCommandBase CreateRandomCommand(SimulationCommandKind kind, Random rng)
    {
        var actor = new PlayerId(rng.Next(1, 8));
        var tick = rng.NextInt64(0, 10_000);
        var sequence = rng.NextInt64(0, 10_000);
        var entityId = rng.Next(1, 500);
        var tile = new TilePosition(rng.Next(0, 64), rng.Next(0, 64));
        var tech = new TechnologyId(rng.Next(0, 2) == 0 ? "mining-1" : "logistics-1");

        SimulationCommandBase command = kind switch
        {
            SimulationCommandKind.IssueMove => new IssueMoveCommand(actor, tick, entityId, tile),
            SimulationCommandKind.StopCommander => new StopCommanderCommand(actor, tick, entityId),
            SimulationCommandKind.QueueCommanderBuild => new QueueCommanderBuildCommand(
                actor, tick, entityId, EntityKind.Assembler, tile, Direction.East, ItemRecipeId.IronGear),
            SimulationCommandKind.QueueCommanderDemolish => new QueueCommanderDemolishCommand(actor, tick, entityId, rng.Next(1, 500)),
            SimulationCommandKind.PlaceGhostBuildFromCommander => new PlaceGhostBuildFromCommanderCommand(
                actor, tick, entityId, EntityKind.Wall, tile, Direction.North),
            SimulationCommandKind.RotateEntity => new RotateEntityCommand(actor, tick, entityId, rng.Next(0, 2) == 0),
            SimulationCommandKind.StartResearch => new StartResearchCommand(actor, tick, tech),
            SimulationCommandKind.CancelResearch => new CancelResearchCommand(actor, tick, tech),
            SimulationCommandKind.SetTrackAllocation => new SetTrackAllocationCommand(
                actor, tick, new Dictionary<string, int> { ["cycle"] = rng.Next(0, 5), ["burst"] = rng.Next(0, 5) }),
            SimulationCommandKind.SetFactoryProduction => new SetFactoryProductionCommand(
                actor, tick, entityId, EntityKind.BasicTank, rng.Next(1, 20)),
            SimulationCommandKind.SetBastionTemplate => new SetBastionTemplateCommand(
                actor, tick, entityId, EntityKind.LightBot, rng.Next(1, 10)),
            SimulationCommandKind.IssueBastionOrder => new IssueBastionOrderCommand(
                actor, tick, entityId,
                new BastionOrder(BastionOrderKind.AttackArea, tile, Array.Empty<TilePosition>(), 0)),
            SimulationCommandKind.SetAssemblerRecipe => new SetAssemblerRecipeCommand(actor, tick, entityId, ItemRecipeId.Composite),
            SimulationCommandKind.CollectOutputBuffer => new CollectOutputBufferCommand(actor, tick, entityId, rng.Next(1, 500)),
            SimulationCommandKind.WithdrawFromHubOrOutput => new WithdrawFromHubOrOutputCommand(actor, tick, entityId, rng.Next(1, 500)),
            SimulationCommandKind.DepositToHubOrInput => new DepositToHubOrInputCommand(actor, tick, entityId, rng.Next(1, 500)),
            SimulationCommandKind.DepositItemTypeToHubOrInput => new DepositItemTypeToHubOrInputCommand(
                actor, tick, entityId, rng.Next(1, 500), ItemId.IronPlate),
            SimulationCommandKind.WithdrawItemTypeFromHubOrOutput => new WithdrawItemTypeFromHubOrOutputCommand(
                actor, tick, entityId, rng.Next(1, 500), ItemId.CopperPlate),
            SimulationCommandKind.SelectResearch => new SelectResearchCommand(
                actor, tick, tech, ConfirmExclusive: rng.Next(0, 2) == 0, PreferredTrackId: "cycle"),
            SimulationCommandKind.SetProjectWeight => new SetProjectWeightCommand(
                actor, tick, ResearchTrackIds.Cycle, tech, rng.Next(0, 500)),
            _ => throw new InvalidOperationException($"Unhandled advertised kind {kind}"),
        };

        return command with { Sequence = sequence };
    }
}
