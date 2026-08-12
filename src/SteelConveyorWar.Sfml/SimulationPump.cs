using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// M08: fixed-step simulation pump — pacing, AdvanceTick, FoW dirty union, combat shot linger capture.
/// Presentation and window ownership stay outside this type.
/// </summary>
internal sealed class SimulationPump
{
    private readonly GameSimulation _simulation;
    private readonly PlayerId _localPlayer;
    private readonly InputCommandMapper _inputMapper;
    private readonly List<(CombatShotEvent Shot, CombatShotRevealMode Reveal, float Remaining)> _lingeringShots = new();
    private readonly HashSet<(int X, int Y)> _frameFogDirtyKeys = new();
    private readonly List<TilePosition> _frameFogDirtyTiles = new();
    private float _accumulator;

    public SimulationPump(GameSimulation simulation, PlayerId localPlayer, InputCommandMapper inputMapper)
    {
        _simulation = simulation;
        _localPlayer = localPlayer;
        _inputMapper = inputMapper;
    }

    public IReadOnlyList<(CombatShotEvent Shot, CombatShotRevealMode Reveal, float Remaining)> LingeringShots =>
        _lingeringShots;

    public IReadOnlyList<TilePosition> FrameFogDirtyTiles => _frameFogDirtyTiles;

    public void TickDemolishHold(float frameDt, bool rightPressed, int? hoverEntityId)
    {
        _inputMapper.TickDemolishHold(_simulation, frameDt, rightPressed, hoverEntityId);
    }

    public void AdvanceFixedSteps(float frameDt, float fixedDelta)
    {
        _accumulator += frameDt;
        var (ticksThisFrame, pacedAccumulator) = FixedStepPacer.Plan(_accumulator, fixedDelta);
        _accumulator = pacedAccumulator;
        _frameFogDirtyKeys.Clear();
        for (var tickIndex = 0; tickIndex < ticksThisFrame; tickIndex++)
        {
            _simulation.AdvanceTick();
            MinimapDirtyTracker.AccumulateFogDirty(
                _frameFogDirtyKeys,
                _simulation.GetFogDirtyTiles(_localPlayer));
            foreach (var shot in _simulation.CombatShotsThisTick)
            {
                var reveal = WorldRenderer.ClassifyShotForLocalPlayer(_simulation, _localPlayer, shot);
                if (CombatShotVisibility.IsVisible(reveal))
                {
                    _lingeringShots.Add((shot, reveal, SfmlUiLayout.CombatShotLingerSeconds));
                }
            }

            _inputMapper.RefreshSelectionVisibility(_simulation);
        }

        MinimapDirtyTracker.CopyFogDirty(_frameFogDirtyKeys, _frameFogDirtyTiles);
    }

    public void AgeLingeringShots(float frameDt)
    {
        for (var i = _lingeringShots.Count - 1; i >= 0; i--)
        {
            var remaining = _lingeringShots[i].Remaining - frameDt;
            if (remaining <= 0f)
            {
                _lingeringShots.RemoveAt(i);
            }
            else
            {
                _lingeringShots[i] = (_lingeringShots[i].Shot, _lingeringShots[i].Reveal, remaining);
            }
        }
    }

    public IReadOnlyList<(CombatShotEvent Shot, CombatShotRevealMode Reveal)> VisibleShots()
    {
        if (_lingeringShots.Count == 0)
        {
            return Array.Empty<(CombatShotEvent, CombatShotRevealMode)>();
        }

        var shots = new List<(CombatShotEvent, CombatShotRevealMode)>(_lingeringShots.Count);
        for (var i = 0; i < _lingeringShots.Count; i++)
        {
            shots.Add((_lingeringShots[i].Shot, _lingeringShots[i].Reveal));
        }

        return shots;
    }
}
