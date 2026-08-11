using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

// R30: GetAffordableBuildCount must mirror the real payment sources (commander inventory + owned hubs
// within CommanderInteractRadius) so the build UI cannot show a count that a real ghost-build would
// not honor.
public sealed class BuildAffordabilityQueryTests
{
    private static (GameSimulation Simulation, PlayerId Blue, WorldEntity Commander) NewGame()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var blue = new PlayerId(1);
        var commander = simulation.World.Entities.Single(e => e.Kind == EntityKind.Commander && e.OwnerId == blue);

        // The starting layout seeds a hub near the commander; zero all owned hub + commander stock so
        // the affordability query only sees the amounts each test explicitly injects.
        Assert.True(simulation.ClearEntityInventoryForTests(commander.Id));
        foreach (var hub in simulation.World.Entities.Where(e => e.Kind == EntityKind.Hub && e.OwnerId == blue))
        {
            Assert.True(simulation.ClearEntityInventoryForTests(hub.Id));
        }

        return (simulation, blue, commander);
    }

    [Fact]
    public void AffordableCount_CommanderInventoryOnly_FloorsToWholeBuilds()
    {
        var (simulation, _, commander) = NewGame();

        // Mine costs 20 IronPlate; 65 stock affords 3 (floored), not 3.25.
        commander.Inventory.Add(ItemId.IronPlate, 65);

        Assert.Equal(3, simulation.GetAffordableBuildCount(commander, EntityKind.Mine));
    }

    [Fact]
    public void AffordableCount_IncludesNearbyOwnedHubStock()
    {
        var (simulation, blue, commander) = NewGame();
        commander.Inventory.Add(ItemId.IronPlate, 60); // affords 3 mines alone

        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.Hub,
            new TilePosition(commander.Position.X + 1, commander.Position.Y),
            blue,
            out var hubId));
        Assert.True(simulation.TryAddOutputItemToEntity(hubId, ItemId.IronPlate, 20)); // +1 mine worth

        Assert.Equal(4, simulation.GetAffordableBuildCount(commander, EntityKind.Mine));
    }

    [Fact]
    public void AffordableCount_IsLimitedByScarcestItem()
    {
        var (simulation, _, commander) = NewGame();

        // MachineGunTurret costs 20 IronPlate + 10 CopperPlate: iron affords 5, copper affords 3 -> 3.
        commander.Inventory.Add(ItemId.IronPlate, 100);
        commander.Inventory.Add(ItemId.CopperPlate, 30);

        Assert.Equal(3, simulation.GetAffordableBuildCount(commander, EntityKind.MachineGunTurret));
    }

    [Fact]
    public void AffordableCount_UnknownKind_IsZero()
    {
        var (simulation, _, commander) = NewGame();
        Assert.Equal(0, simulation.GetAffordableBuildCount(commander, EntityKind.Commander));
    }
}
