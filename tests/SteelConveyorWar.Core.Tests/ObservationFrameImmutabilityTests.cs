using System.Collections.Immutable;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// M10: observation frames are hard-immutable and atomic (entities + fog/terrain share one tick).
/// </summary>
public sealed class ObservationFrameImmutabilityTests
{
    private static readonly PlayerId Blue = new(1);
    private static readonly PlayerId Red = new(2);

    [Fact]
    public void CaptureFrame_NestedCollections_AreNotCastMutable()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var frame = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair).CaptureFrame();

        Assert.Throws<NotSupportedException>(() =>
            ((IList<VisibleEntitySnapshot>)frame.VisibleEntities).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)frame.OwnEconomy.Inventory).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((ISet<TechnologyId>)frame.OwnResearch.CompletedTechnologies).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<TechnologyId, int>)frame.OwnResearch.ProgressWorkUnits).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<TrackObservation>)frame.OwnResearch.Tracks).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((ISet<string>)frame.OwnResearch.AppliedCapabilities).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<TechSignatureObservation>)frame.TechSignatures).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SimulationCommandKind>)frame.AvailableCommandKinds).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ObservedCombatEvent>)frame.EventsThisTick).Clear());

        var ownCommander = Assert.Single(frame.VisibleEntities, entity => entity.IsOwn && entity.Kind == EntityKind.Commander);
        Assert.NotNull(ownCommander.Own);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)ownCommander.Own!.Inventory).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<EntityKind, int>)ownCommander.Own.BastionTemplate).Clear());
    }

    [Fact]
    public void AvailableCommandKinds_MutationDoesNotAffectGlobalVocabulary()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var kinds = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair).GetAvailableCommandKinds();
        var before = SimulationCommandVocabulary.AdvertisedKinds.ToArray();

        Assert.IsAssignableFrom<ImmutableArray<SimulationCommandKind>>(kinds);
        Assert.Throws<NotSupportedException>(() => ((IList<SimulationCommandKind>)kinds).Clear());

        var again = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair).GetAvailableCommandKinds();
        Assert.Equal(before, again);
        Assert.Equal(before, SimulationCommandVocabulary.AdvertisedKinds);
    }

    [Fact]
    public void CaptureFrame_FogBoard_StableAfterAdvanceTick()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == Blue);
        var red = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == Red);

        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);
        var frame = fair.CaptureFrame();
        var visibleTile = blue.Position;
        var unknownTile = red.Position;

        Assert.Equal(VisibilityState.Visible, frame.FogBoard.GetVisibility(visibleTile));
        Assert.True(frame.FogBoard.TryGetTerrain(visibleTile, out var knownTerrain));
        Assert.Equal(simulation.World.GetTerrain(visibleTile), knownTerrain);
        Assert.Equal(VisibilityState.Unknown, frame.FogBoard.GetVisibility(unknownTile));
        Assert.False(frame.FogBoard.TryGetTerrain(unknownTile, out _));

        // Mutate live FoW (teleport enemy into view) and advance — retained frame must not change.
        Assert.True(simulation.TryTeleportEntityForTests(red.Id, new TilePosition(blue.Position.X + 1, blue.Position.Y)));
        simulation.AdvanceTick();

        Assert.Equal(VisibilityState.Visible, frame.FogBoard.GetVisibility(visibleTile));
        Assert.Equal(VisibilityState.Unknown, frame.FogBoard.GetVisibility(unknownTile));
        Assert.False(frame.FogBoard.TryGetTerrain(unknownTile, out _));
        Assert.Equal(frame.ObservationTick, frame.FogBoard.ObservationTick);
        Assert.NotEqual(simulation.Tick, frame.ObservationTick);
    }

    [Fact]
    public void CaptureFrame_EntitiesEconomyAndFogShareObservationTick()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        simulation.AdvanceTick();
        var frame = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair).CaptureFrame();

        Assert.Equal(simulation.Tick, frame.ObservationTick);
        Assert.Equal(frame.ObservationTick, frame.OwnEconomy.ObservationTick);
        Assert.Equal(frame.ObservationTick, frame.OwnResearch.ObservationTick);
        Assert.Equal(frame.ObservationTick, frame.FogBoard.ObservationTick);
        Assert.All(frame.VisibleEntities, entity => Assert.Equal(frame.ObservationTick, entity.ObservationTick));
    }

    [Fact]
    public void FairEvents_DoNotLeakHiddenEndpoints_H06()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == Blue);
        var red = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == Red);

        // Own attacker fires toward a fog target: muzzle-only, no exact hidden To.
        var shot = new CombatShotEvent(
            blue.Id,
            red.Id,
            blue.WorldPosition,
            red.WorldPosition,
            ProjectileKind.GroundToGround);
        simulation.Presentation.AddCombatShot(shot);

        var fair = simulation.CreatePlayerView(Blue, PlayerObservationMode.Fair);
        var frame = fair.CaptureFrame();
        var ev = Assert.Single(frame.EventsThisTick);

        Assert.Equal(CombatShotRevealMode.MuzzleOnly, ev.RevealMode);
        Assert.NotNull(ev.From);
        Assert.NotNull(ev.To);
        Assert.NotEqual(red.WorldPosition, ev.To);
        Assert.Equal(blue.WorldPosition, ev.From);
    }
}
