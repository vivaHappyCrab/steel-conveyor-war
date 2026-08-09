namespace SteelConveyorWar.Core;

/// <summary>
/// Presentation-only combat FX for one shot during a tick. Not part of authoritative hashed state.
/// </summary>
public readonly record struct CombatShotEvent(
    int AttackerId,
    int TargetId,
    WorldPosition From,
    WorldPosition To,
    ProjectileKind ProjectileKind);
