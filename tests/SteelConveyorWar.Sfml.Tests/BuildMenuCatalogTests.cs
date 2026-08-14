using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

public class BuildMenuCatalogTests
{
    private static EntityKind[] DefaultMenu() =>
        BuildMenuCatalog.ComposeFrom(MvpBuildCostCatalog.Embedded);

    [Fact]
    public void ComposeFrom_ContainsHubOnQuickPage()
    {
        var menu = DefaultMenu();
        Assert.Contains(EntityKind.Hub, menu.Take(10));
        Assert.Equal(EntityKind.Hub, menu[9]);
    }

    [Fact]
    public void BuildBarModel_AffordLabel_CapsAtNinePlus()
    {
        Assert.Equal("0", BuildBarModel.AffordLabel(0));
        Assert.Equal("9", BuildBarModel.AffordLabel(9));
        Assert.Equal("9+", BuildBarModel.AffordLabel(10));
    }

    [Fact]
    public void BuildBarModel_ShortcutBadge_MapsNumKeys()
    {
        Assert.Equal("1", BuildBarModel.ShortcutBadge(0));
        Assert.Equal("0", BuildBarModel.ShortcutBadge(9));
        Assert.Null(BuildBarModel.ShortcutBadge(10));
    }

    [Fact]
    public void BuildBarModel_Glyph_IsStablePlaceholder()
    {
        Assert.Equal("=", BuildBarModel.Glyph(EntityKind.Conveyor));
        Assert.Equal("H", BuildBarModel.Glyph(EntityKind.Hub));
    }

    // R30/H05: composition is derived from the provided catalog, not a hardcoded list.
    [Fact]
    public void ComposeFrom_ContainsEveryCatalogEntity()
    {
        var catalogKinds = MvpBuildCostCatalog.Embedded.Costs.Keys.ToHashSet();
        var menuKinds = DefaultMenu().ToHashSet();

        Assert.Equal(catalogKinds, menuKinds);
    }

    [Theory]
    [InlineData(EntityKind.UndergroundConveyor)]
    [InlineData(EntityKind.SteelWall)]
    [InlineData(EntityKind.CannonTurret)]
    [InlineData(EntityKind.AntiAirTurret)]
    public void ComposeFrom_IncludesPreviouslyMissingEntities(EntityKind kind)
    {
        Assert.Contains(kind, DefaultMenu());
    }

    [Fact]
    public void ComposeFrom_HasNoDuplicates()
    {
        var menu = DefaultMenu();
        Assert.Equal(menu.Length, menu.Distinct().Count());
    }

    [Fact]
    public void EveryComposeFromKind_HasAGlyph()
    {
        foreach (var kind in DefaultMenu())
        {
            Assert.NotEqual("?", BuildBarModel.Glyph(kind));
        }
    }

    [Fact]
    public void ComposeFrom_IsDeterministic()
    {
        var first = BuildMenuCatalog.ComposeFrom(MvpBuildCostCatalog.Embedded);
        var second = BuildMenuCatalog.ComposeFrom(MvpBuildCostCatalog.Embedded);

        Assert.Equal(first, second);
    }

    // H05: custom catalog divergence — extra kind appears; removed default kind is absent.
    [Fact]
    public void ComposeFrom_UsesProvidedCatalogNotEmbedded()
    {
        var costs = new Dictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>
        {
            [EntityKind.Mine] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 1 },
            [EntityKind.Conveyor] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 1 },
            // Hub is in Embedded PreferredOrder but intentionally omitted here.
        };
        var ticks = costs.Keys.ToDictionary(k => k, _ => 30);
        var custom = new BuildCostCatalog(1, costs, ticks, new Dictionary<EntityKind, TechnologyId>());

        var menu = BuildMenuCatalog.ComposeFrom(custom);

        Assert.Equal([EntityKind.Mine, EntityKind.Conveyor], menu);
        Assert.DoesNotContain(EntityKind.Hub, menu);
        Assert.DoesNotContain(EntityKind.Assembler, menu);
        Assert.NotEqual(DefaultMenu().Length, menu.Length);
    }

    [Fact]
    public void ComposeFrom_SessionCatalog_MatchesSimulationNotEmbeddedStatic()
    {
        var costs = MvpBuildCostCatalog.Embedded.Costs.ToDictionary(
            pair => pair.Key,
            pair => pair.Value);
        costs.Remove(EntityKind.AntiAirTurret);
        costs[EntityKind.Scout] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 5 };
        var ticks = costs.Keys.ToDictionary(k => k, _ => 30);
        var custom = new BuildCostCatalog(
            1,
            costs,
            ticks,
            MvpBuildCostCatalog.Embedded.Requirements);

        var simulation = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 7, BuildCosts = custom });
        var menu = BuildMenuCatalog.ComposeFrom(simulation.BuildCostCatalog);

        Assert.Contains(EntityKind.Scout, menu);
        Assert.DoesNotContain(EntityKind.AntiAirTurret, menu);
        Assert.Equal(simulation.BuildCostCatalog.Costs.Keys.ToHashSet(), menu.ToHashSet());
#pragma warning disable CS0618 // obsolete Embedded helper — assert production path diverges
        Assert.NotEqual(BuildMenuCatalog.BuildableKinds.ToHashSet(), menu.ToHashSet());
#pragma warning restore CS0618
    }

    [Fact]
    public void BuildBarModel_TryCopyFromWorldEntity_RequiresCatalogMembership()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 3);
        var hub = simulation.World.Entities.First(e => e.Kind == EntityKind.Hub);
        var empty = BuildCostCatalog.Empty;

        Assert.False(BuildBarModel.TryCopyFromWorldEntity(hub, empty, out _, out _, out _, out _));
        Assert.True(BuildBarModel.TryCopyFromWorldEntity(
            hub,
            simulation.BuildCostCatalog,
            out var kind,
            out _,
            out _,
            out _));
        Assert.Equal(EntityKind.Hub, kind);
    }

    [Fact]
    public void BastionOrderBarModel_AsdfHotkeysAndGlyphs()
    {
        Assert.True(BastionOrderBarModel.TryGetCommandFromKey("A", out var attack));
        Assert.Equal(BastionOrderCommand.Attack, attack);
        Assert.True(BastionOrderBarModel.TryGetCommandFromKey("S", out var scout));
        Assert.Equal(BastionOrderCommand.Scout, scout);
        Assert.True(BastionOrderBarModel.TryGetCommandFromKey("D", out var defend));
        Assert.Equal(BastionOrderCommand.ActiveDefense, defend);
        Assert.True(BastionOrderBarModel.TryGetCommandFromKey("F", out var patrol));
        Assert.Equal(BastionOrderCommand.Patrol, patrol);

        Assert.Equal("A", BastionOrderBarModel.Glyph(BastionOrderCommand.Attack));
        Assert.Equal("S", BastionOrderBarModel.Glyph(BastionOrderCommand.Scout));
        Assert.Equal("D", BastionOrderBarModel.Glyph(BastionOrderCommand.ActiveDefense));
        Assert.Equal("F", BastionOrderBarModel.Glyph(BastionOrderCommand.Patrol));
        Assert.Equal(
            new[]
            {
                BastionOrderCommand.Attack,
                BastionOrderCommand.Scout,
                BastionOrderCommand.ActiveDefense,
                BastionOrderCommand.Patrol
            },
            BastionOrderBarModel.Commands);
    }

    [Fact]
    public void BastionCompositionPanelModel_GlyphAndCountLabel()
    {
        Assert.Equal("T", BastionCompositionPanelModel.Glyph(EntityKind.BasicTank));
        Assert.Equal("L", BastionCompositionPanelModel.Glyph(EntityKind.LightBot));
        Assert.Equal("2/5", BastionCompositionPanelModel.CountLabel(2, 5));
    }

    [Fact]
    public void BastionCompositionPanelModel_UnlockedUnitKinds_StartsWithBaselineTank()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var unlocked = BastionCompositionPanelModel.UnlockedUnitKinds(simulation, new PlayerId(1));
        Assert.Contains(EntityKind.BasicTank, unlocked);
        Assert.Contains(EntityKind.LightBot, unlocked);
        Assert.Contains(EntityKind.Scout, unlocked);
    }

    [Fact]
    public void BastionCompositionPanelModel_BuildSlots_ExposesLiveAndTemplateMax()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var bastion = simulation.World.Entities.Single(entity => entity.OwnerId == new PlayerId(1) && entity.Kind == EntityKind.Bastion);
        Assert.True(simulation.TrySetBastionTemplate(bastion.Id, new PlayerId(1), EntityKind.BasicTank, 3));
        var slots = BastionCompositionPanelModel.BuildSlots(simulation, bastion);
        var tank = Assert.Single(slots, slot => slot.UnitKind == EntityKind.BasicTank);
        Assert.Equal(0, tank.LiveCount);
        Assert.Equal(3, tank.TemplateMax);
        Assert.Equal("T", tank.Glyph);
    }
}
