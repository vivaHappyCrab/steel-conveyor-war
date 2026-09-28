namespace SteelConveyorWar.Core;

public enum BuildPlacementBlock
{
    None,
    OutsideMap,
    UnknownKind,
    Locked,
    Unwalkable,
    Occupied,
    WrongResource,
    OutOfRange,
    Unaffordable
}

public readonly record struct BuildPlacementReport(
    bool CanPlaceNow,
    bool CanQueueWalk,
    BuildPlacementBlock Block);
