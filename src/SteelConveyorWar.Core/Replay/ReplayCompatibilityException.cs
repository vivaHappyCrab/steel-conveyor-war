namespace SteelConveyorWar.Core.Replay;

/// <summary>
/// Thrown when a replay file cannot be attached to the current build or match identity
/// (format/protocol/algorithm/session fields) before tick 0.
/// </summary>
public sealed class ReplayCompatibilityException : Exception
{
    public ReplayCompatibilityException(string message)
        : base(message)
    {
    }
}
