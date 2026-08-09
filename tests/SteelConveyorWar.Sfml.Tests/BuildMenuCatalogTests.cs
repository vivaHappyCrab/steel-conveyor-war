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
}
