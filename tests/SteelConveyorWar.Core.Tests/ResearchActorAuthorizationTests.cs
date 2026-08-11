using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

// R26: research may only be steered by the player who owns it. A cross-player actor (e.g. selecting a
// visible enemy laboratory in the UI) must be rejected at the Core boundary as defense-in-depth.
public sealed class ResearchActorAuthorizationTests
{
    private static (GameSimulation Simulation, PlayerId P1, PlayerId P2, TechnologyId Tech) NewGame()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var p1 = new PlayerId(1);
        var p2 = new PlayerId(2);
        // Any technology present in the catalog exercises the authorization gate.
        var tech = simulation.ResearchCatalog.Technologies.Keys.First();
        return (simulation, p1, p2, tech);
    }

    [Fact]
    public void TrySelectResearch_ForeignActor_IsRejected()
    {
        var (simulation, p1, p2, tech) = NewGame();
        Assert.Equal(ResearchCommandResult.NotAvailable, simulation.TrySelectResearch(p2, tech, actor: p1));
    }

    [Fact]
    public void TryStartResearch_ForeignActor_IsRejected()
    {
        var (simulation, p1, p2, tech) = NewGame();
        Assert.False(simulation.TryStartResearch(p2, tech, actor: p1));
    }

    [Fact]
    public void OwnerActor_BehavesIdenticallyToLegacyNullActor()
    {
        // The gate must be transparent for the rightful owner: supplying actor == owner yields the same
        // result the pre-R26 (no-actor) API would, regardless of the technology's selectability.
        var (simulationA, p1, _, tech) = NewGame();
        var (simulationB, p1B, _, techB) = NewGame();

        var withOwnerActor = simulationA.TrySelectResearch(p1, tech, actor: p1);
        var legacyNullActor = simulationB.TrySelectResearch(p1B, techB);

        Assert.Equal(legacyNullActor, withOwnerActor);
    }
}
