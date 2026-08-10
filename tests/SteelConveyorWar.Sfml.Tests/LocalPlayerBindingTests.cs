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
    public void DisplayOptions_ThreeArgCtor_UsesDefaultLocalPlayer()
    {
        var display = new SfmlDisplayOptions(800, 600, "Test");
        Assert.Equal(new PlayerId(1), display.LocalPlayerId);
    }
}
