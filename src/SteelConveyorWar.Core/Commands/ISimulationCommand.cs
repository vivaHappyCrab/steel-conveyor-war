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
}
