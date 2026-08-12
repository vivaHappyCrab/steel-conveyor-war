namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// M11: session identity includes map/roster/TPS/seed; mismatched peers fail EnsureMatch.
/// </summary>
public sealed class SessionManifestTests
{
    [Fact]
    public void SessionManifest_IsEqual_ForIdenticalSessions()
    {
        var a = GameSimulation.CreateNewGame(randomSeed: 42);
        var b = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.Equal(SimulationSessionManifest.Compute(a), SimulationSessionManifest.Compute(b));
        SimulationSessionManifest.EnsureMatch(a, b);
    }

    [Fact]
    public void EnsureMatch_Throws_OnMismatchedTps()
    {
        var a = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, TicksPerSecond = 30 });
        var b = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, TicksPerSecond = 60 });
        Assert.NotEqual(SimulationSessionManifest.Compute(a), SimulationSessionManifest.Compute(b));
        var ex = Assert.Throws<SessionManifestMismatchException>(() => SimulationSessionManifest.EnsureMatch(a, b));
        Assert.NotEqual(ex.LocalHash, ex.RemoteHash);
    }

    [Fact]
    public void EnsureMatch_Throws_OnMismatchedSeed()
    {
        var a = GameSimulation.CreateNewGame(randomSeed: 42);
        var b = GameSimulation.CreateNewGame(randomSeed: 43);
        Assert.Throws<SessionManifestMismatchException>(() => SimulationSessionManifest.EnsureMatch(a, b));
    }

    [Fact]
    public void EnsureMatch_Throws_OnMismatchedMapId()
    {
        var map = MapSettings.Default1v1 with { MapId = "alt" };
        var a = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42 });
        var b = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, Map = map });
        Assert.Throws<SessionManifestMismatchException>(() => SimulationSessionManifest.EnsureMatch(a, b));
    }

    [Fact]
    public void ContentMayMatch_WhileSessionDiffers_OnTps()
    {
        var a = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, TicksPerSecond = 30 });
        var b = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, TicksPerSecond = 60 });
        Assert.Equal(SimulationContentManifest.Compute(a), SimulationContentManifest.Compute(b));
        Assert.NotEqual(SimulationSessionManifest.Compute(a), SimulationSessionManifest.Compute(b));
    }
}
