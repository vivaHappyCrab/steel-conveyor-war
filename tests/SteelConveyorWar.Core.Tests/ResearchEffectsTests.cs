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
    public void LightBot_UnlocksEntityContent()
    {
        var simulation = GameSimulation.CreateNewGame(new GameCreationOptions(42, ResearchProfileIds.MvpB, MvpResearchCatalog.CreateEmbedded()));
        var player = new PlayerId(1);
        ForceComplete(simulation, player, TechnologyId.LightBot);

        Assert.Contains(EntityKind.LightBot.ToString(), simulation.GetPlayer(player).Research.UnlockedEntityKinds);
        Assert.Contains(TechnologyId.LightBot, simulation.GetPlayer(player).ResearchedTechnologies);
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
