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

        // R31: victory is decided per team/side, not per surviving player. A team is alive while
        // at least one of its players is undefeated; the match ends when a single team remains.
        // M11: zero active teams (mutual elimination) terminates as Draw instead of eternal InProgress.
        var activePlayers = _players.Where(player => !player.IsDefeated).ToList();
        var activeTeams = activePlayers.Select(player => player.TeamId).Distinct().ToList();
        var totalTeams = _players.Select(player => player.TeamId).Distinct().Count();

        if (totalTeams > 1 && activeTeams.Count == 0)
        {
            Status = GameStatus.Draw;
            WinnerTeamId = null;
            WinnerId = null;
            return;
        }

        // The multi-team guard prevents a degenerate single-team roster from declaring an instant
        // winner. For the default 1v1 (two single-player teams) this fires exactly when one player
        // survives, so WinnerId stays that player and the deterministic state hash is unchanged.
        if (totalTeams > 1 && activeTeams.Count == 1)
        {
            Status = GameStatus.PlayerWon;
            WinnerTeamId = activeTeams[0];
            // Representative survivor: lowest player id on the winning team (stable, deterministic).
            WinnerId = activePlayers.OrderBy(player => player.Id.Value).First().Id;
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
