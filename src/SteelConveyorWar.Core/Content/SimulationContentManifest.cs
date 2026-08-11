using System.Security.Cryptography;
using System.Text;

namespace SteelConveyorWar.Core;

/// <summary>
/// R10: Versioned identity of every authoritative gameplay catalog (research, build costs, entities,
/// tiles). Two peers whose catalogs differ in build costs or entity loss-conditions could otherwise
/// share the same tick-0 state hash and only diverge once a difference is first exercised. Folding a
/// combined manifest hash into the fingerprint (and a fail-fast handshake check) turns that latent
/// divergence into an up-front mismatch.
/// </summary>
public static class SimulationContentManifest
{
    /// <summary>Bumped when the manifest canonicalization changes.</summary>
    public const int ManifestVersion = 1;

    /// <summary>
    /// Combined content hash across all gameplay catalogs. Deterministic and independent of whether a
    /// catalog was loaded from JSON or the embedded defaults, as long as the content matches.
    /// </summary>
    public static string Compute(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var canonical = new StringBuilder();
        canonical.Append("manifest.v").Append(ManifestVersion).Append('\n');
        canonical.Append("research:").Append(simulation.ResearchCatalog.ContentHash).Append('\n');
        canonical.Append("profile:").Append(simulation.ResearchProfile.Id).Append('\n');
        canonical.Append("build:").Append(ComputeBuildCostIdentity(simulation.BuildCostCatalog)).Append('\n');
        canonical.Append("entity:").Append(ComputeEntityIdentity(simulation.EntityCatalog)).Append('\n');
        canonical.Append("tile:").Append(ComputeTileIdentity(simulation.TileCatalog)).Append('\n');
        return Sha256Hex(canonical.ToString());
    }

    /// <summary>
    /// Fail-fast handshake/replay guard: throws if two peers' content manifests differ, before tick 0.
    /// </summary>
    public static void EnsureMatch(GameSimulation local, GameSimulation remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        var localHash = Compute(local);
        var remoteHash = Compute(remote);
        if (!string.Equals(localHash, remoteHash, StringComparison.Ordinal))
        {
            throw new ContentManifestMismatchException(localHash, remoteHash);
        }
    }

    public static string ComputeBuildCostIdentity(BuildCostCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var canonical = new StringBuilder();
        canonical.Append("schema:").Append(catalog.SchemaVersion).Append('\n');

        canonical.Append("costs:\n");
        foreach (var kind in catalog.Costs.Keys.OrderBy(k => (int)k))
        {
            canonical.Append((int)kind).Append('=');
            foreach (var item in catalog.Costs[kind].OrderBy(pair => (int)pair.Key))
            {
                canonical.Append((int)item.Key).Append(':').Append(item.Value).Append(',');
            }

            canonical.Append('\n');
        }

        canonical.Append("ticks:\n");
        foreach (var pair in catalog.BuildTicks.OrderBy(p => (int)p.Key))
        {
            canonical.Append((int)pair.Key).Append('=').Append(pair.Value).Append('\n');
        }

        canonical.Append("requirements:\n");
        foreach (var pair in catalog.Requirements.OrderBy(p => (int)p.Key))
        {
            canonical.Append((int)pair.Key).Append('=').Append(pair.Value.Value).Append('\n');
        }

        return Sha256Hex(canonical.ToString());
    }

    public static string ComputeEntityIdentity(EntityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var canonical = new StringBuilder();
        canonical.Append("schema:").Append(catalog.SchemaVersion).Append('\n');
        foreach (var pair in catalog.Entities.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var definition = pair.Value;
            canonical.Append(pair.Key).Append('|')
                .Append(definition.Id).Append('|')
                .Append(definition.Name).Append('|')
                .Append(definition.Kind).Append('|')
                .Append(definition.BuildsStructures ? '1' : '0').Append('|')
                .Append(definition.LossCondition ?? string.Empty).Append('\n');
        }

        return Sha256Hex(canonical.ToString());
    }

    public static string ComputeTileIdentity(TileCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var canonical = new StringBuilder();
        canonical.Append("schema:").Append(catalog.SchemaVersion).Append('\n');
        foreach (var pair in catalog.Tiles.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var definition = pair.Value;
            canonical.Append(pair.Key).Append('|')
                .Append(definition.Id).Append('|')
                .Append(definition.Name).Append('|')
                .Append(definition.Walkable ? '1' : '0').Append('|')
                .Append(definition.Resource ?? string.Empty).Append('\n');
        }

        return Sha256Hex(canonical.ToString());
    }

    private static string Sha256Hex(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>R10: thrown when peer content manifests disagree during the pre-simulation handshake.</summary>
public sealed class ContentManifestMismatchException : Exception
{
    public ContentManifestMismatchException(string localHash, string remoteHash)
        : base($"Content manifest mismatch: local={localHash} remote={remoteHash}. Peers must share identical gameplay catalogs before simulation start.")
    {
        LocalHash = localHash;
        RemoteHash = remoteHash;
    }

    public string LocalHash { get; }

    public string RemoteHash { get; }
}
