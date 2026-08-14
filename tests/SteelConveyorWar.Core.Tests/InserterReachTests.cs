using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class InserterReachTests
{
    [Fact]
    public void LongReach_PullsFromTwoTilesAway()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var hubPos = NearBlue(simulation, 0, 4);
        var inserterPos = new TilePosition(hubPos.X + 3, hubPos.Y);
        var destPos = new TilePosition(inserterPos.X + 2, inserterPos.Y);

        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.Hub, hubPos, out var sourceHubId));
        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.Inserter, inserterPos, out var inserterId));
        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.Hub, destPos, out var destHubId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetInserterReach(inserterId, player, longReach: true));
        var inserter = simulation.World.GetEntity(inserterId)!;
        Assert.True(inserter.InserterLongReach);
        var pickup = MvpDefinitions.InserterPickupTile(inserter.Position, inserter.Direction, true);
        Assert.Equal(new TilePosition(hubPos.X + 1, hubPos.Y), pickup);
        Assert.Equal(sourceHubId, simulation.World.GetTopEntityAt(pickup)!.Id);
        Assert.Null(simulation.World.GetTopEntityAt(
            MvpDefinitions.InserterPickupTile(inserter.Position, inserter.Direction, false)));

        simulation.AddItemToEntity(sourceHubId, ItemId.IronOre, 1);
        AdvanceTicks(simulation, MvpDefinitions.InserterTransferTicks + 4);

        Assert.Equal(0, simulation.World.GetEntity(sourceHubId)!.Inventory.Count(ItemId.IronOre));
        Assert.True(simulation.World.GetEntity(destHubId)!.Inventory.Count(ItemId.IronOre) >= 1
                    || simulation.World.GetEntity(inserterId)!.HeldItem == ItemId.IronOre);
    }

    [Fact]
    public void AdjacentReach_IgnoresTileTwoAway()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var hubPos = NearBlue(simulation, 0, 4);
        var inserterPos = new TilePosition(hubPos.X + 3, hubPos.Y);

        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.Hub, hubPos, out var sourceHubId));
        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.Inserter, inserterPos, out var inserterId));
        AdvanceTicks(simulation, 30);

        simulation.AddItemToEntity(sourceHubId, ItemId.IronOre, 1);
        AdvanceTicks(simulation, MvpDefinitions.InserterTransferTicks + 4);
        Assert.Equal(1, simulation.World.GetEntity(sourceHubId)!.Inventory.Count(ItemId.IronOre));
        Assert.Null(simulation.World.GetEntity(inserterId)!.HeldItem);
    }

    [Fact]
    public void PlaceFromCommander_CopiesLongReachOntoGhost()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = new PlayerId(1);
        var commander = simulation.World.Entities.Single(e => e.Kind == EntityKind.Commander && e.OwnerId == player);
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.IronPlate, 10));
        Assert.True(simulation.AddItemToEntity(commander.Id, ItemId.CopperPlate, 10));

        Assert.True(simulation.TryPlaceGhostBuildFromCommander(
            commander.Id,
            EntityKind.Inserter,
            NearBlue(simulation, 6, 0),
            out var ghostId,
            Direction.East,
            inserterLongReach: true));
        Assert.True(simulation.World.GetEntity(ghostId)!.InserterLongReach);
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }

    private static TilePosition NearBlue(GameSimulation simulation, int x, int yOffsetFromMid)
    {
        var midY = simulation.World.Size.Height / 2;
        return new TilePosition(x, midY + yOffsetFromMid);
    }
}
