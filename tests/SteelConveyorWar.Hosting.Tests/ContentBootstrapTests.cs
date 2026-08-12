using SteelConveyorWar.Core;
using SteelConveyorWar.Hosting;

namespace SteelConveyorWar.Hosting.Tests;

/// <summary>
/// R25 / M09: both hosts (Client, Headless) load content through one shared Hosting bootstrap, so
/// config I/O is defined in a single place and cannot drift between hosts. Core stays parse-only.
/// </summary>
public sealed class ContentBootstrapTests
{
    private static string ConfigDirectory()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "config"))
        };
        return candidates.First(dir => File.Exists(Path.Combine(dir, "game.json")));
    }

    [Fact]
    public void Load_ProducesUsableCreationOptions_AndRawGameJson()
    {
        var content = ContentBootstrap.Load(ConfigDirectory());

        Assert.False(string.IsNullOrWhiteSpace(content.GameJson));
        // CreationOptions carries all cross-validated catalogs and constructs a real match.
        var simulation = GameSimulation.CreateNewGame(content.CreationOptions);
        Assert.True(simulation.Players.Count >= 2);
        // Settings and creation options agree on the tick rate the hosts thread through.
        Assert.Equal(content.Settings.TicksPerSecond, simulation.TicksPerSecond);
    }

    [Fact]
    public void Load_IsDeterministic_SameContentForEveryHost()
    {
        var dir = ConfigDirectory();

        // Both hosts call the same bootstrap; two loads must agree on the composed content, which is
        // the guarantee that Client and Headless cannot drift.
        var a = ContentBootstrap.Load(dir);
        var b = ContentBootstrap.Load(dir);

        Assert.Equal(a.GameJson, b.GameJson);
        Assert.Equal(a.Settings, b.Settings);
        Assert.Equal(
            GameSimulation.CreateNewGame(a.CreationOptions).ComputeStateHash(),
            GameSimulation.CreateNewGame(b.CreationOptions).ComputeStateHash());
    }

    [Fact]
    public void Load_MissingConfigDirectory_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "scw-no-such-config-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(() => ContentBootstrap.Load(missing));
    }

    [Fact]
    public void ResolveConfigDirectory_FallsBackToBaseConfig_WhenNoCandidateExists()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "scw-base-" + Guid.NewGuid().ToString("N"));
        var currentDir = Path.Combine(Path.GetTempPath(), "scw-cur-" + Guid.NewGuid().ToString("N"));

        var resolved = ContentBootstrap.ResolveConfigDirectory(baseDir, currentDir);

        Assert.Equal(Path.Combine(baseDir, "config"), resolved);
    }
}
