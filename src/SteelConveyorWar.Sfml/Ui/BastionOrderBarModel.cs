using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public enum BastionOrderCommand
{
    ActiveDefense = 0,
    Patrol = 1,
    Attack = 2,
    Scout = 3
}

public enum BastionPendingInputMode
{
    None,
    AttackTarget,
    ScoutTarget,
    PatrolWaypoints
}

public static class BastionOrderBarModel
{
    public static readonly BastionOrderCommand[] Commands =
    [
        BastionOrderCommand.ActiveDefense,
        BastionOrderCommand.Patrol,
        BastionOrderCommand.Attack,
        BastionOrderCommand.Scout
    ];

    public static readonly EntityKind[] TemplateUnitKinds = MvpDefinitions.UnitKinds
        .OrderBy(kind => (int)kind)
        .ToArray();

    public static string Label(BastionOrderCommand command)
    {
        return command switch
        {
            BastionOrderCommand.ActiveDefense => "Active Defense",
            BastionOrderCommand.Patrol => "Patrol",
            BastionOrderCommand.Attack => "Attack",
            BastionOrderCommand.Scout => "Scout",
            _ => command.ToString()
        };
    }

    public static string Glyph(BastionOrderCommand command)
    {
        return command switch
        {
            BastionOrderCommand.ActiveDefense => "D",
            BastionOrderCommand.Patrol => "P",
            BastionOrderCommand.Attack => "A",
            BastionOrderCommand.Scout => "S",
            _ => "?"
        };
    }

    public static string? ShortcutBadge(int catalogIndex)
    {
        return catalogIndex is >= 0 and <= 3 ? (catalogIndex + 1).ToString() : null;
    }

    public static bool TryGetCommand(int index, out BastionOrderCommand command)
    {
        if (index < 0 || index >= Commands.Length)
        {
            command = default;
            return false;
        }

        command = Commands[index];
        return true;
    }

    public static BastionPendingInputMode PendingModeFor(BastionOrderCommand command)
    {
        return command switch
        {
            BastionOrderCommand.Patrol => BastionPendingInputMode.PatrolWaypoints,
            BastionOrderCommand.Attack => BastionPendingInputMode.AttackTarget,
            BastionOrderCommand.Scout => BastionPendingInputMode.ScoutTarget,
            _ => BastionPendingInputMode.None
        };
    }

    public static bool IsCommandHighlighted(
        BastionOrderCommand command,
        BastionPendingInputMode pendingMode,
        BastionOrderKind currentOrder)
    {
        return command switch
        {
            BastionOrderCommand.ActiveDefense =>
                pendingMode == BastionPendingInputMode.None && currentOrder == BastionOrderKind.Defend,
            BastionOrderCommand.Patrol =>
                pendingMode == BastionPendingInputMode.PatrolWaypoints || currentOrder == BastionOrderKind.Patrol,
            BastionOrderCommand.Attack =>
                pendingMode == BastionPendingInputMode.AttackTarget || currentOrder == BastionOrderKind.AttackArea,
            BastionOrderCommand.Scout =>
                pendingMode == BastionPendingInputMode.ScoutTarget || currentOrder == BastionOrderKind.Scout,
            _ => false
        };
    }

    public static string FormatOrder(BastionOrder order)
    {
        return order.Kind switch
        {
            BastionOrderKind.Defend => "Defend",
            BastionOrderKind.AttackArea => order.Target is null
                ? "Attack"
                : $"Attack ({order.Target.Value.X},{order.Target.Value.Y})",
            BastionOrderKind.Scout => order.Target is null
                ? "Scout"
                : $"Scout ({order.Target.Value.X},{order.Target.Value.Y})",
            BastionOrderKind.Patrol => $"Patrol ({order.WaypointList.Count} wp)",
            _ => order.Kind.ToString()
        };
    }

    public static string PendingHint(BastionPendingInputMode mode, int patrolWaypointCount)
    {
        return mode switch
        {
            BastionPendingInputMode.AttackTarget => "Pending Attack: LMB/RMB map = target, Esc cancel",
            BastionPendingInputMode.ScoutTarget => "Pending Scout: LMB/RMB map = target, Esc cancel",
            BastionPendingInputMode.PatrolWaypoints =>
                $"Pending Patrol: LMB add wp ({patrolWaypointCount}/4), Enter/RMB confirm (2+), Esc cancel",
            _ => string.Empty
        };
    }
}
