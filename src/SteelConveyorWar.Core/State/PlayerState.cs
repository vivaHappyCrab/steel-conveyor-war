namespace SteelConveyorWar.Core;

public sealed class PlayerState
{
    private readonly VisibilityState[,] _visibility;
    private readonly List<TechSignatureHotspot> _techSignatures = new();

    public PlayerState(PlayerId id, string name, WorldSize worldSize)
    {
        Id = id;
        Name = name;
        _visibility = new VisibilityState[worldSize.Width, worldSize.Height];
    }

    public PlayerId Id { get; }

    public string Name { get; }

    public Inventory Inventory { get; } = new();

    public PlayerResearchState Research { get; } = new();

    public IReadOnlySet<TechnologyId> ResearchedTechnologies => Research.CompletedTechnologies;

    public bool IsDefeated { get; internal set; }

    public int PowerProduced { get; internal set; }

    public int PowerDemand { get; internal set; }

    public IReadOnlyList<TechSignatureHotspot> TechSignatures => _techSignatures;

    public VisibilityState GetVisibility(TilePosition position)
    {
        return _visibility[position.X, position.Y];
    }

    internal void SetVisible(TilePosition position)
    {
        _visibility[position.X, position.Y] = VisibilityState.Visible;
    }

    internal void DecayVisibility()
    {
        for (var y = 0; y < _visibility.GetLength(1); y++)
        {
            for (var x = 0; x < _visibility.GetLength(0); x++)
            {
                if (_visibility[x, y] == VisibilityState.Visible)
                {
                    _visibility[x, y] = VisibilityState.Explored;
                }
            }
        }
    }

    internal void SetTechSignatures(IEnumerable<TechSignatureHotspot> hotspots)
    {
        _techSignatures.Clear();
        _techSignatures.AddRange(hotspots);
    }
}
