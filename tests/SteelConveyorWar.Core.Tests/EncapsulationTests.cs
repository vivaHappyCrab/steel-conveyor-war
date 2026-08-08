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

    private static bool HasPublicSetter(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var setter = property!.GetSetMethod(nonPublic: true);
        Assert.NotNull(setter);
        return setter!.IsPublic;
    }
}
