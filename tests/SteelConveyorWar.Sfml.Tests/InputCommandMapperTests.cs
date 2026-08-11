using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;
using SteelConveyorWar.Sfml;

namespace SteelConveyorWar.Sfml.Tests;

/// <summary>
/// R21: InputCommandMapper intent → command kind/payload without an SFML window.
/// </summary>
public sealed class InputCommandMapperTests
{
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
        var mapper = new InputCommandMapper(localPlayer, state, gateway);

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
        var mapper = new InputCommandMapper(localPlayer, state, gateway);
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
        var mapper = new InputCommandMapper(localPlayer, state, gateway);

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
        var mapper = new InputCommandMapper(localPlayer, state, gateway);

        var result = mapper.HandleKeyPressed(
            simulation,
            "B",
            new InputModifiers(Shift: false, Control: false),
            new SessionHoverContext(null, null, false));

        Assert.True(result.Consumed);
        Assert.True(state.IsBuildMenuOpen);
        Assert.Empty(gateway.CommandLog);
    }
}
