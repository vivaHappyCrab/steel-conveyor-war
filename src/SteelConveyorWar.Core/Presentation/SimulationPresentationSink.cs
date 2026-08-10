namespace SteelConveyorWar.Core;

/// <summary>
/// Core-owned presentation side-channels for SFML (combat tracers, etc.).
/// Never authoritative for gameplay and never part of <see cref="SimulationStateHasher"/>.
/// Per-player presentation rings (<see cref="PlayerState.EnergyStats"/>, tech-signature hotspots)
/// stay on <see cref="PlayerState"/> but follow the same exclusion policy.
/// </summary>
public sealed class SimulationPresentationSink
{
    private readonly List<CombatShotEvent> _combatShotsThisTick = new();

    /// <summary>
    /// Shots landed during the last combat pass of <see cref="GameSimulation.AdvanceTick"/>.
    /// Cleared at the start of each combat pass.
    /// </summary>
    public IReadOnlyList<CombatShotEvent> CombatShotsThisTick => _combatShotsThisTick;

    /// <summary>
    /// Appends a presentation-only combat FX event. Safe for tests/diagnostics:
    /// mutations here must never change <see cref="GameSimulation.ComputeStateHash"/>.
    /// </summary>
    public void AddCombatShot(CombatShotEvent shot) => _combatShotsThisTick.Add(shot);

    internal void ClearCombatShots() => _combatShotsThisTick.Clear();
}
