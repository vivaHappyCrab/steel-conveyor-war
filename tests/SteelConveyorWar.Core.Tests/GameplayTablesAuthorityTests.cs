namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// H01: match-scoped <see cref="GameplayTablesCatalog"/> must drive runtime rules, not Embedded via
/// <see cref="MvpDefinitions"/> facades.
/// </summary>
public sealed class GameplayTablesAuthorityTests
{
    [Fact]
    public void CustomMaxHealth_IsAppliedAtSpawn()
    {
        var tables = WithCommanderMaxHealth(999);
        var simulation = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 42, GameplayTables = tables });

        var commander = simulation.World.Entities.Single(
            entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        Assert.Equal(999, commander.MaxHealth);
        Assert.Equal(999, commander.Health);
        Assert.Equal(600, MvpDefinitions.GetStats(EntityKind.Commander).MaxHealth);
    }

    [Fact]
    public void CustomPowerDemand_AffectsFill()
    {
        var embeddedDemand = GameplayTablesCatalog.Embedded.GetPowerDemand(EntityKind.Assembler);
        Assert.True(embeddedDemand > 0);

        var powerDemand = GameplayTablesCatalog.Embedded.PowerDemand.ToDictionary(pair => pair.Key, pair => pair.Value);
        powerDemand[EntityKind.Assembler] = embeddedDemand * 3;
        var tables = GameplayTablesCatalog.Embedded with { PowerDemand = powerDemand };

        var simulation = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 42, GameplayTables = tables });
        Assert.True(simulation.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Assembler, NearBlue(simulation, 5, -2), out var assemblerId));
        AdvanceTicks(simulation, 30);

        var assembler = simulation.World.GetEntity(assemblerId)!;
        Assert.Equal(tables.GetEnergyBufferCapacity(EntityKind.Assembler), assembler.EnergyBufferCapacity);
        Assert.NotEqual(
            MvpDefinitions.GetEnergyBufferCapacity(EntityKind.Assembler),
            assembler.EnergyBufferCapacity);
        Assert.Equal(embeddedDemand * 3 * MvpDefinitions.EnergyBufferCapacityFactor, assembler.EnergyBufferCapacity);
    }

    [Fact]
    public void CustomRecipe_UsedByFactory()
    {
        var embedded = GameplayTablesCatalog.Embedded.ProductionRecipes[EntityKind.BasicTank];
        var recipes = GameplayTablesCatalog.Embedded.ProductionRecipes.ToDictionary(pair => pair.Key, pair => pair.Value);
        recipes[EntityKind.BasicTank] = embedded with
        {
            WorkTicks = 5,
            Inputs = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 1 }
        };
        var tables = GameplayTablesCatalog.Embedded with { ProductionRecipes = recipes };

        var simulation = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 42, GameplayTables = tables });
        var playerId = new PlayerId(1);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == playerId && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        AdvanceTicks(simulation, 30);

        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, playerId, EntityKind.BasicTank, 1));
        Assert.True(simulation.TrySetEnergyBufferForTests(factoryId, int.MaxValue));
        simulation.AddItemToEntity(factoryId, ItemId.IronPlate, 1);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, playerId, EntityKind.BasicTank));

        AdvanceTicks(simulation, 1);
        var factory = simulation.World.GetEntity(factoryId)!;
        Assert.Equal(5, factory.WorkTicksTotal);
        AdvanceTicks(simulation, 6);
        Assert.Equal(0, factory.InputBuffer.Count(ItemId.IronPlate));
        Assert.Contains(
            simulation.World.Entities,
            entity => entity.IsAlive && entity.Kind == EntityKind.BasicTank && entity.OwnerId == playerId);
        Assert.Equal(
            embedded.WorkTicks,
            MvpDefinitions.ProductionRecipes[EntityKind.BasicTank].WorkTicks);
    }

    [Fact]
    public void CustomFootprint_AffectsPlacement()
    {
        var footprints = GameplayTablesCatalog.Embedded.Footprints.ToDictionary(pair => pair.Key, pair => pair.Value);
        footprints[EntityKind.Smelter] = new WorldSize(20, 20);
        var tables = GameplayTablesCatalog.Embedded with { Footprints = footprints };

        var baseline = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(baseline.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(baseline, 5, -2), out _));

        var custom = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 42, GameplayTables = tables });
        Assert.False(custom.TryPlaceGhostBuild(new PlayerId(1), EntityKind.Smelter, NearBlue(custom, 5, -2), out _));
        Assert.Equal(new WorldSize(2, 2), MvpDefinitions.GetFootprint(EntityKind.Smelter));
    }

    [Fact]
    public void CustomCatalog_DualRun_IdenticalHash()
    {
        var tables = WithCommanderMaxHealth(777);
        var hashA = RunCustomCatalogScenario(tables, seed: 42);
        var hashB = RunCustomCatalogScenario(tables, seed: 42);
        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length);

        var embeddedHash = RunCustomCatalogScenario(GameplayTablesCatalog.Embedded, seed: 42);
        Assert.NotEqual(hashA, embeddedHash);
    }

    private static GameplayTablesCatalog WithCommanderMaxHealth(int maxHealth)
    {
        var stats = GameplayTablesCatalog.Embedded.EntityStats.ToDictionary(pair => pair.Key, pair => pair.Value);
        stats[EntityKind.Commander] = GameplayTablesCatalog.Embedded.GetStats(EntityKind.Commander) with { MaxHealth = maxHealth };
        return GameplayTablesCatalog.Embedded with { EntityStats = stats };
    }

    private static string RunCustomCatalogScenario(GameplayTablesCatalog tables, int seed)
    {
        var simulation = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = seed, GameplayTables = tables });
        var playerId = new PlayerId(1);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.SolarPanel, NearBlue(simulation, 6, 4), out _));
        AdvanceTicks(simulation, 30);
        return simulation.ComputeStateHash();
    }

    private static void AdvanceTicks(GameSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }
    }

    private static TilePosition NearBlue(GameSimulation simulation, int x, int yOffsetFromMid, int playerId = 1)
    {
        var midY = simulation.World.Size.Height / 2;
        if (playerId == 1)
        {
            return new TilePosition(x, midY + yOffsetFromMid);
        }

        return new TilePosition(simulation.World.Size.Width - 1 - x, midY + yOffsetFromMid);
    }
}
