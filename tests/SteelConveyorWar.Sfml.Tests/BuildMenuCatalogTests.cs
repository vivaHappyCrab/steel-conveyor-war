using SteelConveyorWar.Core;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

public class BuildMenuCatalogTests
{
    [Fact]
    public void BuildMenuCatalog_ContainsHubOnQuickPage()
    {
        Assert.Contains(EntityKind.Hub, BuildMenuCatalog.BuildableKinds.Take(10));
        Assert.Equal(EntityKind.Hub, BuildMenuCatalog.BuildableKinds[9]);
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
        Assert.DoesNotContain(EntityKind.LightBot, unlocked);
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
