using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public class MapGenerationTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalTerrain()
    {
        var first = GameSimulation.CreateNewGame(randomSeed: 42);
        var second = GameSimulation.CreateNewGame(randomSeed: 42);

        AssertTerrainEqual(first, second);
    }

    [Fact]
    public void DifferentSeeds_CanProduceDifferentTerrain()
    {
        var baseline = GameSimulation.CreateNewGame(randomSeed: 1);
        var foundDifference = false;

        foreach (var seed in new[] { 2, 7, 99, 12345, 99991 })
        {
            var other = GameSimulation.CreateNewGame(randomSeed: seed);
            if (!TerrainsEqual(baseline, other))
            {
                foundDifference = true;
                break;
            }
        }

        Assert.True(foundDifference, "Expected at least one alternate seed to produce a different terrain layout.");
    }

    [Fact]
    public void Terrain_IsLeftRightMirrorSymmetric()
    {
        // Even width (192): no center column. Odd widths would leave the middle column unpaired.
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var width = simulation.World.Size.Width;
        var height = simulation.World.Size.Height;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var left = simulation.World.GetTerrain(new TilePosition(x, y));
                var mirrored = simulation.World.GetTerrain(new TilePosition(width - 1 - x, y));
                Assert.Equal(left, mirrored);
            }
        }
    }

    [Fact]
    public void StartingResources_ExistNearStartsAndCenter()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var half = simulation.World.Size.Width / 2;
        var midY = simulation.World.Size.Height / 2;

        Assert.Equal(new WorldSize(192, 112), simulation.World.Size);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.IronOre && t.X < half);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.CopperOre && t.X < half);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.IronOre && t.X >= half);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.CopperOre && t.X >= half);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.Coal);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.Oil);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.Coal && t.X >= half - 28 && t.X < half);
        Assert.Contains(EnumerateTerrain(simulation), t => t.Type == TerrainType.Oil && t.X >= half - 28 && t.X < half);

        var startX = 4;
        var nearestIron = EnumerateTerrain(simulation)
            .Where(t => t.Type == TerrainType.IronOre && t.X < half)
            .Min(t => Math.Abs(t.X - startX) + Math.Abs(t.Y - midY));
        var nearestCoal = EnumerateTerrain(simulation)
            .Where(t => t.Type == TerrainType.Coal && t.X < half)
            .Min(t => Math.Abs(t.X - startX) + Math.Abs(t.Y - midY));
        Assert.True(nearestCoal > nearestIron);
    }

    [Fact]
    public void StartingResourcePatches_HaveAtLeastFourByFourBoundingBox()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        foreach (var type in new[] { TerrainType.IronOre, TerrainType.CopperOre, TerrainType.Coal, TerrainType.Oil })
        {
            var tiles = EnumerateTerrain(simulation).Where(t => t.Type == type && t.X < simulation.World.Size.Width / 2).ToList();
            Assert.NotEmpty(tiles);
            var width = tiles.Max(t => t.X) - tiles.Min(t => t.X) + 1;
            var height = tiles.Max(t => t.Y) - tiles.Min(t => t.Y) + 1;
            Assert.True(width >= 4, $"{type} width {width}");
            Assert.True(height >= 4, $"{type} height {height}");
        }
    }

    private static void AssertTerrainEqual(GameSimulation left, GameSimulation right)
    {
        Assert.True(TerrainsEqual(left, right));
    }

    private static bool TerrainsEqual(GameSimulation left, GameSimulation right)
    {
        if (left.World.Size != right.World.Size)
        {
            return false;
        }

        for (var y = 0; y < left.World.Size.Height; y++)
        {
            for (var x = 0; x < left.World.Size.Width; x++)
            {
                var position = new TilePosition(x, y);
                if (left.World.GetTerrain(position) != right.World.GetTerrain(position))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static IEnumerable<(int X, int Y, TerrainType Type)> EnumerateTerrain(GameSimulation simulation)
    {
        for (var y = 0; y < simulation.World.Size.Height; y++)
        {
            for (var x = 0; x < simulation.World.Size.Width; x++)
            {
                yield return (x, y, simulation.World.GetTerrain(new TilePosition(x, y)));
            }
        }
    }
}
