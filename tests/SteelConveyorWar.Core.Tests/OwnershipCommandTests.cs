namespace SteelConveyorWar.Core.Tests;

public sealed class OwnershipCommandTests
{
    [Fact]
    public void TryIssueMoveCommand_RejectsForeignCommander()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var enemy = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        var start = enemy.Position;

        Assert.False(simulation.TryIssueMoveCommand(enemy.Id, new PlayerId(1), new TilePosition(start.X + 1, start.Y)));
        Assert.Null(simulation.World.GetEntity(enemy.Id)!.MoveTarget);
    }

    [Fact]
    public void TryStopCommander_RejectsForeignCommander()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var enemy = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.TryIssueMoveCommand(enemy.Id, new PlayerId(2), new TilePosition(enemy.Position.X + 2, enemy.Position.Y)));
        Assert.NotNull(simulation.World.GetEntity(enemy.Id)!.MoveTarget);

        Assert.False(simulation.TryStopCommander(enemy.Id, new PlayerId(1)));
        Assert.NotNull(simulation.World.GetEntity(enemy.Id)!.MoveTarget);
    }

    [Fact]
    public void TryIssueBastionOrder_RejectsForeignBastion()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var enemyBastion = simulation.World.Entities.Single(entity =>
            entity.OwnerId == new PlayerId(2) && entity.Kind == EntityKind.Bastion);
        var target = new TilePosition(enemyBastion.Position.X + 3, enemyBastion.Position.Y);

        Assert.False(simulation.TryIssueBastionOrder(
            enemyBastion.Id,
            new PlayerId(1),
            new BastionOrder(BastionOrderKind.AttackArea, target)));
        Assert.Equal(BastionOrderKind.Defend, simulation.World.GetEntity(enemyBastion.Id)!.Order.Kind);
    }

    [Fact]
    public void TrySetBastionTemplate_RejectsForeignBastion()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var enemyBastion = simulation.World.Entities.Single(entity =>
            entity.OwnerId == new PlayerId(2) && entity.Kind == EntityKind.Bastion);

        Assert.False(simulation.TrySetBastionTemplate(enemyBastion.Id, new PlayerId(1), EntityKind.BasicTank, 1));
        Assert.Empty(simulation.World.GetEntity(enemyBastion.Id)!.BastionTemplate);
    }

    [Fact]
    public void TrySetFactoryProduction_RejectsForeignFactory()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.TankFactory,
            NearBlue(simulation, 2, 6, playerId: 2),
            new PlayerId(2),
            out var factoryId));

        Assert.False(simulation.TrySetFactoryProduction(factoryId, new PlayerId(1), EntityKind.BasicTank));
        Assert.Null(simulation.World.GetEntity(factoryId)!.ProductionTargetKind);
    }

    [Fact]
    public void TrySetAssemblerRecipe_RejectsForeignAssembler()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.Assembler,
            NearBlue(simulation, 4, 2, playerId: 2),
            new PlayerId(2),
            out var assemblerId));

        Assert.False(simulation.TrySetAssemblerRecipe(assemblerId, new PlayerId(1), ItemRecipeId.IronGear));
        Assert.Null(simulation.World.GetEntity(assemblerId)!.SelectedItemRecipe);
    }

    [Fact]
    public void OwnedActor_CanControlOwnEntities()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var actor = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == actor);
        var bastion = simulation.World.Entities.Single(entity =>
            entity.OwnerId == actor && entity.Kind == EntityKind.Bastion);

        Assert.True(simulation.TryIssueMoveCommand(
            commander.Id,
            actor,
            new TilePosition(commander.Position.X, commander.Position.Y - 2)));
        Assert.True(simulation.TryStopCommander(commander.Id, actor));
        Assert.True(simulation.TryIssueBastionOrder(bastion.Id, actor, new BastionOrder(BastionOrderKind.Defend)));
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, actor, EntityKind.Scout, 1));
        Assert.True(simulation.TryPlaceGhostBuild(actor, EntityKind.TankFactory, NearBlue(simulation, 2, 6), out var factoryId));
        Assert.True(simulation.TryPlaceGhostBuild(actor, EntityKind.Assembler, NearBlue(simulation, 5, -2), out var assemblerId));
        AdvanceTicks(simulation, 90);
        Assert.True(simulation.TrySetFactoryProduction(factoryId, actor, EntityKind.BasicTank));
        Assert.True(simulation.TrySetAssemblerRecipe(assemblerId, actor, ItemRecipeId.IronGear));
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
