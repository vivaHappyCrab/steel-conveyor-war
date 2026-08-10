namespace SteelConveyorWar.Core;

/// <summary>
/// How an adapter/bot reads simulation state.
/// </summary>
public enum PlayerObservationMode
{
    /// <summary>
    /// FoW-limited read model matching SFML draw gating: unknown terrain is hidden;
    /// live non-owned entities appear only on <see cref="VisibilityState.Visible"/> tiles.
    /// Preferred for fair AI and future net clients.
    /// </summary>
    Fair,

    /// <summary>
    /// Unfiltered live <see cref="GameWorld"/> access (full entity list and terrain).
    /// Intended for tests, tools, and explicit cheat bots — not default product AI.
    /// </summary>
    Cheat
}
