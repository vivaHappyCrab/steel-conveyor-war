namespace SteelConveyorWar.Core.Commands;

/// <summary>
/// M02: stable payload equality for protocol tests. Uses the canonical wire snapshot so collection
/// order and nested fields are compared the same way peers would see them after serialization.
/// </summary>
public static class CommandPayloadEquality
{
    /// <summary>Canonical JSON snapshot of header + payload (protocol version stamped).</summary>
    public static string Snapshot(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SimulationCommandSerializer.Serialize(command);
    }

    /// <summary>True when both commands serialize to the same wire snapshot.</summary>
    public static bool AreEqual(ISimulationCommand left, ISimulationCommand right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Snapshot(left) == Snapshot(right);
    }
}
