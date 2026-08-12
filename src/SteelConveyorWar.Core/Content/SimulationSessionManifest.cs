using System.Security.Cryptography;
using System.Text;

namespace SteelConveyorWar.Core;

/// <summary>
/// M11 / R10+R31+R32: session identity beyond gameplay catalogs — algorithm version, content
/// manifest, map id, roster (players/teams/starts/colors), TPS, seed, and research profile.
/// Peers call <see cref="EnsureMatch"/> before tick 0 so mismatched session settings fail fast
/// even when content hashes agree.
/// </summary>
public static class SimulationSessionManifest
{
    /// <summary>Bumped when session canonicalization changes.</summary>
    public const int ManifestVersion = 1;

    public static string Compute(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var canonical = new StringBuilder();
        canonical.Append("session.v").Append(ManifestVersion).Append('\n');
        canonical.Append("algorithm:").Append(SimulationStateHasher.AlgorithmVersion).Append('\n');
        canonical.Append("content:").Append(SimulationContentManifest.Compute(simulation)).Append('\n');
        canonical.Append("tps:").Append(simulation.TicksPerSecond).Append('\n');
        canonical.Append("seed:").Append(simulation.RandomSeed).Append('\n');
        canonical.Append("profile:").Append(simulation.ResearchProfile.Id).Append('\n');
        AppendMapRoster(canonical, simulation.Map);
        return Sha256Hex(canonical.ToString());
    }

    /// <summary>
    /// Fail-fast handshake/replay guard: throws if two peers' session manifests differ, before tick 0.
    /// </summary>
    public static void EnsureMatch(GameSimulation local, GameSimulation remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        var localHash = Compute(local);
        var remoteHash = Compute(remote);
        if (!string.Equals(localHash, remoteHash, StringComparison.Ordinal))
        {
            throw new SessionManifestMismatchException(localHash, remoteHash);
        }
    }

    private static void AppendMapRoster(StringBuilder canonical, MapSettings map)
    {
        canonical.Append("mapId:").Append(map.MapId).Append('\n');
        canonical.Append("schema:").Append(map.SchemaVersion).Append('\n');
        canonical.Append("roster:\n");
        foreach (var player in map.Players.OrderBy(p => p.Id))
        {
            var color = player.Color ?? MapPlayerDefinition.DefaultColorForPlayer(player.Id);
            var (commander, bastion, hub) = player.ResolveStartPositions(
                new WorldSize(MapPlayerDefinition.DefaultWorldWidth, MapPlayerDefinition.DefaultWorldHeight));
            canonical.Append(player.Id).Append('|')
                .Append(player.Name).Append('|')
                .Append(player.TeamId).Append('|')
                .Append(color).Append('|')
                .Append("cmd:").Append(commander.X).Append(',').Append(commander.Y).Append('|')
                .Append("bas:").Append(bastion.X).Append(',').Append(bastion.Y).Append('|')
                .Append("hub:").Append(hub.X).Append(',').Append(hub.Y).Append('\n');
        }
    }

    private static string Sha256Hex(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>M11: thrown when peer session manifests disagree during the pre-simulation handshake.</summary>
public sealed class SessionManifestMismatchException : Exception
{
    public SessionManifestMismatchException(string localHash, string remoteHash)
        : base($"Session manifest mismatch: local={localHash} remote={remoteHash}. Peers must share identical map/roster/TPS/seed/content before simulation start.")
    {
        LocalHash = localHash;
        RemoteHash = remoteHash;
    }

    public string LocalHash { get; }

    public string RemoteHash { get; }
}
