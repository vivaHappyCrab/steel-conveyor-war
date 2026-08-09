using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public enum ResearchTreeNodeStatus
{
    Completed,
    Available,
    Locked,
    Active
}

public sealed record ResearchTreeNode(
    TechnologyId Id,
    string Symbol,
    string DisplayName,
    string Description,
    string CostLabel,
    string EffortLabel,
    string PrereqLabel,
    string StatusLabel,
    ResearchTreeNodeStatus Status,
    bool RequiresExclusiveConfirmation,
    string? TrackId,
    FloatRect Bounds);

public sealed record ResearchTreePanelModel(
    string ProfileId,
    string CurrentTierId,
    IReadOnlyList<ResearchTreeNode> Nodes,
    TechnologyId? SelectedId,
    TechnologyId? ActiveId,
    bool SupportsAllocationToggle,
    FloatRect OverlayBounds,
    FloatRect ExitButtonBounds,
    FloatRect ActionButtonBounds,
    FloatRect AllocationButtonBounds,
    string ActionButtonLabel,
    bool CanStartSelected,
    bool CanCancelSelected)
{
    public const float IconSize = 36f;
    public const float IconGapX = 14f;
    public const float IconGapY = 18f;
    public const float OverlayPadding = 10f;
    public const float ButtonHeight = 28f;
    public const float ButtonGap = 8f;

    public static ResearchTreePanelModel FromSnapshot(
        ResearchSnapshot snapshot,
        FloatRect overlayBounds,
        TechnologyId? selectedId)
    {
        TechnologyId? activeId = null;
        foreach (var track in snapshot.Tracks.OrderBy(track => track.Id, StringComparer.Ordinal))
        {
            if (track.ActiveSerialTarget is not null)
            {
                activeId = track.ActiveSerialTarget;
                break;
            }
        }

        if (activeId is null)
        {
            foreach (var track in snapshot.Tracks.OrderBy(track => track.Id, StringComparer.Ordinal))
            {
                if (track.ProjectWeights.Count == 0)
                {
                    continue;
                }

                activeId = track.ProjectWeights.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).First();
                break;
            }
        }

        var byTier = snapshot.Technologies
            .GroupBy(tech => tech.TierId)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        var nodes = new List<ResearchTreeNode>();
        var contentLeft = overlayBounds.Left + OverlayPadding;
        var contentTop = overlayBounds.Top + OverlayPadding + 22f;
        var column = 0;
        foreach (var tierGroup in byTier)
        {
            var row = 0;
            foreach (var tech in tierGroup.OrderBy(t => t.Id.Value, StringComparer.Ordinal))
            {
                var status = ResolveStatus(tech, activeId);
                var x = contentLeft + column * (IconSize + IconGapX);
                var y = contentTop + row * (IconSize + IconGapY);
                nodes.Add(new ResearchTreeNode(
                    tech.Id,
                    SymbolFor(tech.Id),
                    ShortId(tech.Id.Value),
                    BuildDescription(tech),
                    FormatCost(tech.SciencePacks),
                    $"{tech.ProgressWorkUnits}/{tech.EffortUnits}",
                    BuildPrereqLabel(tech, snapshot.CurrentTierId),
                    StatusLabel(status),
                    status,
                    tech.RequiresExclusiveConfirmation,
                    tech.TrackId,
                    new FloatRect(new Vector2f(x, y), new Vector2f(IconSize, IconSize))));
                row++;
            }

            column++;
        }

        var selected = selectedId is null
            ? null
            : snapshot.Technologies.FirstOrDefault(tech => tech.Id == selectedId);
        if (selected is null && selectedId is not null)
        {
            selectedId = null;
        }

        var selectedIsActive = selectedId is not null && activeId == selectedId;
        var canStart = selected is not null
            && !selected.IsCompleted
            && !selected.IsLocked
            && selected.IsAvailable
            && !selectedIsActive;
        var canCancel = selectedIsActive;

        var exitBounds = new FloatRect(
            new Vector2f(overlayBounds.Left + overlayBounds.Width - 78f, overlayBounds.Top + 6f),
            new Vector2f(70f, ButtonHeight));
        var actionBounds = new FloatRect(
            new Vector2f(overlayBounds.Left + OverlayPadding, overlayBounds.Top + overlayBounds.Height - ButtonHeight - OverlayPadding),
            new Vector2f(160f, ButtonHeight));
        var allocationBounds = new FloatRect(
            new Vector2f(overlayBounds.Left + OverlayPadding + 168f, overlayBounds.Top + overlayBounds.Height - ButtonHeight - OverlayPadding),
            new Vector2f(180f, ButtonHeight));

        var supportsAllocation = snapshot.Tracks.Any(track => track.PlayerAdjustableAllocation) && snapshot.Tracks.Count >= 2;
        var actionLabel = canCancel ? "Cancel research" : "Start research";

        return new ResearchTreePanelModel(
            snapshot.ProfileId,
            snapshot.CurrentTierId,
            nodes,
            selectedId,
            activeId,
            supportsAllocation,
            overlayBounds,
            exitBounds,
            actionBounds,
            allocationBounds,
            actionLabel,
            canStart,
            canCancel);
    }

    public static FloatRect ComputeOverlayBounds(uint windowWidth, uint windowHeight, float panelX, float bottomReserved)
    {
        var left = OverlayPadding;
        var top = TopBarHeight + OverlayPadding;
        var width = Math.Max(120f, panelX - left - OverlayPadding);
        var height = Math.Max(120f, windowHeight - bottomReserved - top - OverlayPadding);
        return new FloatRect(new Vector2f(left, top), new Vector2f(width, height));
    }

    // Mirrored from SfmlGameRunner for layout math without a circular dependency.
    private const float TopBarHeight = 36f;

    public ResearchTreeNode? SelectedNode =>
        SelectedId is null ? null : Nodes.FirstOrDefault(node => node.Id == SelectedId);

    public IEnumerable<string> ToDetailLines()
    {
        var node = SelectedNode;
        if (node is null)
        {
            yield return "Research tree";
            yield return $"Profile: {ProfileId}";
            yield return $"Tier: {CurrentTierId}";
            yield return "Click a tech icon for details.";
            yield return "Double-click or Start to research.";
            yield return "T / Exit: close overlay.";
            yield break;
        }

        yield return $"Tech: {node.DisplayName}";
        yield return $"Status: {node.StatusLabel}";
        yield return $"Desc: {node.Description}";
        yield return $"Cost: {node.CostLabel}";
        yield return $"Effort: {node.EffortLabel}";
        yield return $"Prereqs: {node.PrereqLabel}";
        if (node.TrackId is not null)
        {
            yield return $"Track: {node.TrackId}";
        }

        if (SupportsAllocationToggle)
        {
            yield return "Alloc button: cycle/tactical";
        }
    }

    public bool TryPickNode(Vector2i mouse, out TechnologyId technologyId)
    {
        technologyId = default!;
        var point = new Vector2f(mouse.X, mouse.Y);
        foreach (var node in Nodes)
        {
            if (Contains(node.Bounds, point))
            {
                technologyId = node.Id;
                return true;
            }
        }

        return false;
    }

    public bool HitExit(Vector2i mouse) => Contains(ExitButtonBounds, new Vector2f(mouse.X, mouse.Y));

    public bool HitAction(Vector2i mouse) => Contains(ActionButtonBounds, new Vector2f(mouse.X, mouse.Y));

    public bool HitAllocation(Vector2i mouse) =>
        SupportsAllocationToggle && Contains(AllocationButtonBounds, new Vector2f(mouse.X, mouse.Y));

    public bool ContainsOverlay(Vector2i mouse) => Contains(OverlayBounds, new Vector2f(mouse.X, mouse.Y));

    private static bool Contains(FloatRect rect, Vector2f point) =>
        point.X >= rect.Left
        && point.Y >= rect.Top
        && point.X < rect.Left + rect.Width
        && point.Y < rect.Top + rect.Height;

    private static ResearchTreeNodeStatus ResolveStatus(ResearchTechnologySnapshot tech, TechnologyId? activeId)
    {
        if (tech.IsCompleted)
        {
            return ResearchTreeNodeStatus.Completed;
        }

        if (activeId == tech.Id)
        {
            return ResearchTreeNodeStatus.Active;
        }

        if (tech.IsLocked || !tech.IsAvailable)
        {
            return ResearchTreeNodeStatus.Locked;
        }

        return ResearchTreeNodeStatus.Available;
    }

    private static string StatusLabel(ResearchTreeNodeStatus status) => status switch
    {
        ResearchTreeNodeStatus.Completed => "completed",
        ResearchTreeNodeStatus.Available => "available",
        ResearchTreeNodeStatus.Active => "active",
        _ => "locked"
    };

    private static string SymbolFor(TechnologyId id)
    {
        var shortId = ShortId(id.Value);
        var letter = shortId.FirstOrDefault(char.IsLetter);
        return letter == default ? "?" : char.ToUpperInvariant(letter).ToString();
    }

    private static string ShortId(string value)
    {
        var parts = value.Split('.');
        return parts.Length <= 2 ? value : string.Join('.', parts.TakeLast(2));
    }

    private static string BuildDescription(ResearchTechnologySnapshot tech)
    {
        if (tech.Tags.Count == 0)
        {
            return "No tags";
        }

        return string.Join(", ", tech.Tags.Take(4));
    }

    private static string BuildPrereqLabel(ResearchTechnologySnapshot tech, string currentTierId)
    {
        if (tech.IsCompleted)
        {
            return "-";
        }

        if (tech.IsLocked)
        {
            return "locked by exclusive";
        }

        if (!tech.IsAvailable)
        {
            return tech.TierId == currentTierId ? "unavailable" : $"requires {tech.TierId}";
        }

        return tech.RequiresExclusiveConfirmation ? "confirm exclusive" : "ready";
    }

    private static string FormatCost(IReadOnlyList<SciencePackCost> packs)
    {
        if (packs.Count == 0)
        {
            return "-";
        }

        return string.Join(", ", packs.Select(pack => $"{pack.Amount} {pack.Item}"));
    }
}
