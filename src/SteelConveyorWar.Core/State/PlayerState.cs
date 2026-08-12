namespace SteelConveyorWar.Core;

public sealed class PlayerState
{
    private readonly VisibilityState[,] _visibility;
    private readonly List<TilePosition> _visibleTiles = new();
    private readonly List<TechSignatureHotspot> _techSignatures = new();

    // R16 / R33: tiles whose Visible↔Explored/Unknown state changed during the last FoW repaint.
    private readonly List<TilePosition> _fogDirtyTiles = new();
    private readonly List<TilePosition> _fogDirtyPreviousVisible = new();
    private readonly HashSet<long> _fogDirtyPreviousKeys = new();
    private readonly HashSet<long> _fogDirtyRetainedKeys = new();
    private bool _trackingFogDirty;

    public PlayerState(
        PlayerId id,
        string name,
        WorldSize worldSize,
        int teamId,
        string color = MapPlayerDefinition.DefaultColorPlayerOne,
        int ticksPerSecond = GameSimulation.DefaultTicksPerSecond)
    {
        Id = id;
        Name = name;
        TeamId = teamId;
        Color = color;
        _visibility = new VisibilityState[worldSize.Width, worldSize.Height];
        // R32: size the presentation energy-history window from the match tick rate.
        EnergyStats = new EnergyStatsHistory(ticksPerSecond);
    }

    public PlayerId Id { get; }

    public string Name { get; }

    /// <summary>Static alliance id for the match. Same TeamId = allied (no FF, shared vision).</summary>
    public int TeamId { get; }

    /// <summary>R31: roster seat color from map config (presentation; not hashed).</summary>
    public string Color { get; }

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
    public IReadOnlyList<TechSignatureHotspot> TechSignatures => _techSignatures;

    /// <summary>
    /// R16/R33: tiles whose visibility state changed on the most recent FoW update.
    /// Empty when vision sources were unchanged and repaint was skipped. Not hashed.
    /// </summary>
    public IReadOnlyList<TilePosition> FogDirtyTiles => _fogDirtyTiles;

    public VisibilityState GetVisibility(TilePosition position)
    {
        return _visibility[position.X, position.Y];
    }

    internal void ClearFogDirty()
    {
        _fogDirtyTiles.Clear();
        _fogDirtyPreviousVisible.Clear();
        _fogDirtyPreviousKeys.Clear();
        _fogDirtyRetainedKeys.Clear();
        _trackingFogDirty = false;
    }

    /// <summary>
    /// Snapshot current Visible tiles so <see cref="SetVisible"/> / <see cref="EndFogDirtyTracking"/>
    /// can emit the symmetric difference after decay+repaint.
    /// </summary>
    internal void BeginFogDirtyTracking()
    {
        _fogDirtyTiles.Clear();
        _fogDirtyPreviousVisible.Clear();
        _fogDirtyPreviousKeys.Clear();
        _fogDirtyRetainedKeys.Clear();
        for (var i = 0; i < _visibleTiles.Count; i++)
        {
            var position = _visibleTiles[i];
            _fogDirtyPreviousVisible.Add(position);
            _fogDirtyPreviousKeys.Add(PackTile(position));
        }

        _trackingFogDirty = true;
    }

    internal void EndFogDirtyTracking()
    {
        if (!_trackingFogDirty)
        {
            return;
        }

        for (var i = 0; i < _fogDirtyPreviousVisible.Count; i++)
        {
            var position = _fogDirtyPreviousVisible[i];
            if (!_fogDirtyRetainedKeys.Contains(PackTile(position)))
            {
                _fogDirtyTiles.Add(position);
            }
        }

        _fogDirtyPreviousVisible.Clear();
        _fogDirtyPreviousKeys.Clear();
        _fogDirtyRetainedKeys.Clear();
        _trackingFogDirty = false;
    }

    internal void SetVisible(TilePosition position)
    {
        if (_visibility[position.X, position.Y] == VisibilityState.Visible)
        {
            return;
        }

        _visibility[position.X, position.Y] = VisibilityState.Visible;
        _visibleTiles.Add(position);

        if (!_trackingFogDirty)
        {
            return;
        }

        var key = PackTile(position);
        if (_fogDirtyPreviousKeys.Contains(key))
        {
            _fogDirtyRetainedKeys.Add(key);
        }
        else
        {
            _fogDirtyTiles.Add(position);
        }
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

    /// <summary>M06: decay a single tile without scanning the full visible set.</summary>
    internal void DecayVisibilityAt(TilePosition position)
    {
        if (_visibility[position.X, position.Y] != VisibilityState.Visible)
        {
            return;
        }

        _visibility[position.X, position.Y] = VisibilityState.Explored;
        for (var i = _visibleTiles.Count - 1; i >= 0; i--)
        {
            if (_visibleTiles[i].X == position.X && _visibleTiles[i].Y == position.Y)
            {
                _visibleTiles.RemoveAt(i);
                break;
            }
        }
    }

    internal void SetTechSignatures(IEnumerable<TechSignatureHotspot> hotspots)
    {
        _techSignatures.Clear();
        _techSignatures.AddRange(hotspots);
    }

    internal void SetTechSignatures(List<TechSignatureHotspot> hotspots)
    {
        _techSignatures.Clear();
        _techSignatures.AddRange(hotspots);
    }

    private static long PackTile(TilePosition position)
        => ((long)position.X << 32) | (uint)position.Y;
}
