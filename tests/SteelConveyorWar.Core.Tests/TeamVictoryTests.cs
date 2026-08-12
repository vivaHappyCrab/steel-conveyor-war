namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R31: victory is decided per team/side, not per surviving player. A 2v2 must not end at the
/// first casualty, and must end only once every player of one side is defeated. The default 1v1
/// (two single-player teams) is preserved: the sole survivor's team wins and <see cref="GameSimulation.WinnerId"/>
/// stays that player, so existing consumers and the deterministic state hash are unchanged.
/// </summary>
public sealed class TeamVictoryTests
{
    private static int KillCommander(GameSimulation simulation, PlayerId owner)
    {
        var commander = simulation.World.Entities.Single(entity =>
            entity.Kind == EntityKind.Commander && entity.OwnerId == owner && entity.IsAlive);
        Assert.True(simulation.TrySetEntityHealthForTests(commander.Id, 0));
        return commander.Id;
    }

    [Fact]
    public void TwoVsTwo_DoesNotEndAtFirstCasualty_EndsWhenOneSideFullyDefeated()
    {
        // Teams: players 1 & 2 -> team 1; players 3 & 4 -> team 2. Starts are explicit so all four seats spawn.
        var map = new MapSettings(
            1,
            "2v2",
            [
                new MapPlayerDefinition(1, "Blue", 1, new(4, 56), new(1, 56), new(5, 58)),
                new MapPlayerDefinition(2, "BlueAlly", 1, new(4, 20), new(1, 20), new(5, 22)),
                new MapPlayerDefinition(3, "Red", 2, new(187, 56), new(188, 56), new(186, 58)),
                new MapPlayerDefinition(4, "RedAlly", 2, new(187, 100), new(188, 100), new(186, 102))
            ]);
        var simulation = GameSimulation.CreateNewGame(GameCreationOptions.Default with { RandomSeed = 42, Map = map });
        Assert.Equal(4, simulation.Players.Count);
        Assert.Equal(4, simulation.World.Entities.Count(entity => entity.Kind == EntityKind.Commander && entity.IsAlive));

        simulation.AdvanceTick();
        Assert.Equal(GameStatus.InProgress, simulation.Status);

        // First casualty on team 1: its ally (player 2) still holds, so the match continues.
        KillCommander(simulation, new PlayerId(1));
        simulation.AdvanceTick();
        Assert.Equal(GameStatus.InProgress, simulation.Status);
        Assert.Null(simulation.WinnerTeamId);

        // Team 1 fully defeated -> team 2 wins.
        KillCommander(simulation, new PlayerId(2));
        simulation.AdvanceTick();
        Assert.Equal(GameStatus.PlayerWon, simulation.Status);
        Assert.Equal(2, simulation.WinnerTeamId);
        // Representative survivor is the lowest player id on the winning side (deterministic).
        Assert.Equal(new PlayerId(3), simulation.WinnerId);
    }

    [Fact]
    public void OneVsOne_Regression_SoleSurvivorWins_WinnerIdUnchanged()
    {
        // Default map: player 1 (team 1) vs player 2 (team 2), two single-player teams.
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);
        Assert.Equal(2, simulation.Players.Count);

        KillCommander(simulation, new PlayerId(1));
        simulation.AdvanceTick();

        Assert.Equal(GameStatus.PlayerWon, simulation.Status);
        // Unchanged 1v1 contract: the surviving player is the winner.
        Assert.Equal(new PlayerId(2), simulation.WinnerId);
        Assert.Equal(2, simulation.WinnerTeamId);
    }

    [Fact]
    public void MutualElimination_EndsInDraw()
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: 42);

        KillCommander(simulation, new PlayerId(1));
        KillCommander(simulation, new PlayerId(2));
        simulation.AdvanceTick();

        Assert.Equal(GameStatus.Draw, simulation.Status);
        Assert.Null(simulation.WinnerId);
        Assert.Null(simulation.WinnerTeamId);
    }
}
