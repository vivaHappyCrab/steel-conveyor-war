namespace SteelConveyorWar.Core;

/// <summary>
/// A shell already in the air. Damage is applied on <see cref="LandTick"/> at <see cref="ImpactX"/>/<see cref="ImpactY"/>,
/// the tile the target occupied when the shot was fired.
/// </summary>
public readonly record struct ArtilleryShot(
    int AttackerId,
    int OwnerId,
    long LaunchTick,
    long LandTick,
    long OriginXMilli,
    long OriginYMilli,
    int ImpactX,
    int ImpactY,
    int AttackDamage,
    ProjectileKind ProjectileKind,
    int SplashRadius);
