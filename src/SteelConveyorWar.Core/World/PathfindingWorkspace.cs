namespace SteelConveyorWar.Core;

/// <summary>
/// Reusable A* scratch buffers. Clear between pathfind calls instead of allocating new collections.
/// </summary>
internal sealed class PathfindingWorkspace
{
    public PriorityQueue<TilePosition, (int F, int H, int Y, int X)> Open { get; } = new();

    public Dictionary<TilePosition, TilePosition?> Previous { get; } = new();

    public Dictionary<TilePosition, int> CostSoFar { get; } = new();

    public void Clear()
    {
        Open.Clear();
        Previous.Clear();
        CostSoFar.Clear();
    }
}
