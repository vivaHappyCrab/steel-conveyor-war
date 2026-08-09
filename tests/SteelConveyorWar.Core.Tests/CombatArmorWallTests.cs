namespace SteelConveyorWar.Core.Tests;

public sealed class CombatArmorWallTests
{
    [Theory]
    [InlineData(10, 2, 10_000, 8)]
    [InlineData(10, 15, 10_000, 1)]
    [InlineData(10, 0, 7_000, 7)]
    [InlineData(24, 4, 13_000, 26)]
    [InlineData(5, 0, 0, 0)]
    public void FormulaC_UsesArmorFloorAndResistanceBasisPoints(
        int attackDamage,
        int armor,
        int resistanceBasisPoints,
        int expected)
    {
        Assert.Equal(expected, CombatDamage.ComputeFinalDamage(attackDamage, armor, resistanceBasisPoints));
    }

    [Fact]
    public void ProcessCombat_AppliesFormulaC_AgainstArmoredCommander()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(PlaceAdjacent(simulation, attacker, defender.Position));

        // Commander 10 dmg, armor 2, G2G×Unit 1.0 → 8
        Assert.Equal(8, simulation.ComputeCombatDamageForTests(attacker.Id, defender.Id));
        var healthBefore = defender.Health;
        simulation.AdvanceTick();
        Assert.Equal(healthBefore - 8, defender.Health);
    }

    [Fact]
    public void ProcessCombat_WallBlocksGroundToGround_ToAlliedGroundUnit()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));

        Assert.True(simulation.TryTeleportEntityForTests(attacker.Id, new TilePosition(20, 20)));
        Assert.True(simulation.TryTeleportEntityForTests(defender.Id, new TilePosition(22, 20)));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, new TilePosition(21, 20), new PlayerId(1), out _));

        var healthBefore = defender.Health;
        simulation.AdvanceTick();

        Assert.Equal(healthBefore, defender.Health);
        Assert.True(attacker.AttackCooldownRemaining > 0);
    }

    [Fact]
    public void ProcessCombat_BallisticIgnoresWallCover()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        // Keep the enemy commander far so the cannon is the only in-range attacker.
        var enemyCommander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.TryTeleportEntityForTests(enemyCommander.Id, new TilePosition(2, 2)));

        Assert.True(simulation.TryTeleportEntityForTests(defender.Id, new TilePosition(40, 40)));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, new TilePosition(39, 40), new PlayerId(1), out _));
        Assert.True(simulation.TrySpawnEntityForTests(
            EntityKind.CannonTurret,
            new TilePosition(38, 40),
            new PlayerId(2),
            out var cannonId));

        var cannon = simulation.World.GetEntity(cannonId)!;
        Assert.Equal(ProjectileKind.Ballistic, MvpDefinitions.GetStats(cannon.Kind).ProjectileKind);
        var expected = simulation.ComputeCombatDamageForTests(cannonId, defender.Id);
        Assert.True(expected > 0);
        var healthBefore = defender.Health;

        simulation.AdvanceTick();

        Assert.Equal(healthBefore - expected, defender.Health);
    }

    [Fact]
    public void ProcessCombat_WallAsTarget_TakesDamageNormally()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var enemy = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(simulation.TryTeleportEntityForTests(enemy.Id, new TilePosition(2, 2)));

        Assert.True(simulation.TryTeleportEntityForTests(attacker.Id, new TilePosition(30, 30)));
        Assert.True(simulation.TrySpawnEntityForTests(EntityKind.Wall, new TilePosition(31, 30), new PlayerId(2), out var wallId));

        var expected = simulation.ComputeCombatDamageForTests(attacker.Id, wallId);
        Assert.True(expected > 0);
        var wall = simulation.World.GetEntity(wallId)!;
        var healthBefore = wall.Health;
        simulation.AdvanceTick();
        Assert.Equal(healthBefore - expected, wall.Health);
    }

    [Fact]
    public void ResearchModifiers_ScaleCombatStatsInProcessCombat()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var attacker = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(1));
        var defender = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == new PlayerId(2));
        Assert.True(PlaceAdjacent(simulation, attacker, defender.Position));

        var baselineDamage = simulation.ComputeCombatDamageForTests(attacker.Id, defender.Id);
        simulation.ApplyResearchModifierForTests(
            new PlayerId(1),
            new AddModifierEffect(ResearchStatIds.AttackDamage, ModifierOperation.Multiply, 15_000));
        simulation.ApplyResearchModifierForTests(
            new PlayerId(1),
            new AddModifierEffect(ResearchStatIds.AttackCooldownTicks, ModifierOperation.Multiply, 5_000));
        simulation.ApplyResearchModifierForTests(
            new PlayerId(2),
            new AddModifierEffect(ResearchStatIds.Armor, ModifierOperation.Add, 20_000));
        simulation.ApplyResearchModifierForTests(
            new PlayerId(2),
            new AddModifierEffect(ResearchStatIds.MaxHealth, ModifierOperation.Add, 50_000));

        Assert.True(defender.MaxHealth > MvpDefinitions.GetStats(EntityKind.Commander).MaxHealth);
        var scaledDamage = simulation.ComputeCombatDamageForTests(attacker.Id, defender.Id);
        Assert.NotEqual(baselineDamage, scaledDamage);

        var healthBefore = defender.Health;
        var expectedCooldown = simulation.ResolveStat(
            new PlayerId(1),
            ResearchStatIds.AttackCooldownTicks,
            MvpDefinitions.GetStats(EntityKind.Commander).AttackCooldownTicks,
            EntityKind.Commander.ToString());
        simulation.AdvanceTick();

        Assert.Equal(healthBefore - scaledDamage, defender.Health);
        Assert.Equal(expectedCooldown, attacker.AttackCooldownRemaining);
        Assert.True(expectedCooldown < MvpDefinitions.GetStats(EntityKind.Commander).AttackCooldownTicks);
    }

    [Fact]
    public void GetStats_CombatUnitsHaveProjectileKindsAndArmor()
    {
        Assert.Equal(ProjectileKind.GroundToGround, MvpDefinitions.GetStats(EntityKind.MachineGunTurret).ProjectileKind);
        Assert.Equal(ProjectileKind.Ballistic, MvpDefinitions.GetStats(EntityKind.CannonTurret).ProjectileKind);
        Assert.Equal(ProjectileKind.AirToGround, MvpDefinitions.GetStats(EntityKind.AntiAirTurret).ProjectileKind);
        Assert.Equal(ProjectileKind.Ballistic, MvpDefinitions.GetStats(EntityKind.RocketLauncher).ProjectileKind);
        Assert.True(MvpDefinitions.GetStats(EntityKind.MediumTank).SplashRadius > 0);
        Assert.True(MvpDefinitions.GetStats(EntityKind.SteelWall).Armor > MvpDefinitions.GetStats(EntityKind.Wall).Armor);
        Assert.True(MvpDefinitions.GetStats(EntityKind.BasicTank).Armor > 0);
    }

    [Fact]
    public void TryForceCompleteResearch_ConcreteWalls_SyncsExistingEntityMaxHealth()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == playerId);
        var baseline = MvpDefinitions.GetStats(EntityKind.Commander).MaxHealth;
        Assert.Equal(baseline, commander.MaxHealth);

        Assert.True(simulation.TryForceCompleteResearch(playerId, TechnologyId.ConcreteWalls, confirmExclusive: true));

        var expected = simulation.ResolveStat(
            playerId,
            ResearchStatIds.MaxHealth,
            baseline,
            EntityKind.Commander.ToString(),
            minValue: 1);
        Assert.True(expected > baseline);
        Assert.Equal(expected, commander.MaxHealth);
        Assert.Equal(expected, commander.Health);
    }

    [Fact]
    public void CompleteGhostBuild_AppliesResearchedMaxHealth()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var playerId = new PlayerId(1);
        Assert.True(simulation.TryForceCompleteResearch(playerId, TechnologyId.ConcreteWalls, confirmExclusive: true));

        var wallBaseline = MvpDefinitions.GetStats(EntityKind.Wall).MaxHealth;
        var expected = simulation.ResolveStat(
            playerId,
            ResearchStatIds.MaxHealth,
            wallBaseline,
            EntityKind.Wall.ToString(),
            minValue: 1);
        Assert.True(expected > wallBaseline);

        var commander = simulation.World.Entities.Single(entity => entity.Kind == EntityKind.Commander && entity.OwnerId == playerId);
        var wallTile = new TilePosition(commander.Position.X + 2, commander.Position.Y);
        Assert.True(simulation.TryPlaceGhostBuild(playerId, EntityKind.Wall, wallTile, out var ghostId));
        for (var i = 0; i < 30; i++)
        {
            simulation.AdvanceTick();
        }

        var wall = simulation.World.GetEntity(ghostId)!;
        Assert.Equal(EntityKind.Wall, wall.Kind);
        Assert.Equal(expected, wall.MaxHealth);
        Assert.Equal(expected, wall.Health);
    }

    private static bool PlaceAdjacent(GameSimulation simulation, WorldEntity entity, TilePosition near)
    {
        var candidates = new[]
        {
            new TilePosition(near.X - 1, near.Y),
            new TilePosition(near.X + 1, near.Y),
            new TilePosition(near.X, near.Y - 1),
            new TilePosition(near.X, near.Y + 1)
        };

        var position = candidates.First(candidate => candidate.X >= 0 && candidate.Y >= 0);
        return simulation.TryTeleportEntityForTests(entity.Id, position);
    }
}
