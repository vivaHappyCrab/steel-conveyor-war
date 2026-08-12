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
    public void Inventory_Mutators_AreNotPublic()
    {
        AssertAssemblyOnly(typeof(Inventory), "Add", typeof(ItemId), typeof(int));
        AssertAssemblyOnly(typeof(Inventory), "Clear");
        AssertAssemblyOnly(typeof(Inventory), "TryRemove", typeof(ItemId), typeof(int));
        AssertAssemblyOnly(typeof(Inventory), "TryRemoveAll", typeof(IReadOnlyDictionary<ItemId, int>));
        AssertAssemblyOnly(typeof(Inventory), "TryTakeFirst", typeof(Func<ItemId, bool>), typeof(ItemId).MakeByRefType());
        AssertAssemblyOnly(typeof(Inventory), "TryAddWithinStackLimit", typeof(ItemId), typeof(int), typeof(GameplayTablesCatalog));
        AssertAssemblyOnly(typeof(Inventory), "TryAddWithinTotalStackLimit", typeof(ItemId), typeof(int), typeof(int), typeof(GameplayTablesCatalog));
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

    // R05: root-level authoritative collections must not be downcastable to their backing List/Dictionary.
    [Fact]
    public void RootCollections_AreNotCastMutable()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);

        Assert.IsNotType<List<WorldEntity>>(simulation.World.Entities);
        Assert.IsNotType<List<PlayerState>>(simulation.Players);
        Assert.IsNotType<List<Commands.ISimulationCommand>>(simulation.PendingCommands);

        Assert.Throws<NotSupportedException>(() =>
            ((IList<WorldEntity>)simulation.World.Entities).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PlayerState>)simulation.Players).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Commands.ISimulationCommand>)simulation.PendingCommands).Clear());
    }

    // R05: static content tables must be immutable (previously public mutable HashSet / castable Dictionary).
    [Fact]
    public void ContentTables_AreNotCastMutable()
    {
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<EntityKind, int>)MvpDefinitions.PowerDemand)[EntityKind.Mine] = 0);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<ItemId, int>)MvpDefinitions.ItemStackSizes)[ItemId.IronPlate] = 0);
        Assert.IsNotType<HashSet<EntityKind>>(MvpDefinitions.UnitKinds);
        Assert.IsNotType<HashSet<EntityKind>>(MvpDefinitions.FactoryKinds);
    }

    private static bool HasPublicSetter(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var setter = property!.GetSetMethod(nonPublic: true);
        Assert.NotNull(setter);
        return setter!.IsPublic;
    }

    private static void AssertAssemblyOnly(Type type, string methodName, params Type[] parameterTypes)
    {
        var method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.False(method!.IsPublic);
        Assert.True(method.IsAssembly);
    }
}
