namespace SteelConveyorWar.Core;

public enum ResearchTrackMode
{
    Serial,
    WeightedParallel
}

public enum ExclusiveLockOn
{
    Start,
    Complete
}

public enum ModifierOperation
{
    Add,
    Multiply
}

public enum ResearchCommandResult
{
    Ok,
    UnknownTechnology,
    AlreadyCompleted,
    Locked,
    NotAvailable,
    ExclusiveConfirmationRequired,
    InvalidTrack,
    InvalidAllocation,
    InvalidWeight,
    UnknownProfile
}

public static class ResearchStatIds
{
    public const string ConveyorMoveTicks = "stat.conveyor.move-ticks";
    public const string FactoryWorkTicks = "stat.factory.work-ticks";
    public const string SmelterWorkTicks = "stat.smelter.work-ticks";
    public const string ConstructionTicks = "stat.construction.ticks";
    public const string VisionRadius = "stat.vision.radius";
    public const string HubStorageStacks = "stat.hub.storage-stacks";
    public const string EnergyShortagePenalty = "stat.energy.shortage-penalty";
    public const string BastionTemplateCapacity = "stat.bastion.template-capacity";
    public const string MaxBastions = "stat.bastion.max-count";
    public const string AttackDamage = "stat.combat.attack-damage";
    public const string Armor = "stat.combat.armor";
    public const string AttackCooldownTicks = "stat.combat.attack-cooldown-ticks";
    public const string MaxHealth = "stat.combat.max-health";
}

public static class ResearchCapabilityIds
{
    public const string AdditionalBastions = "capability.additional-bastions";
    public const string RepairOutOfCombat = "capability.repair-out-of-combat";
    public const string Tier2Content = "capability.tier2-content";
}

public static class ResearchMilestoneIds
{
    public const string T3Qualified = "milestone.t3-qualified";
}

public static class ResearchTierIds
{
    public const string T1 = "tier.t1";
    public const string T2 = "tier.t2";
}

public static class ResearchTrackIds
{
    public const string Primary = "track.primary";
    public const string Cycle = "track.cycle";
    public const string Tactical = "track.tactical";
}

public static class ResearchProfileIds
{
    public const string MvpB = "mvp-b";
}
