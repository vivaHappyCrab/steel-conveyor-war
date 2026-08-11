using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

// R27: tracers and selection must respect fog-of-war so hidden movement/combat cannot be inferred.
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

        // Precondition: at spawn the enemy commander sits in unexplored fog for blue.
        Assert.Equal(VisibilityState.Unknown, simulation.GetVisibility(blue, redCommander.Position));

        var shot = ShotBetween(redCommander, redCommander);
        Assert.False(WorldRenderer.IsShotVisibleToLocalPlayer(simulation, blue, shot));
    }

    [Fact]
    public void Shot_ByOwnedAttacker_IsAlwaysVisible()
    {
        var (simulation, blue, blueCommander, redCommander) = NewGame();

        var shot = ShotBetween(blueCommander, redCommander);
        Assert.True(WorldRenderer.IsShotVisibleToLocalPlayer(simulation, blue, shot));
    }

    [Fact]
    public void Shot_BecomesVisible_WhenEndpointEntersVision()
    {
        var (simulation, blue, blueCommander, redCommander) = NewGame();

        var hiddenShot = ShotBetween(redCommander, redCommander);
        Assert.False(WorldRenderer.IsShotVisibleToLocalPlayer(simulation, blue, hiddenShot));

        Assert.True(simulation.TryTeleportEntityForTests(
            redCommander.Id,
            new TilePosition(blueCommander.Position.X + 1, blueCommander.Position.Y)));
        simulation.AdvanceTick();

        Assert.Equal(VisibilityState.Visible, simulation.GetVisibility(blue, redCommander.Position));
        var revealedShot = ShotBetween(blueCommander, redCommander);
        Assert.True(WorldRenderer.IsShotVisibleToLocalPlayer(simulation, blue, revealedShot));
    }

    [Fact]
    public void HiddenEnemySelection_WouldBeDropped_ByVisibilityGate()
    {
        // The session drops any selection whose entity fails this gate each tick; here we assert the
        // gate itself hides an enemy in fog and re-exposes it only once its tile becomes Visible.
        var (simulation, blue, blueCommander, redCommander) = NewGame();

        Assert.False(WorldRenderer.IsVisibleToLocalPlayer(simulation, blue, redCommander));

        Assert.True(simulation.TryTeleportEntityForTests(
            redCommander.Id,
            new TilePosition(blueCommander.Position.X + 1, blueCommander.Position.Y)));
        simulation.AdvanceTick();

        Assert.True(WorldRenderer.IsVisibleToLocalPlayer(simulation, blue, redCommander));
    }
}
