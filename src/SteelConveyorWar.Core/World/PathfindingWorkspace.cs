namespace SteelConveyorWar.Core;

/// <summary>
/// Reusable A* scratch buffers. Clear between pathfind calls instead of allocating new collections.
/// </summary>
internal sealed class PathfindingWorkspace
{
    public PriorityQueue<TilePosition, (int F, int H, int Y, int X)> Open { get; } = new();

    public Dictionary<TilePosition, TilePosition?> Previous { get; } = new();

    public Dictionary<TilePosition, int> CostSoFar { get; } = new();

    /// <summary>M06: pooled path reconstruction + candidate ring buffers.</summary>
    public List<TilePosition> PathScratch { get; } = new();

    public List<TilePosition> CandidateScratch { get; } = new();

    public void Clear()
    {
        ClearSearch();
        PathScratch.Clear();
        CandidateScratch.Clear();
    }

    /// <summary>Clears A* search state only so candidate rings can survive across attempts.</summary>
    public void ClearSearch()
    {
        Open.Clear();
        Previous.Clear();
        CostSoFar.Clear();
    }
}
