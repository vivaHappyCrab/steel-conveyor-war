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
}
