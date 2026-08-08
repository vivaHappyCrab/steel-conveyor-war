using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public sealed record ResearchPanelModel(
    string ProfileId,
    string CurrentTierId,
    IReadOnlyList<string> TrackLines,
    IReadOnlyList<string> GateLines,
    IReadOnlyList<ResearchPanelEntry> PageEntries,
    int PageIndex,
    int PageCount,
    bool SupportsAllocationToggle,
    IReadOnlyList<string> ExclusivePreviewLines)
{
    public const int DefaultPageSize = 8;

    public static ResearchPanelModel FromSnapshot(ResearchSnapshot snapshot, int pageIndex, int pageSize = DefaultPageSize)
    {
        var available = snapshot.Technologies
            .Where(tech => tech.IsAvailable || tech.RequiresExclusiveConfirmation)
            .OrderBy(tech => tech.Id.Value, StringComparer.Ordinal)
            .ToList();

        var pageCount = Math.Max(1, (available.Count + pageSize - 1) / pageSize);
        var safePage = Math.Clamp(pageIndex, 0, pageCount - 1);
        var pageEntries = available
            .Skip(safePage * pageSize)
            .Take(pageSize)
            .Select((tech, index) => new ResearchPanelEntry(
                ShortcutIndex: index,
                Id: tech.Id,
                ProgressLabel: $"{tech.ProgressWorkUnits}/{tech.EffortUnits}",
                TrackId: tech.TrackId,
                RequiresExclusiveConfirmation: tech.RequiresExclusiveConfirmation,
                IsLocked: tech.IsLocked,
                Tags: tech.Tags))
            .ToList();

        var trackLines = snapshot.Tracks
            .Select(track =>
            {
                var active = track.Mode == ResearchTrackMode.Serial
                    ? track.ActiveSerialTarget?.Value ?? "-"
                    : string.Join(",", track.ProjectWeights.Keys.Select(id => id.Value).OrderBy(v => v));
                return $"{track.Id}: {track.AllocationBasisPoints}bp [{track.Mode}] {active}";
            })
            .ToList();

        var gateLines = snapshot.Gates
            .Select(gate =>
            {
                var progress = string.Join(
                    ",",
                    gate.Requirements.Select(requirement => $"{requirement.Id}:{requirement.CompletedCount}/{requirement.MinimumCompleted}"));
                var status = gate.IsCompleted ? "done" : "open";
                return $"{gate.Id} [{status}] {progress}";
            })
            .ToList();

        var exclusivePreview = available
            .Where(tech => tech.RequiresExclusiveConfirmation)
            .Select(tech => $"confirm:{tech.Id.Value}")
            .ToList();

        var supportsAllocation = snapshot.Tracks.Any(track => track.PlayerAdjustableAllocation) && snapshot.Tracks.Count >= 2;

        return new ResearchPanelModel(
            snapshot.ProfileId,
            snapshot.CurrentTierId,
            trackLines,
            gateLines,
            pageEntries,
            safePage,
            pageCount,
            supportsAllocation,
            exclusivePreview);
    }

    public IEnumerable<string> ToHudLines()
    {
        yield return $"Research [{ProfileId}] tier={CurrentTierId} page={PageIndex + 1}/{PageCount}";
        foreach (var track in TrackLines)
        {
            yield return $"  {track}";
        }

        foreach (var gate in GateLines.Take(2))
        {
            yield return $"  gate {gate}";
        }

        if (SupportsAllocationToggle)
        {
            yield return "  [T] toggle cycle/tactical allocation";
        }

        for (var i = 0; i < PageEntries.Count; i++)
        {
            var entry = PageEntries[i];
            var marker = entry.RequiresExclusiveConfirmation ? "!" : entry.IsLocked ? "x" : " ";
            yield return $"  {i}:{marker}{ShortId(entry.Id.Value)} {entry.ProgressLabel}";
        }

        foreach (var preview in ExclusivePreviewLines.Take(2))
        {
            yield return $"  {preview}";
        }
    }

    private static string ShortId(string value)
    {
        var parts = value.Split('.');
        return parts.Length <= 2 ? value : string.Join('.', parts.TakeLast(2));
    }
}

public sealed record ResearchPanelEntry(
    int ShortcutIndex,
    TechnologyId Id,
    string ProgressLabel,
    string? TrackId,
    bool RequiresExclusiveConfirmation,
    bool IsLocked,
    IReadOnlyList<string> Tags);
