using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class ResearchProgressionTests
{
    [Fact]
    public void ProfileB_ThreeMandatoryUnlocksTier2()
    {
        var simulation = Create(ResearchProfileIds.MvpB);
        var player = new PlayerId(1);
        CompleteTech(simulation, player, TechnologyId.ProductionI);
        CompleteTech(simulation, player, TechnologyId.EnergyI);
        Assert.Equal(ResearchTierIds.T1, simulation.GetPlayer(player).Research.CurrentTierId);
        CompleteTech(simulation, player, TechnologyId.CommandI);

        var research = simulation.GetPlayer(player).Research;
        Assert.Equal(ResearchTierIds.T2, research.CurrentTierId);
        Assert.True(CapabilityResolver.HasCapability(research, ResearchCapabilityIds.Tier2Content));
        Assert.Contains(EntityKind.Refinery.ToString(), research.UnlockedEntityKinds);
    }

    [Fact]
    public void ProfileB_ExclusiveDoctrineLocksAlternativeOnComplete()
    {
        var simulation = Create(ResearchProfileIds.MvpB);
        var player = new PlayerId(1);
        var mobile = new TechnologyId("technology.t1.doctrine.mobile-groups");
        var fortified = new TechnologyId("technology.t1.doctrine.fortified-line");

        Assert.Equal(ResearchCommandResult.ExclusiveConfirmationRequired, simulation.TrySelectResearch(player, mobile));
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, mobile, confirmExclusive: true));
        CompleteTech(simulation, player, mobile);

        Assert.Contains(fortified, simulation.GetPlayer(player).Research.LockedTechnologies);
        Assert.Equal(ResearchCommandResult.Locked, simulation.TrySelectResearch(player, fortified, confirmExclusive: true));
    }

    [Fact]
    public void GroundAttackAndArmor_RequireCommandI()
    {
        var simulation = Create(ResearchProfileIds.MvpB);
        var player = new PlayerId(1);

        Assert.Equal(ResearchCommandResult.NotAvailable, simulation.TrySelectResearch(player, TechnologyId.GroundUnitAttack));
        Assert.Equal(ResearchCommandResult.NotAvailable, simulation.TrySelectResearch(player, TechnologyId.GroundUnitArmor));

        CompleteTech(simulation, player, TechnologyId.CommandI);

        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, TechnologyId.GroundUnitAttack));
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, TechnologyId.GroundUnitArmor));
    }

    [Fact]
    public void ProgressIsPreservedWhenSwitchingResearch()
    {
        var simulation = Create(ResearchProfileIds.MvpB);
        var player = new PlayerId(1);
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, TechnologyId.ProductionI));

        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.Laboratory, new TilePosition(5, simulation.World.Size.Height / 2 - 2), out var labId));
        for (var i = 0; i < 30; i++)
        {
            simulation.AdvanceTick();
        }

        simulation.AddItemToEntity(labId, ItemId.SciencePackT1, 1);
        for (var i = 0; i < ResearchSystem.LabCycleTicks; i++)
        {
            simulation.AdvanceTick();
        }

        var progress = simulation.GetPlayer(player).Research.ProgressWorkUnits.GetValueOrDefault(TechnologyId.ProductionI);
        Assert.True(progress > 0);
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, TechnologyId.EnergyI));
        Assert.Equal(progress, simulation.GetPlayer(player).Research.ProgressWorkUnits[TechnologyId.ProductionI]);
    }

    [Fact]
    public void TryCancelResearch_ClearsActiveTargetButKeepsProgress()
    {
        var simulation = Create(ResearchProfileIds.MvpB);
        var player = new PlayerId(1);
        Assert.True(simulation.TryStartResearch(player, TechnologyId.ProductionI));

        Assert.True(simulation.TryPlaceGhostBuild(player, EntityKind.Laboratory, new TilePosition(5, simulation.World.Size.Height / 2 - 2), out var labId));
        for (var i = 0; i < 30; i++)
        {
            simulation.AdvanceTick();
        }

        simulation.AddItemToEntity(labId, ItemId.SciencePackT1, 1);
        for (var i = 0; i < ResearchSystem.LabCycleTicks; i++)
        {
            simulation.AdvanceTick();
        }

        var progress = simulation.GetPlayer(player).Research.ProgressWorkUnits.GetValueOrDefault(TechnologyId.ProductionI);
        Assert.True(progress > 0);
        Assert.True(simulation.TryCancelResearch(player, TechnologyId.ProductionI));
        Assert.Equal(progress, simulation.GetPlayer(player).Research.ProgressWorkUnits[TechnologyId.ProductionI]);
        Assert.Null(simulation.GetResearchSnapshot(player).Tracks.Single().ActiveSerialTarget);
    }

    private static GameSimulation Create(string profileId)
    {
        return GameSimulation.CreateNewGame(new GameCreationOptions(42, profileId, MvpResearchCatalog.CreateEmbedded()));
    }

    private static void CompleteTech(GameSimulation simulation, PlayerId playerId, TechnologyId technologyId)
    {
        Assert.True(simulation.TryForceCompleteResearch(playerId, technologyId, confirmExclusive: true));
        simulation.AdvanceTick();
        Assert.Contains(technologyId, simulation.GetPlayer(playerId).Research.CompletedTechnologies);
    }
}
