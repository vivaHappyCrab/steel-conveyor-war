using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

// R29: the persistent largest-remainder rotation must distribute selections proportionally to the
// configured weights over the long run (no systematic last-entry bias) and be fully deterministic.
public sealed class ResearchAllocationDeterminismTests
{
    private static List<string> RunRotation(IReadOnlyDictionary<string, int> weights, int iterations)
    {
        var candidates = weights.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var accumulator = new Dictionary<string, int>();
        var picks = new List<string>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            picks.Add(ResearchSystem.SelectByLargestRemainder(
                candidates,
                id => weights[id],
                accumulator));
        }

        return picks;
    }

    [Fact]
    public void SeventyThirtySplit_ConvergesToRatio_NotLastEntryBias()
    {
        var weights = new Dictionary<string, int> { ["a"] = 7_000, ["b"] = 3_000 };
        var picks = RunRotation(weights, 1_000);

        var a = picks.Count(id => id == "a");
        var b = picks.Count(id => id == "b");

        // Neither track is starved (the old code collapsed the minority to 0), and each is within
        // one selection of its exact share.
        Assert.InRange(a, 699, 701);
        Assert.InRange(b, 299, 301);
    }

    [Fact]
    public void EqualWeights_AlternateFairly()
    {
        var weights = new Dictionary<string, int> { ["a"] = 1, ["b"] = 1 };
        var picks = RunRotation(weights, 1_000);

        Assert.Equal(500, picks.Count(id => id == "a"));
        Assert.Equal(500, picks.Count(id => id == "b"));
    }

    [Fact]
    public void Rotation_IsDeterministicAcrossRuns()
    {
        var weights = new Dictionary<string, int> { ["a"] = 5_500, ["b"] = 3_000, ["c"] = 1_500 };

        var first = RunRotation(weights, 500);
        var second = RunRotation(weights, 500);

        Assert.Equal(first, second);
    }

    [Fact]
    public void AllZeroWeights_FallsBackToFirstCandidate_NeverThrows()
    {
        var weights = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0 };
        var picks = RunRotation(weights, 10);

        Assert.All(picks, pick => Assert.Equal("a", pick));
    }
}
