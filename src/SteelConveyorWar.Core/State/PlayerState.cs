namespace SteelConveyorWar.Core;

public sealed class PlayerState
{
    private readonly VisibilityState[,] _visibility;
    private readonly List<TilePosition> _visibleTiles = new();
    private readonly List<TechSignatureHotspot> _techSignatures = new();

    public PlayerState(
        PlayerId id,
        string name,
        WorldSize worldSize,
        int teamId,
        int ticksPerSecond = GameSimulation.DefaultTicksPerSecond)
    {
        Id = id;
        Name = name;
        TeamId = teamId;
        _visibility = new VisibilityState[worldSize.Width, worldSize.Height];
        // R32: size the presentation energy-history window from the match tick rate.
        EnergyStats = new EnergyStatsHistory(ticksPerSecond);
    }

    public PlayerId Id { get; }

    public string Name { get; }

    /// <summary>Static alliance id for the match. Same TeamId = allied (no FF, shared vision).</summary>
    public int TeamId { get; }

    public Inventory Inventory { get; } = new();

    public PlayerResearchState Research { get; } = new();

    public IReadOnlySet<TechnologyId> ResearchedTechnologies => Research.CompletedTechnologies;

    public bool IsDefeated { get; internal set; }

    public int PowerProduced { get; internal set; }

    public int PowerDemand { get; internal set; }

    /// <summary>
    /// Presentation-only energy time series for the SFML overlay.
    /// Not part of determinism hash; see § Presentation state in Core.
    /// </summary>
    public EnergyStatsHistory EnergyStats { get; }

    /// <summary>
    /// Presentation-only tech-signature hotspots for SFML fog overlay.
    /// Derived each tick from entities; not part of determinism hash.
    /// </summary>
    public IReadOnlyList<TechSignatureHotspot> TechSignatures => _techSignatures.AsReadOnly();

    public VisibilityState GetVisibility(TilePosition position)
    {
        return _visibility[position.X, position.Y];
    }

    internal void SetVisible(TilePosition position)
    {
        if (_visibility[position.X, position.Y] == VisibilityState.Visible)
        {
            return;
        }

        _visibility[position.X, position.Y] = VisibilityState.Visible;
        _visibleTiles.Add(position);
    }

    internal void DecayVisibility()
    {
        for (var i = 0; i < _visibleTiles.Count; i++)
        {
            var position = _visibleTiles[i];
            _visibility[position.X, position.Y] = VisibilityState.Explored;
        }

        _visibleTiles.Clear();
    }

    internal void SetTechSignatures(IEnumerable<TechSignatureHotspot> hotspots)
    {
        _techSignatures.Clear();
        _techSignatures.AddRange(hotspots);
    }
}
