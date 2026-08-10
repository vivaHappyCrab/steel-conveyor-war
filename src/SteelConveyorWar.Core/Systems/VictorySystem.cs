namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Commander-defeat checks and match winner resolution.
    /// </summary>
    private static class VictorySystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.CheckVictory();
        }
    }

    private void CheckVictory()
    {
        foreach (var player in _players)
        {
            var commanderAlive = World.Entities.Any(entity => entity.OwnerId == player.Id && entity.Kind == EntityKind.Commander && entity.IsAlive);
            player.IsDefeated = !commanderAlive;
        }

        var activePlayers = _players.Where(player => !player.IsDefeated).ToList();
        if (activePlayers.Count == 1)
        {
            Status = GameStatus.PlayerWon;
            WinnerId = activePlayers[0].Id;
        }
    }
}
