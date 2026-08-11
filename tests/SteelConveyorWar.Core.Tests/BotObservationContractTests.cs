using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R19: the fair observation contract (<see cref="IPlayerView"/>) must be self-sufficient for a bot —
/// own economy/research, tech signatures, the command vocabulary, and a visibility-filtered event
/// stream — without the bot ever reaching into <see cref="GameSimulation.World"/> or GetPlayer.
/// </summary>
public sealed class BotObservationContractTests
{
    private static readonly PlayerId Blue = new(1);
    private static readonly PlayerId Red = new(2);

    [Fact]
    public void OwnEconomy_IsPopulatedForObserver_AndTickStamped()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);

        var economy = fair.GetOwnEconomy();

        Assert.Equal(Blue, economy.OwnerId);
        Assert.Equal(simulation.Tick, economy.ObservationTick);
        Assert.Equal(simulation.Tick, fair.ObservationTick);
        // Starting player-global stock is seeded in CreateNewGame.
        Assert.True(economy.Inventory.GetValueOrDefault(ItemId.IronPlate) > 0);
        Assert.False(economy.IsDefeated);
    }

    [Fact]
    public void OwnEconomy_IsADefensiveCopy_NotTrackingLaterTicks()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);

        var economy = fair.GetOwnEconomy();
        var frozenIron = economy.Inventory.GetValueOrDefault(ItemId.IronPlate);

        simulation.AdvanceTick();

        // The retained snapshot dictionary must not observe later simulation mutations.
        Assert.Equal(frozenIron, economy.Inventory.GetValueOrDefault(ItemId.IronPlate));
    }

    [Fact]
    public void OwnResearch_ExposesTierAndTracks_AsImmutableSnapshot()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);

        var research = fair.GetOwnResearch();

        Assert.Equal(simulation.Tick, research.ObservationTick);
        Assert.False(string.IsNullOrEmpty(research.CurrentTierId));
        Assert.NotNull(research.CompletedTechnologies);
        Assert.NotNull(research.Tracks);
        Assert.NotNull(research.AppliedCapabilities);
        Assert.NotNull(research.UnlockedEntityKinds);
    }

    [Fact]
    public void AvailableCommandKinds_CoverTheEntireVocabulary()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);

        var kinds = fair.GetAvailableCommandKinds();

        foreach (var kind in Enum.GetValues<SimulationCommandKind>())
        {
            Assert.Contains(kind, kinds);
        }
    }

    [Fact]
    public void EventsThisTick_AreEmpty_OnAFreshGameWithNoCombat()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);

        Assert.Empty(fair.GetEventsThisTick());
    }

    [Fact]
    public void FairEventStream_IsNeverBroaderThanCheat()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);
        var cheat = simulation.CreatePlayerView(Blue, PlayerObservationMode.Cheat);

        for (var step = 0; step < 20; step++)
        {
            simulation.AdvanceTick();
            // A fair observer can never see more combat events than the cheat (unfiltered) view.
            Assert.True(fair.GetEventsThisTick().Count <= cheat.GetEventsThisTick().Count);
        }
    }

    [Fact]
    public void TechSignatures_ExposeOnlyCoarseZones_NotExactEnemyPositions()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);

        // The type carries only zone coordinates + intensity — there is no per-entity position field to leak.
        var signatures = fair.GetTechSignatures();
        Assert.NotNull(signatures);
        Assert.All(signatures, signature => Assert.True(signature.Intensity > 0));
    }

    [Fact]
    public void CaptureSnapshot_IsSelfContained_ForAFairBot()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);

        var snapshot = fair.CaptureSnapshot();

        Assert.Equal(simulation.Tick, snapshot.ObservationTick);
        Assert.NotNull(snapshot.OwnEconomy);
        Assert.Equal(snapshot.ObservationTick, snapshot.OwnEconomy.ObservationTick);
        Assert.NotNull(snapshot.OwnResearch);
        Assert.Equal(snapshot.ObservationTick, snapshot.OwnResearch.ObservationTick);
        Assert.NotNull(snapshot.TechSignatures);
        Assert.NotEmpty(snapshot.AvailableCommandKinds);
        Assert.NotNull(snapshot.EventsThisTick);
    }

    [Fact]
    public void FairContract_ExposesEverythingTheBotStubNeeds_WithoutWorldAccess()
    {
        // Mirrors what HeadlessHostRunner.TryApplyStubAiStep reads: it must find its own commander and its
        // move/build/demolish order state entirely through the fair view, never through World/GetPlayer.
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);

        var commander = fair.GetVisibleEntities()
            .Single(entity => entity.IsOwn && entity.Kind == EntityKind.Commander);

        Assert.True(commander.Id > 0);
        Assert.NotNull(commander.Own);
        // These are exactly the idle-check fields the stub consults.
        Assert.Null(commander.Own!.MoveTarget);
        Assert.Null(commander.Own.QueuedBuildOrder);
        Assert.Null(commander.Own.QueuedDemolishOrder);

        // And the world bounds needed for the target check come from the contract, not World.
        Assert.True(fair.WorldSize.Width > 0);
        Assert.True(fair.WorldSize.Height > 0);
    }

    [Fact]
    public void OwnEconomy_NeverLeaksAnotherPlayersAggregate()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blueView = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);
        var redView = simulation.CreatePlayerView(Red, PlayerObservationMode.Fair);

        // Each view reports only its own owner id — there is no channel to read the opponent's economy.
        Assert.Equal(Blue, blueView.GetOwnEconomy().OwnerId);
        Assert.Equal(Red, redView.GetOwnEconomy().OwnerId);
    }
}
