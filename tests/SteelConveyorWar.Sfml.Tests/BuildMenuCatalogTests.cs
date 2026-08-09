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
}
