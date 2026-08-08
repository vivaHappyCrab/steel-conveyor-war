using System.Reflection;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Core.Tests;

public sealed class EncapsulationTests
{
    [Fact]
    public void WorldEntity_HealthAndDirection_SettersAreNotPublic()
    {
        Assert.False(HasPublicSetter(typeof(WorldEntity), nameof(WorldEntity.Health)));
        Assert.False(HasPublicSetter(typeof(WorldEntity), nameof(WorldEntity.Direction)));
        Assert.False(HasPublicSetter(typeof(WorldEntity), nameof(WorldEntity.ProductionTargetKind)));
    }

    [Fact]
    public void Inventory_Add_IsNotPublic()
    {
        var add = typeof(Inventory).GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ItemId), typeof(int)],
            modifiers: null);

        Assert.NotNull(add);
        Assert.False(add!.IsPublic);
        Assert.True(add.IsAssembly);
    }

    [Fact]
    public void PlayerResearchState_EnsureTracks_IsNotPublic()
    {
        var ensureTracks = typeof(PlayerResearchState).GetMethod(
            "EnsureTracks",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(ensureTracks);
        Assert.False(ensureTracks!.IsPublic);
        Assert.True(ensureTracks.IsAssembly);
    }

    [Fact]
    public void AuthoritativeCollections_AreWrappedAgainstCastMutation()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        var player = simulation.GetPlayer(new PlayerId(1));
        var factory = simulation.World.Entities.First(entity => entity.Kind == EntityKind.Commander);

        Assert.ThrowsAny<Exception>(() => ((ICollection<TechnologyId>)player.Research.CompletedTechnologies).Add(TechnologyId.LightBot));
        Assert.ThrowsAny<Exception>(() => ((IDictionary<ItemId, int>)player.Inventory.Items)[ItemId.IronPlate] = 99);
        Assert.ThrowsAny<Exception>(() => ((IList<ConveyorItem>)factory.ConveyorItems).Clear());
        Assert.ThrowsAny<Exception>(() => ((IDictionary<EntityKind, int>)factory.BastionTemplate)[EntityKind.BasicTank] = 1);
    }

    private static bool HasPublicSetter(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var setter = property!.GetSetMethod(nonPublic: true);
        Assert.NotNull(setter);
        return setter!.IsPublic;
    }
}
