using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Replay;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// Client-owned replay watch context. SFML does not read files; Client loads the document and
/// catalogs, then this type recreates the simulation from tick 0.
/// </summary>
public sealed class ReplayWatchSession
{
    public ReplayWatchSession(ReplayDocument document, GameCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(creationOptions);
        Document = document;
        CreationOptions = creationOptions;
    }

    public ReplayDocument Document { get; }

    public GameCreationOptions CreationOptions { get; }

    public GameSimulation Restart() => ReplayPlayback.CreateReady(CreationOptions, Document);
}

/// <summary>HUD overlay for spectator playback (not hashed, presentation only).</summary>
public readonly record struct ReplayHudStatus(
    long Tick,
    long DurationTicks,
    int Rate,
    bool Paused,
    bool? HashMatch);
