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
    /// Fraction of the full shot vector used for muzzle-only / impact-only stubs so the hidden
    /// endpoint coordinate is never implied exactly.
    /// </summary>
    public const int PartialSegmentBasisPoints = 2_000; // 20%

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

    /// <summary>Sanitized endpoints for observation/draw — hidden sides are null or stubbed.</summary>
    public static (WorldPosition? From, WorldPosition? To) SanitizeEndpoints(
        CombatShotEvent shot,
        CombatShotRevealMode mode)
    {
        return mode switch
        {
            CombatShotRevealMode.Full => (shot.From, shot.To),
            CombatShotRevealMode.MuzzleOnly => (shot.From, PartialToward(shot.From, shot.To)),
            CombatShotRevealMode.ImpactOnly => (PartialToward(shot.To, shot.From), shot.To),
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

    private static WorldPosition PartialToward(WorldPosition from, WorldPosition toward)
    {
        var dx = toward.X - from.X;
        var dy = toward.Y - from.Y;
        return new WorldPosition(
            from.X + dx * PartialSegmentBasisPoints / ModifierResolver.BasisPointsScale,
            from.Y + dy * PartialSegmentBasisPoints / ModifierResolver.BasisPointsScale);
    }
}
