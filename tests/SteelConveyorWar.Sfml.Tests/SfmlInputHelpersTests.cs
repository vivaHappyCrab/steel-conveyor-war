using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

/// <summary>L02: commander focus helpers must not throw after local commander death.</summary>
public sealed class SfmlInputHelpersTests
{
    [Fact]
    public void EnsureLocalCommanderSelected_NoLivingCommander_ReturnsFalse_NoThrow()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var local = new PlayerId(1);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == local && entity.Kind == EntityKind.Commander && entity.IsAlive);
        simulation.DamageEntity(commander.Id, commander.Health + 1);

        int? selected = commander.Id;
        Assert.False(SfmlInputHelpers.EnsureLocalCommanderSelected(simulation, local, ref selected));
        Assert.Equal(commander.Id, selected);
    }

    [Fact]
    public void EnsureLocalCommanderSelected_LivingCommander_SelectsAndReturnsTrue()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var local = new PlayerId(1);
        var commander = simulation.World.Entities.First(entity =>
            entity.OwnerId == local && entity.Kind == EntityKind.Commander && entity.IsAlive);

        int? selected = null;
        Assert.True(SfmlInputHelpers.EnsureLocalCommanderSelected(simulation, local, ref selected));
        Assert.Equal(commander.Id, selected);
    }
}
