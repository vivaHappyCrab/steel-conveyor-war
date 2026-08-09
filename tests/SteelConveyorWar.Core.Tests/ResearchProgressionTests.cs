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
    public void ProfileA_OneOfTwoCategoriesUnlocksTier2()
    {
        var simulation = Create(ResearchProfileIds.MvpA);
        var player = new PlayerId(1);
        CompleteTech(simulation, player, TechnologyId.ImprovedConveyors);
        CompleteTech(simulation, player, new TechnologyId("technology.t1.distributed-energy"));
        CompleteTech(simulation, player, new TechnologyId("technology.t1.expedition-logistics"));

        Assert.Equal(ResearchTierIds.T2, simulation.GetPlayer(player).Research.CurrentTierId);
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, new TechnologyId("technology.t1.mass-production")));
    }

    [Fact]
    public void ProfileC_SupportsParallelCycleProjects()
    {
        var simulation = Create(ResearchProfileIds.MvpC);
        var player = new PlayerId(1);
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, new TechnologyId("technology.t1.automated-base")));
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, new TechnologyId("technology.t1.distant-expedition")));
        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, new TechnologyId("technology.t1.new-resource-mastery")));

        var snapshot = simulation.GetResearchSnapshot(player);
        var cycle = snapshot.Tracks.Single(track => track.Id == ResearchTrackIds.Cycle);
        Assert.Equal(3, cycle.ProjectWeights.Count);
        Assert.Equal(7_000, cycle.AllocationBasisPoints);
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
    public void ProfileC_ExclusiveDoctrineLocksOnStart()
    {
        var simulation = Create(ResearchProfileIds.MvpC);
        var player = new PlayerId(1);
        var swarm = new TechnologyId("technology.t1.doctrine.swarm");
        var observation = new TechnologyId("technology.t1.doctrine.observation");

        Assert.Equal(ResearchCommandResult.Ok, simulation.TrySelectResearch(player, swarm, confirmExclusive: true));
        Assert.Contains(observation, simulation.GetPlayer(player).Research.LockedTechnologies);
    }

    [Fact]
    public void ProfileHybrid_UsesAGatesAndCSchedule()
    {
        var simulation = Create(ResearchProfileIds.HybridAC);
        var player = new PlayerId(1);
        var snapshot = simulation.GetResearchSnapshot(player);
        Assert.Equal(2, snapshot.Tracks.Count);

        CompleteTech(simulation, player, TechnologyId.ImprovedConveyors);
        CompleteTech(simulation, player, new TechnologyId("technology.t1.distributed-energy"));
        CompleteTech(simulation, player, new TechnologyId("technology.t1.expedition-logistics"));
        Assert.Equal(ResearchTierIds.T2, simulation.GetPlayer(player).Research.CurrentTierId);
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
    public void AllocationSplitIsDeterministicAcrossSimulations()
    {
        var first = Create(ResearchProfileIds.MvpC);
        var second = Create(ResearchProfileIds.MvpC);
        var player = new PlayerId(1);
        Assert.Equal(ResearchCommandResult.Ok, first.TrySetTrackAllocation(player, new Dictionary<string, int>
        {
            [ResearchTrackIds.Cycle] = 10_000,
            [ResearchTrackIds.Tactical] = 0
        }));
        Assert.Equal(ResearchCommandResult.Ok, second.TrySetTrackAllocation(player, new Dictionary<string, int>
        {
            [ResearchTrackIds.Cycle] = 10_000,
            [ResearchTrackIds.Tactical] = 0
        }));

        Assert.Equal(
            first.GetResearchSnapshot(player).Tracks.Single(track => track.Id == ResearchTrackIds.Cycle).AllocationBasisPoints,
            second.GetResearchSnapshot(player).Tracks.Single(track => track.Id == ResearchTrackIds.Cycle).AllocationBasisPoints);
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
