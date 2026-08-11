namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// Tick-stamped gameplay intent applied at the start of <see cref="GameSimulation.AdvanceTick"/>
/// when <see cref="Tick"/> equals the simulation tick (after increment).
/// </summary>
public interface ISimulationCommand
{
    SimulationCommandKind Kind { get; }

    /// <summary>Authoring player; ownership checks use this actor where applicable.</summary>
    PlayerId Actor { get; }

    /// <summary>Simulation tick at which this command is applied.</summary>
    long Tick { get; }

    /// <summary>
    /// R03: monotonic per-author ordering key assigned by the command source. Combined with
    /// <see cref="Actor"/> it yields a canonical cross-peer order <c>(Tick, Actor, Sequence)</c>,
    /// so the applied order does not depend on network arrival order. Also used for
    /// duplicate suppression. A value of 0 means "unsequenced" (legacy/local, exempt from dedup).
    /// </summary>
    long Sequence { get; }
}
