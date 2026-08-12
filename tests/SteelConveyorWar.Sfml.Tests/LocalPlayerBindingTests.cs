using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

public class LocalPlayerBindingTests
{
    [Fact]
    public void Resolve_DefaultsToPlayerOne()
    {
        Assert.Equal(new PlayerId(1), LocalPlayerBinding.Resolve([]));
        Assert.Equal(new PlayerId(1), LocalPlayerBinding.Resolve(["--smoke-test"]));
        Assert.Equal(SfmlDisplayOptions.DefaultLocalPlayerId, SfmlDisplayOptions.Default.LocalPlayerId);
    }

    [Fact]
    public void Resolve_ReadsLocalPlayerFlag()
    {
        Assert.Equal(new PlayerId(2), LocalPlayerBinding.Resolve(["--local-player", "2"]));
        Assert.Equal(new PlayerId(2), LocalPlayerBinding.Resolve(["--smoke-test", "--Local-Player", "2"]));
    }

    [Fact]
    public void Resolve_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => LocalPlayerBinding.Resolve(["--local-player", "0"]));
        Assert.Throws<ArgumentException>(() => LocalPlayerBinding.Resolve(["--local-player", "x"]));
    }

    [Fact]
    public void Resolve_TrailingLocalPlayerFlag_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => LocalPlayerBinding.Resolve(["--local-player"]));
        Assert.Contains("--local-player", ex.Message);
    }

    [Fact]
    public void Resolve_LocalPlayerFollowedByAnotherFlag_Throws()
    {
        Assert.Throws<ArgumentException>(() => LocalPlayerBinding.Resolve(["--local-player", "--smoke-test"]));
    }

    [Fact]
    public void DisplayOptions_DefaultsLocalPlayerAndRequiresPositiveTicks()
    {
        var display = new SfmlDisplayOptions(800, 600, "Test", GameSimulation.DefaultTicksPerSecond);
        Assert.Equal(new PlayerId(1), display.LocalPlayerId);
        Assert.Equal(GameSimulation.DefaultTicksPerSecond, display.TicksPerSecond);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SfmlDisplayOptions(800, 600, "Test", 0));
    }

    [Fact]
    public void EnsureSeatControllable_ValidSeat_DoesNotThrow()
    {
        // Default map seats players 1 and 2, both with a commander.
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        LocalPlayerBinding.EnsureSeatControllable(simulation, new PlayerId(1));
        LocalPlayerBinding.EnsureSeatControllable(simulation, new PlayerId(2));
    }

    [Fact]
    public void EnsureSeatControllable_SeatNotOnMap_ThrowsFriendlyMessage()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var ex = Assert.Throws<InvalidOperationException>(
            () => LocalPlayerBinding.EnsureSeatControllable(simulation, new PlayerId(3)));
        // Friendly message names the seat and lists what is available, instead of a raw First() throw.
        Assert.Contains("Local player 3", ex.Message);
        Assert.Contains("Available player ids", ex.Message);
    }
}
