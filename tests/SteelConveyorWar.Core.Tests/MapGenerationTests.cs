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
    public void StartingIronAndCopper_HaveThreeAndTwoPatchesPerSide()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var half = simulation.World.Size.Width / 2;
        var ironLeft = CountOrePatches(simulation, TerrainType.IronOre, x => x < half);
        var copperLeft = CountOrePatches(simulation, TerrainType.CopperOre, x => x < half);
        var ironRight = CountOrePatches(simulation, TerrainType.IronOre, x => x >= half);
        var copperRight = CountOrePatches(simulation, TerrainType.CopperOre, x => x >= half);
        Assert.Equal(3, ironLeft);
        Assert.Equal(2, copperLeft);
        Assert.Equal(3, ironRight);
        Assert.Equal(2, copperRight);
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

    [Fact]
    public void Mountains_RespectCapClearanceAndMinChunkSize()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var width = simulation.World.Size.Width;
        var height = simulation.World.Size.Height;
        var mountains = EnumerateTerrain(simulation).Where(t => t.Type == TerrainType.Mountain).ToList();
        var resources = EnumerateTerrain(simulation).Where(t => t.Type.IsResource()).ToList();

        Assert.True(mountains.Count <= width * height * 5 / 100);
        Assert.NotEmpty(mountains);

        foreach (var mountain in mountains)
        {
            foreach (var resource in resources)
            {
                var chebyshev = Math.Max(Math.Abs(mountain.X - resource.X), Math.Abs(mountain.Y - resource.Y));
                Assert.True(chebyshev >= 10, $"mountain ({mountain.X},{mountain.Y}) too close to resource ({resource.X},{resource.Y})");
            }
        }

        foreach (var entity in simulation.World.Entities.Where(e => e.Kind is EntityKind.Commander or EntityKind.Bastion or EntityKind.Hub or EntityKind.SolarPanel))
        {
            foreach (var tile in GameWorld.GetFootprintTiles(entity.Kind, entity.Position, simulation.GameplayTables))
            {
                Assert.NotEqual(TerrainType.Mountain, simulation.World.GetTerrain(tile));
            }
        }

        foreach (var component in FloodMountainComponents(mountains, width, height))
        {
            var boxWidth = component.Max(t => t.X) - component.Min(t => t.X) + 1;
            var boxHeight = component.Max(t => t.Y) - component.Min(t => t.Y) + 1;
            Assert.True(component.Count >= 10, $"mountain chunk area {component.Count}");
            Assert.True(Math.Min(boxWidth, boxHeight) >= 2, $"mountain chunk short axis {Math.Min(boxWidth, boxHeight)}");
            Assert.True(Math.Max(boxWidth, boxHeight) >= 5, $"mountain chunk long axis {Math.Max(boxWidth, boxHeight)}");
        }
    }

    [Fact]
    public void GroundCannotPlaceOnMountain_FlyingStopsOnlyOnWalkable()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var mountain = EnumerateTerrain(simulation).First(t => t.Type == TerrainType.Mountain);
        var mountainTile = new TilePosition(mountain.X, mountain.Y);
        var player = new PlayerId(1);

        Assert.False(simulation.TryPlaceGhostBuild(player, EntityKind.Wall, mountainTile, out _));
        Assert.False(simulation.TryPlaceGhostBuild(player, EntityKind.Conveyor, mountainTile, out _));

        var commander = simulation.World.Entities.Single(e => e.Kind == EntityKind.Commander && e.OwnerId == player);
        Assert.True(simulation.TryIssueMoveCommand(commander.Id, player, mountainTile));
        AdvanceTicks(simulation, 30);
        Assert.NotEqual(mountainTile, commander.Position);
        Assert.NotEqual(TerrainType.Mountain, simulation.World.GetTerrain(commander.Position));

        Assert.Equal(MovementType.Flying, simulation.GameplayTables.GetStats(EntityKind.Scout).MovementType);
        Assert.Equal(MovementType.Ground, simulation.GameplayTables.GetStats(EntityKind.Commander).MovementType);
        Assert.Equal(new WorldSize(2, 2), simulation.GameplayTables.GetFootprint(EntityKind.Refinery));
        Assert.Equal(new WorldSize(2, 3), simulation.GameplayTables.GetFootprint(EntityKind.CoalPlant));
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

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }

    private static int CountOrePatches(GameSimulation simulation, TerrainType type, Func<int, bool> xPredicate)
    {
        var tiles = EnumerateTerrain(simulation)
            .Where(t => t.Type == type && xPredicate(t.X))
            .Select(t => (t.X, t.Y))
            .ToHashSet();
        var seen = new HashSet<(int X, int Y)>();
        var patches = 0;
        var offsets = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var start in tiles)
        {
            if (!seen.Add(start))
            {
                continue;
            }

            patches++;
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                foreach (var (dx, dy) in offsets)
                {
                    var next = (tile.X + dx, tile.Y + dy);
                    if (tiles.Contains(next) && seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return patches;
    }

    private static List<List<(int X, int Y)>> FloodMountainComponents(
        List<(int X, int Y, TerrainType Type)> mountains,
        int width,
        int height)
    {
        var mountainSet = mountains.Select(t => (t.X, t.Y)).ToHashSet();
        var seen = new HashSet<(int X, int Y)>();
        var components = new List<List<(int X, int Y)>>();
        var offsets = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        foreach (var start in mountainSet)
        {
            if (!seen.Add(start))
            {
                continue;
            }

            var component = new List<(int X, int Y)>();
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                component.Add(tile);
                foreach (var (dx, dy) in offsets)
                {
                    var next = (tile.X + dx, tile.Y + dy);
                    if (next.Item1 < 0 || next.Item2 < 0 || next.Item1 >= width || next.Item2 >= height)
                    {
                        continue;
                    }

                    if (mountainSet.Contains(next) && seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            components.Add(component);
        }

        return components;
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
