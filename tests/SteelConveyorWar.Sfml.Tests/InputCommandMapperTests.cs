using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

/// <summary>
/// R21: InputCommandMapper intent → command kind/payload without an SFML window.
/// H05: build-menu hotkeys use injected match-scoped kinds.
/// </summary>
public sealed class InputCommandMapperTests
{
    private static InputCommandMapper CreateMapper(
        PlayerId localPlayer,
        SessionState state,
        SfmlCommandGateway gateway,
        GameSimulation simulation,
        IReadOnlyList<EntityKind>? buildMenuKinds = null)
    {
        var menu = buildMenuKinds ?? BuildMenuCatalog.ComposeFrom(simulation.BuildCostCatalog);
        return new InputCommandMapper(localPlayer, state, gateway, menu);
    }

    [Fact]
    public void MapMoveIntent_BuildsIssueMoveCommandPayload()
    {
        var actor = new PlayerId(1);
        var target = new TilePosition(12, 8);
        var command = InputCommandMapper.MapMoveIntent(actor, entityId: 5, target);

        Assert.Equal(SimulationCommandKind.IssueMove, command.Kind);
        Assert.Equal(actor, command.Actor);
        Assert.Equal(5, command.EntityId);
        Assert.Equal(target, command.Target);
        Assert.Equal(0, command.Tick);
    }

    [Fact]
    public void MapStopCommander_BuildsStopCommanderCommandPayload()
    {
        var actor = new PlayerId(2);
        var command = InputCommandMapper.MapStopCommander(actor, commanderId: 9);

        Assert.Equal(SimulationCommandKind.StopCommander, command.Kind);
        Assert.Equal(actor, command.Actor);
        Assert.Equal(9, command.CommanderId);
    }

    [Fact]
    public void MapRotate_BuildsRotateEntityCommandPayload()
    {
        var actor = new PlayerId(1);
        var command = InputCommandMapper.MapRotate(actor, entityId: 3, clockwise: false);

        Assert.Equal(SimulationCommandKind.RotateEntity, command.Kind);
        Assert.Equal(3, command.EntityId);
        Assert.False(command.Clockwise);
    }

    [Fact]
    public void MapSelectResearch_BuildsSelectResearchCommandPayload()
    {
        var actor = new PlayerId(1);
        var command = InputCommandMapper.MapSelectResearch(
            actor,
            TechnologyId.ProductionI,
            confirmExclusive: true,
            preferredTrackId: "track.production");

        Assert.Equal(SimulationCommandKind.SelectResearch, command.Kind);
        Assert.Equal(TechnologyId.ProductionI, command.Technology);
        Assert.True(command.ConfirmExclusive);
        Assert.Equal("track.production", command.PreferredTrackId);
    }

    [Fact]
    public void HandleKeyPressed_StopCommander_EnqueuesStopCommand()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 11);
        var localPlayer = new PlayerId(1);
        var commander = simulation.World.Entities.First(
            e => e.OwnerId == localPlayer && e.Kind == EntityKind.Commander);
        var sink = new DeferredCommandSink(simulation);
        var gateway = new SfmlCommandGateway(sink);
        var state = new SessionState(commander.Id);
        var mapper = CreateMapper(localPlayer, state, gateway, simulation);

        var result = mapper.HandleKeyPressed(
            simulation,
            "S",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false));

        Assert.True(result.Consumed);
        var stop = Assert.IsType<StopCommanderCommand>(Assert.Single(gateway.CommandLog));
        Assert.Equal(commander.Id, stop.CommanderId);
        Assert.Equal(localPlayer, stop.Actor);
    }

    [Fact]
    public void HandleWorldClick_RightClick_EnqueuesIssueMove()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 11);
        var localPlayer = new PlayerId(1);
        var commander = simulation.World.Entities.First(
            e => e.OwnerId == localPlayer && e.Kind == EntityKind.Commander);
        var sink = new DeferredCommandSink(simulation);
        var gateway = new SfmlCommandGateway(sink);
        var state = new SessionState(commander.Id);
        var mapper = CreateMapper(localPlayer, state, gateway, simulation);
        var target = new TilePosition(commander.Position.X + 2, commander.Position.Y);

        var result = mapper.HandleWorldClick(
            simulation,
            "Right",
            target,
            new InputModifiers(Shift: false, Control: false),
            confirmPatrolOnRightOrMinimap: true);

        Assert.True(result.Consumed);
        var move = Assert.IsType<IssueMoveCommand>(Assert.Single(gateway.CommandLog));
        Assert.Equal(commander.Id, move.EntityId);
        Assert.Equal(target, move.Target);
        Assert.Equal(localPlayer, move.Actor);
    }

    [Fact]
    public void HandleKeyPressed_RotateSelected_EnqueuesRotateCommand()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 11);
        var localPlayer = new PlayerId(1);
        var commander = simulation.World.Entities.First(
            e => e.OwnerId == localPlayer && e.Kind == EntityKind.Commander);
        var sink = new DeferredCommandSink(simulation);
        var gateway = new SfmlCommandGateway(sink);
        var state = new SessionState(commander.Id);
        var mapper = CreateMapper(localPlayer, state, gateway, simulation);

        var result = mapper.HandleKeyPressed(
            simulation,
            "R",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false));

        Assert.True(result.Consumed);
        var rotate = Assert.IsType<RotateEntityCommand>(Assert.Single(gateway.CommandLog));
        Assert.Equal(commander.Id, rotate.EntityId);
        Assert.True(rotate.Clockwise);
    }

    [Fact]
    public void HandleKeyPressed_ToggleBuildMenu_DoesNotEnqueueCommand()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 11);
        var localPlayer = new PlayerId(1);
        var commander = simulation.World.Entities.First(
            e => e.OwnerId == localPlayer && e.Kind == EntityKind.Commander);
        var sink = new DeferredCommandSink(simulation);
        var gateway = new SfmlCommandGateway(sink);
        var state = new SessionState(commander.Id);
        var mapper = CreateMapper(localPlayer, state, gateway, simulation);

        var result = mapper.HandleKeyPressed(
            simulation,
            "B",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false));

        Assert.True(result.Consumed);
        Assert.True(state.IsBuildMenuOpen);
        Assert.Equal(mapper.BuildMenuKinds[0], state.PendingBuildKind);
        Assert.Empty(gateway.CommandLog);
    }

    [Fact]
    public void HandleKeyPressed_NumberShortcut_UsesInjectedMenuKinds()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 11);
        var localPlayer = new PlayerId(1);
        var commander = simulation.World.Entities.First(
            e => e.OwnerId == localPlayer && e.Kind == EntityKind.Commander);
        var sink = new DeferredCommandSink(simulation);
        var gateway = new SfmlCommandGateway(sink);
        var state = new SessionState(commander.Id);
        // Custom order: Hub is index 0 (key "1"), not historical Mine.
        IReadOnlyList<EntityKind> customMenu = [EntityKind.Hub, EntityKind.Mine, EntityKind.Conveyor];
        var mapper = CreateMapper(localPlayer, state, gateway, simulation, customMenu);

        Assert.True(mapper.HandleKeyPressed(
            simulation,
            "B",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false)).Consumed);
        Assert.Equal(EntityKind.Hub, state.PendingBuildKind);

        Assert.True(mapper.HandleKeyPressed(
            simulation,
            "Num2",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false)).Consumed);
        Assert.Equal(EntityKind.Mine, state.PendingBuildKind);

        // Index beyond injected menu length must not change selection.
        Assert.True(mapper.HandleKeyPressed(
            simulation,
            "Num4",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false)).Consumed);
        Assert.Equal(EntityKind.Mine, state.PendingBuildKind);
        Assert.Empty(gateway.CommandLog);
    }

    [Fact]
    public void HandleKeyPressed_NumberShortcut_OmitsKindsAbsentFromCatalog()
    {
        var costs = new Dictionary<EntityKind, IReadOnlyDictionary<ItemId, int>>
        {
            [EntityKind.Conveyor] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 1 },
            [EntityKind.Inserter] = new Dictionary<ItemId, int> { [ItemId.IronPlate] = 1 },
        };
        var customCatalog = new BuildCostCatalog(
            1,
            costs,
            costs.Keys.ToDictionary(k => k, _ => 30),
            new Dictionary<EntityKind, TechnologyId>());
        var simulation = GameSimulation.CreateNewGame(
            GameCreationOptions.Default with { RandomSeed = 11, BuildCosts = customCatalog });
        var menu = BuildMenuCatalog.ComposeFrom(simulation.BuildCostCatalog);
        Assert.DoesNotContain(EntityKind.Hub, menu);
        Assert.Equal([EntityKind.Conveyor, EntityKind.Inserter], menu);

        var localPlayer = new PlayerId(1);
        var commander = simulation.World.Entities.First(
            e => e.OwnerId == localPlayer && e.Kind == EntityKind.Commander);
        var sink = new DeferredCommandSink(simulation);
        var gateway = new SfmlCommandGateway(sink);
        var state = new SessionState(commander.Id);
        var mapper = CreateMapper(localPlayer, state, gateway, simulation, menu);

        mapper.HandleKeyPressed(
            simulation,
            "B",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false));
        Assert.Equal(EntityKind.Conveyor, state.PendingBuildKind);

        mapper.HandleKeyPressed(
            simulation,
            "Num2",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false));
        Assert.Equal(EntityKind.Inserter, state.PendingBuildKind);
    }
}
