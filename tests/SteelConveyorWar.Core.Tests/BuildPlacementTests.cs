using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class BuildPlacementTests
{
    [Fact]
    public void ExplainBuildPlacement_ReportsEachBlockWithoutSpending()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == player);
        var ironBefore = commander.Inventory.Count(ItemId.IronPlate);

        AssertBlock(simulation, commander, EntityKind.Commander, commander.Position, BuildPlacementBlock.UnknownKind, canPlace: false, canQueue: false);
        AssertBlock(simulation, commander, EntityKind.MachineGunTurret, GrassNear(simulation, commander, 2, 0), BuildPlacementBlock.Locked, false, false);
        AssertBlock(simulation, commander, EntityKind.Mine, GrassNear(simulation, commander, 2, 1), BuildPlacementBlock.WrongResource, false, false);
        AssertBlock(simulation, commander, EntityKind.Wall, commander.Position, BuildPlacementBlock.Occupied, false, false);
        AssertBlock(simulation, commander, EntityKind.Conveyor, new TilePosition(-1, 0), BuildPlacementBlock.OutsideMap, false, false);

        var mountain = FindTile(simulation, tile => simulation.World.GetTerrain(tile) == TerrainType.Mountain);
        AssertBlock(simulation, commander, EntityKind.Wall, mountain, BuildPlacementBlock.Unwalkable, false, false);

        var far = FindTile(simulation, tile =>
            simulation.World.GetTerrain(tile).IsWalkable()
            && tile.ManhattanDistance(commander.Position) > 30
            && !simulation.World.GetEntitiesAt(tile).Any(entity => entity.IsAlive));
        var queued = simulation.ExplainBuildPlacement(commander.Id, EntityKind.Conveyor, far, player);
        Assert.Equal(BuildPlacementBlock.OutOfRange, queued.Block);
        Assert.False(queued.CanPlaceNow);
        Assert.True(queued.CanQueueWalk);
        Assert.True(simulation.TryQueueCommanderBuild(commander.Id, EntityKind.Conveyor, far, actor: player));

        var near = GrassNear(simulation, commander, 3, 0);
        commander.Inventory.Clear();
        foreach (var hub in simulation.World.Entities.Where(entity => entity.Kind == EntityKind.Hub && entity.OwnerId == player))
        {
            hub.Inventory.Clear();
        }

        var broke = simulation.ExplainBuildPlacement(commander.Id, EntityKind.Conveyor, near, player);
        Assert.Equal(BuildPlacementBlock.Unaffordable, broke.Block);
        Assert.False(broke.CanPlaceNow);
        Assert.False(broke.CanQueueWalk);
        Assert.False(simulation.TryQueueCommanderBuild(commander.Id, EntityKind.Conveyor, near, actor: player));

        simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, 20);
        simulation.AddItemToEntity(commander.Id, ItemId.CopperPlate, 10);
        var ready = simulation.ExplainBuildPlacement(commander.Id, EntityKind.Conveyor, near, player);
        Assert.Equal(BuildPlacementBlock.None, ready.Block);
        Assert.True(ready.CanPlaceNow);
        Assert.False(ready.CanQueueWalk);
        Assert.Equal(20, commander.Inventory.Count(ItemId.IronPlate));
        Assert.True(ironBefore > 0);
    }

    private static void AssertBlock(
        GameSimulation simulation,
        WorldEntity commander,
        EntityKind kind,
        TilePosition tile,
        BuildPlacementBlock block,
        bool canPlace,
        bool canQueue)
    {
        var report = simulation.ExplainBuildPlacement(commander.Id, kind, tile, commander.OwnerId);
        Assert.Equal(block, report.Block);
        Assert.Equal(canPlace, report.CanPlaceNow);
        Assert.Equal(canQueue, report.CanQueueWalk);
    }

    private static TilePosition GrassNear(GameSimulation simulation, WorldEntity commander, int dx, int dy)
    {
        var tile = new TilePosition(commander.Position.X + dx, commander.Position.Y + dy);
        Assert.True(simulation.World.IsInside(tile));
        Assert.Equal(TerrainType.Grass, simulation.World.GetTerrain(tile));
        Assert.DoesNotContain(simulation.World.GetEntitiesAt(tile), entity => entity.IsAlive);
        return tile;
    }

    private static TilePosition FindTile(GameSimulation simulation, Func<TilePosition, bool> match)
    {
        var world = simulation.World;
        for (var y = 0; y < world.Size.Height; y++)
        {
            for (var x = 0; x < world.Size.Width; x++)
            {
                var tile = new TilePosition(x, y);
                if (match(tile))
                {
                    return tile;
                }
            }
        }

        throw new InvalidOperationException("No matching tile.");
    }
}
