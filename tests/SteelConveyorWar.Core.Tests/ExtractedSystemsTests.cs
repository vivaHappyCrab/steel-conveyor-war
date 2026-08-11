namespace SteelConveyorWar.Core.Tests;

/// <summary>
/// R06: the Power and Combat systems were extracted out of the <see cref="GameSimulation"/> god-object
/// into standalone types that depend only on <see cref="ISimulationSystemContext"/>. These tests exercise
/// each system in isolation against an in-memory fake context (no full simulation), plus a determinism
/// regression proving the wired-up systems still produce a stable per-run state hash.
/// </summary>
public sealed class ExtractedSystemsTests
{
    [Fact]
    public void CombatSystem_DealsDamageToEnemyInRange_AndSetsCooldown()
    {
        var size = new WorldSize(16, 16);
        var terrain = new TerrainType[size.Width, size.Height];
        var p1 = new PlayerId(1);
        var p2 = new PlayerId(2);
        var attacker = new WorldEntity(1, EntityKind.BasicTank, new TilePosition(5, 5), p1);
        var target = new WorldEntity(2, EntityKind.BasicTank, new TilePosition(6, 5), p2);
        var world = new GameWorld(size, terrain, new[] { attacker, target });
        var players = new[]
        {
            new PlayerState(p1, "P1", size, teamId: 1),
            new PlayerState(p2, "P2", size, teamId: 2),
        };
        var context = new FakeSystemContext(world, players, tick: 1);
        var combat = new CombatSystem(context);

        var healthBefore = target.Health;
        combat.Tick();

        Assert.True(target.Health < healthBefore, "enemy in range should take damage");
        Assert.True(attacker.AttackCooldownRemaining > 0, "attacker should be on cooldown after firing");
    }

    [Fact]
    public void CombatSystem_DoesNotDamageAlliedUnit()
    {
        var size = new WorldSize(16, 16);
        var terrain = new TerrainType[size.Width, size.Height];
        var p1 = new PlayerId(1);
        var p2 = new PlayerId(2);
        // Same TeamId => allied => must not be targeted.
        var attacker = new WorldEntity(1, EntityKind.BasicTank, new TilePosition(5, 5), p1);
        var ally = new WorldEntity(2, EntityKind.BasicTank, new TilePosition(6, 5), p2);
        var world = new GameWorld(size, terrain, new[] { attacker, ally });
        var players = new[]
        {
            new PlayerState(p1, "P1", size, teamId: 7),
            new PlayerState(p2, "P2", size, teamId: 7),
        };
        var context = new FakeSystemContext(world, players, tick: 1);
        var combat = new CombatSystem(context);

        var healthBefore = ally.Health;
        combat.Tick();

        Assert.Equal(healthBefore, ally.Health);
    }

    [Fact]
    public void PowerSystem_FillsConsumerBufferFromProduction()
    {
        var size = new WorldSize(16, 16);
        var terrain = new TerrainType[size.Width, size.Height];
        var p1 = new PlayerId(1);
        var solar = new WorldEntity(1, EntityKind.SolarPanel, new TilePosition(2, 2), p1);
        var consumer = new WorldEntity(2, EntityKind.Assembler, new TilePosition(3, 2), p1)
        {
            EnergyBufferCapacity = 10,
            EnergyBuffer = 0,
        };
        var world = new GameWorld(size, terrain, new[] { solar, consumer });
        var players = new[] { new PlayerState(p1, "P1", size, teamId: 1) };
        var context = new FakeSystemContext(world, players);
        var power = new PowerSystem(context);

        power.Tick();

        // SolarPanel produces 5; the single eligible consumer receives all of it (emptiest-first).
        Assert.Equal(5, consumer.EnergyBuffer);
    }

    [Fact]
    public void PowerSystem_TryConsumeBuildingEnergy_DrainsWhenAffordableElsePauses()
    {
        var size = new WorldSize(8, 8);
        var terrain = new TerrainType[size.Width, size.Height];
        var p1 = new PlayerId(1);
        var assembler = new WorldEntity(1, EntityKind.Assembler, new TilePosition(2, 2), p1)
        {
            EnergyBufferCapacity = 10,
            EnergyBuffer = 0,
        };
        var world = new GameWorld(size, terrain, new[] { assembler });
        var players = new[] { new PlayerState(p1, "P1", size, teamId: 1) };
        var context = new FakeSystemContext(world, players);
        var power = new PowerSystem(context);

        var demand = MvpDefinitions.GetPowerDemand(EntityKind.Assembler);
        if (demand <= 0)
        {
            // Assembler has no configured demand in this content set: unpowered work is free.
            Assert.True(power.TryConsumeBuildingEnergy(assembler));
            return;
        }

        // Empty buffer cannot afford the drain.
        Assert.False(power.TryConsumeBuildingEnergy(assembler));

        assembler.EnergyBuffer = demand;
        Assert.True(power.TryConsumeBuildingEnergy(assembler));
        Assert.Equal(0, assembler.EnergyBuffer);
    }

    // Behavior-preserving guard: the full simulation, now orchestrating the extracted PowerSystem and
    // CombatSystem, must still be deterministic (identical state hash across two identical runs).
    [Fact]
    public void ExtractedSystems_PreserveDeterministicHashAcrossRuns()
    {
        var hashA = RunTicks(seed: 123, ticks: 200);
        var hashB = RunTicks(seed: 123, ticks: 200);
        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length);
    }

    private static string RunTicks(int seed, int ticks)
    {
        var simulation = GameSimulation.CreateNewGame(randomSeed: seed);
        for (var i = 0; i < ticks; i++)
        {
            simulation.AdvanceTick();
        }

        return simulation.ComputeStateHash();
    }

    /// <summary>
    /// Minimal in-memory <see cref="ISimulationSystemContext"/> backed by a real <see cref="GameWorld"/>
    /// and roster, with research/stat resolution stubbed to the baseline (no modifiers). Lets Power and
    /// Combat be constructed and driven without a full <see cref="GameSimulation"/>.
    /// </summary>
    private sealed class FakeSystemContext : ISimulationSystemContext
    {
        private readonly List<PlayerState> _players;

        public FakeSystemContext(GameWorld world, IEnumerable<PlayerState> players, long tick = 1)
        {
            World = world;
            _players = players.ToList();
            Tick = tick;
        }

        public GameWorld World { get; }

        public long Tick { get; set; }

        public IReadOnlyList<PlayerState> Players => _players;

        public SimulationPresentationSink Presentation { get; } = new();

        public int CascadeBastionDeathsCalls { get; private set; }

        public PlayerState GetPlayer(PlayerId playerId) => _players.Single(player => player.Id == playerId);

        public bool AreAllied(PlayerId? a, PlayerId? b) =>
            a is not null && b is not null && GetPlayer(a.Value).TeamId == GetPlayer(b.Value).TeamId;

        // No research in the fake: every stat resolves to its baseline.
        public int ResolveStat(PlayerId playerId, string statId, int baseValue, string? selector = null, int? minValue = 1) =>
            baseValue;

        public void SyncResolvedMaxHealth(WorldEntity entity)
        {
        }

        public void CascadeBastionDeaths() => CascadeBastionDeathsCalls++;

        public bool TryConsumeBuildingEnergy(WorldEntity building) => true;

        public void CollectSortedAliveEntities(List<WorldEntity> into, Func<WorldEntity, bool> predicate)
        {
            into.Clear();
            foreach (var entity in World.Entities)
            {
                if (entity.IsAlive && predicate(entity))
                {
                    into.Add(entity);
                }
            }

            into.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        }
    }
}
