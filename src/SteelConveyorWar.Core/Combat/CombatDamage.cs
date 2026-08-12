namespace SteelConveyorWar.Core;

/// <summary>
/// Deterministic combat formula C helpers (integer basis-point math).
/// </summary>
public static class CombatDamage
{
    /// <summary>
    /// Formula C: <c>final = max(1, AttackDamage - Armor) * ResistanceMultiplier</c>,
    /// with resistance applied in basis points and truncated toward zero.
    /// </summary>
    public static int ComputeFinalDamage(int attackDamage, int armor, int resistanceBasisPoints)
    {
        var afterArmor = Math.Max(1, attackDamage - armor);
        if (resistanceBasisPoints <= 0)
        {
            return 0;
        }

        return (int)((long)afterArmor * resistanceBasisPoints / ModifierResolver.BasisPointsScale);
    }

    public static int GetResistanceBasisPoints(
        ProjectileKind projectileKind,
        CombatTargetCategory targetCategory,
        GameplayTablesCatalog tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return tables.GetResistanceBasisPoints(projectileKind, targetCategory);
    }
}
