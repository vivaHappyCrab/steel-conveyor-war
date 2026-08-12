namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// H02: largest-remainder research schedulers are authoritative hidden state and must enter the hash surface.
/// </summary>
public sealed class ResearchRemainderHashTests
{
    [Fact]
    public void AlgorithmVersion_IsNine()
    {
        Assert.Equal(9, SimulationStateHasher.AlgorithmVersion);
    }

    [Fact]
    public void DifferentTrackRemainder_ProducesDifferentHash()
    {
        var baseline = GameSimulation.CreateNewGame(42);
        var mutated = GameSimulation.CreateNewGame(42);
        Assert.Equal(baseline.ComputeStateHash(), mutated.ComputeStateHash());

        var research = mutated.GetPlayer(new PlayerId(1)).Research;
        research.TrackSelectionRemainder["military"] = 7;

        Assert.NotEqual(baseline.ComputeStateHash(), mutated.ComputeStateHash());
    }

    [Fact]
    public void DifferentProjectRemainder_ProducesDifferentHash()
    {
        var baseline = GameSimulation.CreateNewGame(42);
        var mutated = GameSimulation.CreateNewGame(42);

        var research = mutated.GetPlayer(new PlayerId(1)).Research;
        research.ProjectSelectionRemainder[TechnologyId.LightBot] = 3;

        Assert.NotEqual(baseline.ComputeStateHash(), mutated.ComputeStateHash());
    }

    [Fact]
    public void SameSeedIdleRuns_ProduceIdenticalHash_IncludingRemainders()
    {
        var hashA = RunIdle(seed: 42, ticks: 90);
        var hashB = RunIdle(seed: 42, ticks: 90);
        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length);
    }

    private static string RunIdle(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(seed);
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }
}
