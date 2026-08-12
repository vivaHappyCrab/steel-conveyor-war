namespace SteelConveyorWar.Core;

/// <summary>
/// H06: fair combat-tracer reveal policy. Hidden endpoints must not leak exact coordinates.
/// Documented in <c>docs/MVP_IMPLEMENTATION_DECISIONS.md</c> (FoW combat tracers).
/// </summary>
public enum CombatShotRevealMode
{
    /// <summary>Neither endpoint is observable — omit the event entirely.</summary>
    Hidden = 0,

    /// <summary>Both endpoints observable — full From→To line is allowed.</summary>
    Full = 1,

    /// <summary>Only the attacker/muzzle side is observable — draw toward target without exact To.</summary>
    MuzzleOnly = 2,

    /// <summary>Only the impact side is observable — show impact without exact From.</summary>
    ImpactOnly = 3,
}

/// <summary>Shared Core/SFML classifier for combat shot FoW (H06 Policy B).</summary>
public static class CombatShotVisibility
{
    /// <summary>
    /// Fixed presentation stub length (millitiles) for muzzle-only / impact-only reveals.
    /// Must not depend on the hidden endpoint — a fractional shot vector would encode exact fog coords.
    /// </summary>
    public const long StubLengthMilli = WorldUnits.MilliPerTile / 5; // 0.2 tile

    public static CombatShotRevealMode Classify(
        GameSimulation simulation,
        PlayerId observerId,
        CombatShotEvent shot,
        bool cheatMode = false)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (cheatMode)
        {
            return CombatShotRevealMode.Full;
        }

        var fromVisible = IsEndpointVisible(simulation, observerId, shot.AttackerId, shot.From);
        var toVisible = IsEndpointVisible(simulation, observerId, shot.TargetId, shot.To);
        return (fromVisible, toVisible) switch
        {
            (true, true) => CombatShotRevealMode.Full,
            (true, false) => CombatShotRevealMode.MuzzleOnly,
            (false, true) => CombatShotRevealMode.ImpactOnly,
            _ => CombatShotRevealMode.Hidden,
        };
    }

    public static bool IsVisible(CombatShotRevealMode mode) => mode != CombatShotRevealMode.Hidden;

    /// <summary>
    /// Sanitized endpoints for observation/draw. Hidden coordinates never leave this API:
    /// partial modes use a fixed-length axis stub that does not reference the opposite endpoint.
    /// </summary>
    public static (WorldPosition? From, WorldPosition? To) SanitizeEndpoints(
        CombatShotEvent shot,
        CombatShotRevealMode mode)
    {
        return mode switch
        {
            CombatShotRevealMode.Full => (shot.From, shot.To),
            // Fixed +X muzzle stub — independent of hidden To.
            CombatShotRevealMode.MuzzleOnly => (
                shot.From,
                new WorldPosition(shot.From.X + StubLengthMilli, shot.From.Y)),
            // Fixed -X approach stub — independent of hidden From.
            CombatShotRevealMode.ImpactOnly => (
                new WorldPosition(shot.To.X - StubLengthMilli, shot.To.Y),
                shot.To),
            _ => (null, null),
        };
    }

    private static bool IsEndpointVisible(
        GameSimulation simulation,
        PlayerId observerId,
        int entityId,
        WorldPosition fallbackTile)
    {
        var entity = simulation.World.GetEntity(entityId);
        if (entity is not null)
        {
            // Owned units always reveal their own muzzle/impact for the local observer.
            if (entity.OwnerId == observerId)
            {
                return true;
            }

            if (simulation.GetVisibility(observerId, entity.Position) == VisibilityState.Visible)
            {
                return true;
            }
        }

        return simulation.GetVisibility(observerId, fallbackTile.ToTilePosition()) == VisibilityState.Visible;
    }
}
