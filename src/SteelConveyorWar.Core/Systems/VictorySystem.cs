namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Defeat-loss checks (catalog <c>lossCondition</c>) and match winner resolution.
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
        var lossKinds = ResolveDefeatLossKinds();
        foreach (var player in _players)
        {
            // Empty loss set (catalog loaded with no Defeat entries) means no entity-based defeat.
            var criticalAlive = lossKinds.Count == 0
                || World.Entities.Any(entity =>
                    entity.OwnerId == player.Id
                    && entity.IsAlive
                    && lossKinds.Contains(entity.Kind));
            player.IsDefeated = !criticalAlive;
        }

        var activePlayers = _players.Where(player => !player.IsDefeated).ToList();
        if (activePlayers.Count == 1)
        {
            Status = GameStatus.PlayerWon;
            WinnerId = activePlayers[0].Id;
        }
    }

    /// <summary>
    /// Defeat kinds come from <see cref="EntityCatalog"/> when loaded; empty catalog keeps MVP
    /// commander-survival fallback so headless tests without JSON stay valid.
    /// </summary>
    private IReadOnlySet<EntityKind> ResolveDefeatLossKinds()
    {
        if (EntityCatalog.Entities.Count == 0)
        {
            return s_defaultDefeatLossKinds;
        }

        return EntityCatalog.GetDefeatLossKinds();
    }

    private static readonly HashSet<EntityKind> s_defaultDefeatLossKinds = [EntityKind.Commander];
}
