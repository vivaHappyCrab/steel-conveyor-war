using SFML.Graphics;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal enum GhostPreviewTone
{
    Place,
    Walk,
    Deny
}

internal static class GhostPreviewStyle
{
    internal static GhostPreviewTone From(BuildPlacementReport report)
    {
        if (report.CanPlaceNow)
        {
            return GhostPreviewTone.Place;
        }

        return report.CanQueueWalk ? GhostPreviewTone.Walk : GhostPreviewTone.Deny;
    }

    internal static Color Fill(GhostPreviewTone tone) => tone switch
    {
        GhostPreviewTone.Place => new Color(70, 170, 90, 90),
        GhostPreviewTone.Walk => new Color(220, 190, 60, 90),
        _ => new Color(190, 60, 60, 90)
    };

    internal static Color Outline(GhostPreviewTone tone) => tone switch
    {
        GhostPreviewTone.Place => new Color(140, 230, 150),
        GhostPreviewTone.Walk => new Color(255, 220, 110),
        _ => new Color(255, 140, 130)
    };

    internal static string PlaceLabel(BuildPlacementReport report)
    {
        if (report.CanPlaceNow)
        {
            return "Place: ok";
        }

        return report.Block switch
        {
            BuildPlacementBlock.OutOfRange => "Place: out of range",
            BuildPlacementBlock.Unaffordable => "Place: cannot afford",
            BuildPlacementBlock.Occupied => "Place: occupied",
            BuildPlacementBlock.WrongResource => "Place: wrong resource",
            BuildPlacementBlock.Locked => "Place: locked",
            BuildPlacementBlock.Unwalkable => "Place: blocked",
            BuildPlacementBlock.OutsideMap => "Place: outside map",
            _ => "Place: unknown"
        };
    }
}

internal static class MatchOutcome
{
    internal static string? Label(GameStatus status, int? winnerTeamId, int localTeamId)
    {
        return status switch
        {
            GameStatus.InProgress => null,
            GameStatus.Draw => "Draw",
            GameStatus.PlayerWon when winnerTeamId == localTeamId => "Victory",
            GameStatus.PlayerWon => "Defeat",
            _ => null
        };
    }
}
