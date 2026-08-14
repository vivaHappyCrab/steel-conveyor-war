using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class ResearchEffectsTests
{
    [Fact]
    public void ImprovedConveyors_ReducesMoveTicksStat()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var player = new PlayerId(1);
        var before = simulation.ResolveStat(player, ResearchStatIds.ConveyorMoveTicks, MvpDefinitions.ConveyorMoveTicks);

        ForceComplete(simulation, player, TechnologyId.ImprovedConveyors);

        var after = simulation.ResolveStat(player, ResearchStatIds.ConveyorMoveTicks, MvpDefinitions.ConveyorMoveTicks);
        Assert.True(after < before);
    }

    [Fact]
    public void LightBot_AppliesGroundUnitAttackBonus()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var player = new PlayerId(1);
        var baseline = MvpDefinitions.GetStats(EntityKind.Commander).AttackDamage;
        var before = simulation.ResolveStat(player, ResearchStatIds.AttackDamage, baseline, EntityKind.Commander.ToString(), minValue: 0);

        ForceComplete(simulation, player, TechnologyId.LightBot);

        var after = simulation.ResolveStat(player, ResearchStatIds.AttackDamage, baseline, EntityKind.Commander.ToString(), minValue: 0);
        Assert.Equal(before + 1, after);

        var lightBotBaseline = MvpDefinitions.GetStats(EntityKind.LightBot).AttackDamage;
        var lightBotAfter = simulation.ResolveStat(
            player, ResearchStatIds.AttackDamage, lightBotBaseline, EntityKind.LightBot.ToString(), minValue: 0);
        Assert.Equal(lightBotBaseline + 1, lightBotAfter);

        var commanderBonus = simulation.ResearchCatalog.Technologies[TechnologyId.LightBot].Effects
            .OfType<AddModifierEffect>()
            .Single(effect => effect.Selector == EntityKind.Commander.ToString());
        Assert.Equal(ModifierOperation.Add, commanderBonus.Operation);
        Assert.Equal(
            GameplayTablesCatalog.Embedded.ResearchBonuses.AttackAddBasisPoints(EntityKind.Commander),
            commanderBonus.ValueBasisPoints);

        Assert.Equal(1, ResearchBonusTables.TenPercentOfAttack(5));
        Assert.Equal(1, ResearchBonusTables.TenPercentOfAttack(10));
        Assert.Equal(1, ResearchBonusTables.TenPercentOfAttack(14));
        Assert.Equal(2, ResearchBonusTables.TenPercentOfAttack(24));
        Assert.Equal(3, ResearchBonusTables.TenPercentOfAttack(32));

        var scoutBaseline = MvpDefinitions.GetStats(EntityKind.Scout).AttackDamage;
        var scoutAfter = simulation.ResolveStat(player, ResearchStatIds.AttackDamage, scoutBaseline, EntityKind.Scout.ToString(), minValue: 0);
        Assert.Equal(scoutBaseline, scoutAfter);
    }

    [Fact]
    public void ScoutResearch_AppliesGroundUnitArmorBonus()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var player = new PlayerId(1);
        var baseline = MvpDefinitions.GetStats(EntityKind.LightBot).Armor;
        var before = simulation.ResolveStat(player, ResearchStatIds.Armor, baseline, EntityKind.LightBot.ToString(), minValue: 0);

        ForceComplete(simulation, player, TechnologyId.Scout);

        var after = simulation.ResolveStat(player, ResearchStatIds.Armor, baseline, EntityKind.LightBot.ToString(), minValue: 0);
        Assert.Equal(before + GameplayTablesCatalog.Embedded.ResearchBonuses.GroundUnitArmorBonus, after);
        Assert.Equal(
            GameplayTablesCatalog.Embedded.ResearchBonuses.GroundUnitArmorAddBasisPoints,
            simulation.ResearchCatalog.Technologies[TechnologyId.Scout].Effects.OfType<AddModifierEffect>().Single().ValueBasisPoints);

        var scoutArmor = MvpDefinitions.GetStats(EntityKind.Scout).Armor;
        Assert.Equal(
            scoutArmor,
            simulation.ResolveStat(player, ResearchStatIds.Armor, scoutArmor, EntityKind.Scout.ToString(), minValue: 0));
    }

    [Fact]
    public void ModifierResolver_AppliesAddThenMultiply()
    {
        var modifiers = new[]
        {
            new AddModifierEffect(ResearchStatIds.VisionRadius, ModifierOperation.Add, 10_000),
            new AddModifierEffect(ResearchStatIds.VisionRadius, ModifierOperation.Multiply, 15_000)
        };

        var resolved = ModifierResolver.Resolve(4, modifiers, ResearchStatIds.VisionRadius, minValue: 0);
        Assert.Equal(7, resolved);
    }

    [Fact]
    public void FieldRepair_GrantsRepairCapability()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var player = new PlayerId(1);
        ForceComplete(simulation, player, new TechnologyId("technology.t1.field-repair"));
        Assert.True(CapabilityResolver.HasCapability(simulation.GetPlayer(player).Research, ResearchCapabilityIds.RepairOutOfCombat));
    }

    [Fact]
    public void ProductionReserve_IncreasesHubStorageStat()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var player = new PlayerId(1);
        var before = simulation.ResolveStat(player, ResearchStatIds.HubStorageStacks, MvpDefinitions.HubStorageStacks);
        ForceComplete(simulation, player, new TechnologyId("technology.t1.production-reserve"));
        var after = simulation.ResolveStat(player, ResearchStatIds.HubStorageStacks, MvpDefinitions.HubStorageStacks);
        Assert.True(after > before);
    }

    private static void ForceComplete(GameSimulation simulation, PlayerId playerId, TechnologyId technologyId)
    {
        Assert.True(simulation.TryForceCompleteResearch(playerId, technologyId, confirmExclusive: true));
        simulation.AdvanceTick();
        Assert.Contains(technologyId, simulation.GetPlayer(playerId).Research.CompletedTechnologies);
    }
}
