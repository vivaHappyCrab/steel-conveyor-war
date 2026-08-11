using System.Reflection;

namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R14: authoritative mutation / cheat seams must not be reachable from out-of-assembly hosts
/// (Sfml / Headless / plugins). They stay usable inside Core and Core.Tests (InternalsVisibleTo) but
/// are never <c>public</c>. This test fails if any of them is promoted back to the public surface.
/// </summary>
public sealed class MutationHookVisibilityTests
{
    [Fact]
    public void TryAddOutputItemToEntity_IsInternalNotPublic()
    {
        var type = typeof(GameSimulation);

        Assert.Null(type.GetMethod("TryAddOutputItemToEntity", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("TryAddOutputItemToEntity", BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void TryConsumeBuildingEnergy_IsInternalNotPublic_OnSimulationAndPowerSystem()
    {
        Assert.Null(typeof(GameSimulation)
            .GetMethod("TryConsumeBuildingEnergy", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(GameSimulation)
            .GetMethod("TryConsumeBuildingEnergy", BindingFlags.NonPublic | BindingFlags.Instance));

        Assert.Null(typeof(PowerSystem)
            .GetMethod("TryConsumeBuildingEnergy", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(PowerSystem)
            .GetMethod("TryConsumeBuildingEnergy", BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void ExtractedSystems_AreNotPublicTypes()
    {
        // Systems that accept live authoritative state must not be public.
        Assert.False(typeof(ResearchSystem).IsPublic);
        Assert.False(typeof(PowerSystem).IsPublic);
        Assert.False(typeof(CombatSystem).IsPublic);
    }
}
