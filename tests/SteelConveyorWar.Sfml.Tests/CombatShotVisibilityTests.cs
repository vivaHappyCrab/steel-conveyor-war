using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

// R27/H06: tracers and selection must respect fog-of-war so hidden movement/combat cannot be inferred.
public sealed class CombatShotVisibilityTests
{
    private static (GameSimulation Simulation, PlayerId Blue, WorldEntity BlueCommander, WorldEntity RedCommander) NewGame()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var red = new PlayerId(2);
        var blueCommander = simulation.World.Entities.Single(e => e.Kind == EntityKind.Commander && e.OwnerId == blue);
        var redCommander = simulation.World.Entities.Single(e => e.Kind == EntityKind.Commander && e.OwnerId == red);
        return (simulation, blue, blueCommander, redCommander);
    }

    private static CombatShotEvent ShotBetween(WorldEntity attacker, WorldEntity target)
    {
        return new CombatShotEvent(
            attacker.Id,
            target.Id,
            WorldPosition.FromTileCenter(attacker.Position),
            WorldPosition.FromTileCenter(target.Position),
            ProjectileKind.Ballistic);
    }

    [Fact]
    public void Shot_InHiddenEnemyArea_IsNotVisibleToLocalPlayer()
    {
        var (simulation, blue, _, redCommander) = NewGame();

        Assert.Equal(VisibilityState.Unknown, simulation.GetVisibility(blue, redCommander.Position));

        var shot = ShotBetween(redCommander, redCommander);
        Assert.Equal(CombatShotRevealMode.Hidden, WorldRenderer.ClassifyShotForLocalPlayer(simulation, blue, shot));
        Assert.False(WorldRenderer.IsShotVisibleToLocalPlayer(simulation, blue, shot));
    }

    [Fact]
    public void Shot_ByOwnedAttacker_IntoFog_IsMuzzleOnly_WithoutExactTarget()
    {
        var (simulation, blue, blueCommander, redCommander) = NewGame();

        var shot = ShotBetween(blueCommander, redCommander);
        var reveal = WorldRenderer.ClassifyShotForLocalPlayer(simulation, blue, shot);
        Assert.Equal(CombatShotRevealMode.MuzzleOnly, reveal);

        var (from, to) = CombatShotVisibility.SanitizeEndpoints(shot, reveal);
        Assert.Equal(shot.From, from);
        Assert.NotNull(to);
        Assert.NotEqual(shot.To, to!.Value);
    }

    [Fact]
    public void FairObservation_OwnedShotIntoFog_DoesNotLeakExactTarget()
    {
        var (simulation, blue, blueCommander, redCommander) = NewGame();
        simulation.Presentation.AddCombatShot(ShotBetween(blueCommander, redCommander));

        var view = simulation.CreatePlayerView(blue, PlayerObservationMode.Fair);
        var ev = Assert.Single(view.GetEventsThisTick());
        Assert.Equal(CombatShotRevealMode.MuzzleOnly, ev.RevealMode);
        Assert.Equal(WorldPosition.FromTileCenter(blueCommander.Position), ev.From);
        Assert.NotNull(ev.To);
        Assert.NotEqual(WorldPosition.FromTileCenter(redCommander.Position), ev.To!.Value);
    }

    [Fact]
    public void Shot_BecomesFull_WhenBothEndpointsVisible()
    {
        var (simulation, blue, blueCommander, redCommander) = NewGame();

        Assert.True(simulation.TryTeleportEntityForTests(
            redCommander.Id,
            new TilePosition(blueCommander.Position.X + 1, blueCommander.Position.Y)));
        simulation.AdvanceTick();

        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(blue, redCommander.Position));
        var revealedShot = ShotBetween(blueCommander, redCommander);
        Assert.Equal(CombatShotRevealMode.Full, WorldRenderer.ClassifyShotForLocalPlayer(simulation, blue, revealedShot));
    }

    [Fact]
    public void HiddenEnemySelection_WouldBeDropped_ByVisibilityGate()
    {
        var (simulation, blue, blueCommander, redCommander) = NewGame();

        Assert.False(WorldRenderer.IsVisibleToLocalPlayer(simulation, blue, redCommander));

        Assert.True(simulation.TryTeleportEntityForTests(
            redCommander.Id,
            new TilePosition(blueCommander.Position.X + 1, blueCommander.Position.Y)));
        simulation.AdvanceTick();

        Assert.True(WorldRenderer.IsVisibleToLocalPlayer(simulation, blue, redCommander));
    }
}
