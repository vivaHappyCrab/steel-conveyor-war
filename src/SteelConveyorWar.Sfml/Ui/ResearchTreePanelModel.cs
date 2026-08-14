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

public sealed record ResearchTreeEdge(Vector2f From, Vector2f To);

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
    bool IsMandatory,
    FloatRect Bounds);

public sealed record ResearchTreePanelModel(
    string ProfileId,
    string CurrentTierId,
    IReadOnlyList<ResearchTreeNode> Nodes,
    IReadOnlyList<ResearchTreeEdge> Edges,
    TechnologyId? SelectedId,
    TechnologyId? ActiveId,
    bool SupportsAllocationToggle,
    FloatRect OverlayBounds,
    FloatRect ContentViewport,
    float ContentHeight,
    FloatRect ExitButtonBounds,
    FloatRect ActionButtonBounds,
    FloatRect AllocationButtonBounds,
    string ActionButtonLabel,
    bool CanStartSelected,
    bool CanCancelSelected)
{
    public const float IconSize = 36f;
    public const float IconGapX = 14f;
    public const float IconGapY = 16f;
    public const float OverlayPadding = 10f;
    public const float ButtonHeight = 28f;
    public const float ButtonGap = 8f;
    public const float LabelHeight = 14f;
    public const float BusGap = 22f;
    public const float TierGap = 28f;
    public const float OptionalOffsetX = 48f;
    public const float TitleChromeHeight = 34f;
    public const float ScrollStep = 48f;

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

        var mandatoryByTier = snapshot.Gates
            .GroupBy(gate => gate.FromTierId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(gate => gate.Requirements)
                    .SelectMany(requirement => requirement.CandidateTechnologyIds)
                    .ToHashSet(),
                StringComparer.Ordinal);

        var visible = snapshot.Technologies
            .Where(tech =>
                tech.IsCompleted
                || tech.IsAvailable
                || tech.IsLocked
                || tech.ProgressWorkUnits > 0
                || mandatoryByTier.Values.Any(set => set.Contains(tech.Id)))
            .ToList();

        var byTier = visible
            .GroupBy(tech => tech.TierId)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        var contentViewport = ComputeContentViewport(overlayBounds);
        var nodes = new List<ResearchTreeNode>();
        var edges = new List<ResearchTreeEdge>();
        // Content-local coordinates: (0,0) is top-left of the scrollable viewport.
        var contentLeft = 0f;
        var cursorY = 0f;
        Vector2f? previousBusCenter = null;

        foreach (var tierGroup in byTier)
        {
            var mandatoryIds = mandatoryByTier.GetValueOrDefault(tierGroup.Key) ?? new HashSet<TechnologyId>();
            var mandatory = tierGroup
                .Where(tech => mandatoryIds.Contains(tech.Id))
                .OrderBy(tech => tech.Id.Value, StringComparer.Ordinal)
                .ToList();
            var optional = tierGroup
                .Where(tech => !mandatoryIds.Contains(tech.Id))
                .OrderBy(tech => tech.Id.Value, StringComparer.Ordinal)
                .ToList();

            if (mandatory.Count == 0 && optional.Count == 0)
            {
                continue;
            }

            if (mandatory.Count == 0)
            {
                // No gate row for this tier — pack optionals as a simple column.
                var col = 0;
                foreach (var tech in optional)
                {
                    var x = contentLeft + col * (IconSize + IconGapX);
                    var y = cursorY;
                    nodes.Add(CreateNode(tech, activeId, snapshot.CurrentTierId, isMandatory: false, x, y));
                    col++;
                }

                cursorY += IconSize + LabelHeight + TierGap;
                continue;
            }

            var mandatoryCenters = new List<Vector2f>();
            for (var i = 0; i < mandatory.Count; i++)
            {
                var x = contentLeft + i * (IconSize + IconGapX);
                var y = cursorY;
                var node = CreateNode(mandatory[i], activeId, snapshot.CurrentTierId, isMandatory: true, x, y);
                nodes.Add(node);
                mandatoryCenters.Add(new Vector2f(node.Bounds.Left + IconSize / 2f, node.Bounds.Top + IconSize));
            }

            var busY = cursorY + IconSize + BusGap / 2f;
            var busLeft = mandatoryCenters[0].X;
            var busRight = mandatoryCenters[^1].X;
            foreach (var center in mandatoryCenters)
            {
                edges.Add(new ResearchTreeEdge(center, new Vector2f(center.X, busY)));
            }

            edges.Add(new ResearchTreeEdge(new Vector2f(busLeft, busY), new Vector2f(busRight, busY)));

            if (previousBusCenter is not null)
            {
                var trunkX = (busLeft + busRight) / 2f;
                edges.Add(new ResearchTreeEdge(previousBusCenter.Value, new Vector2f(trunkX, previousBusCenter.Value.Y)));
                edges.Add(new ResearchTreeEdge(new Vector2f(trunkX, previousBusCenter.Value.Y), new Vector2f(trunkX, busY)));
                edges.Add(new ResearchTreeEdge(new Vector2f(trunkX, busY), new Vector2f((busLeft + busRight) / 2f, busY)));
            }

            var optionalStartX = busRight + OptionalOffsetX;
            if (optional.Count > 0)
            {
                edges.Add(new ResearchTreeEdge(new Vector2f(busRight, busY), new Vector2f(optionalStartX, busY)));
            }

            for (var i = 0; i < optional.Count; i++)
            {
                var x = optionalStartX;
                var y = cursorY + i * (IconSize + LabelHeight + IconGapY);
                var node = CreateNode(optional[i], activeId, snapshot.CurrentTierId, isMandatory: false, x, y);
                nodes.Add(node);
                edges.Add(new ResearchTreeEdge(
                    new Vector2f(optionalStartX, busY),
                    new Vector2f(node.Bounds.Left, node.Bounds.Top + IconSize / 2f)));
            }

            previousBusCenter = new Vector2f((busLeft + busRight) / 2f, busY);
            var optionalHeight = optional.Count == 0
                ? 0f
                : optional.Count * (IconSize + LabelHeight + IconGapY);
            var rowHeight = Math.Max(IconSize + LabelHeight + BusGap, optionalHeight);
            cursorY += rowHeight + TierGap;
        }

        var contentHeight = Math.Max(cursorY, nodes.Count == 0
            ? 0f
            : nodes.Max(node => node.Bounds.Top + node.Bounds.Height + LabelHeight));

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
            edges,
            selectedId,
            activeId,
            supportsAllocation,
            overlayBounds,
            contentViewport,
            contentHeight,
            exitBounds,
            actionBounds,
            allocationBounds,
            actionLabel,
            canStart,
            canCancel);
    }

    private static ResearchTreeNode CreateNode(
        ResearchTechnologySnapshot tech,
        TechnologyId? activeId,
        string currentTierId,
        bool isMandatory,
        float x,
        float y)
    {
        var status = ResolveStatus(tech, activeId);
        return new ResearchTreeNode(
            tech.Id,
            SymbolFor(tech.DisplayName),
            tech.DisplayName,
            tech.Description,
            FormatCost(tech.SciencePacks),
            $"{tech.ProgressWorkUnits}/{tech.EffortUnits}",
            BuildPrereqLabel(tech, currentTierId, isMandatory),
            StatusLabel(status),
            status,
            tech.RequiresExclusiveConfirmation,
            tech.TrackId,
            isMandatory,
            new FloatRect(new Vector2f(x, y), new Vector2f(IconSize, IconSize)));
    }

    public static FloatRect ComputeOverlayBounds(uint windowWidth, uint windowHeight, float panelX, float bottomReserved)
    {
        var left = OverlayPadding;
        var top = TopBarHeight + OverlayPadding;
        var width = Math.Max(120f, panelX - left - OverlayPadding);
        var height = Math.Max(120f, windowHeight - bottomReserved - top - OverlayPadding);
        return new FloatRect(new Vector2f(left, top), new Vector2f(width, height));
    }

    public static FloatRect ComputeContentViewport(FloatRect overlayBounds)
    {
        var buttonReserve = ButtonHeight + OverlayPadding * 2f;
        var height = Math.Max(40f, overlayBounds.Height - TitleChromeHeight - buttonReserve);
        return new FloatRect(
            new Vector2f(overlayBounds.Left + OverlayPadding, overlayBounds.Top + TitleChromeHeight),
            new Vector2f(Math.Max(40f, overlayBounds.Width - OverlayPadding * 2f), height));
    }

    public static float ClampScroll(float scrollY, float contentHeight, float viewportHeight) =>
        Math.Clamp(scrollY, 0f, Math.Max(0f, contentHeight - viewportHeight));

    // Mirrored from SfmlGameRunner for layout math without a circular dependency.
    private const float TopBarHeight = 36f;

    public ResearchTreeNode? SelectedNode =>
        SelectedId is null ? null : Nodes.FirstOrDefault(node => node.Id == SelectedId);

    public IEnumerable<string> ToDetailLines()
    {
        var node = SelectedNode;
        if (node is null)
        {
            yield return "Дерево исследований";
            yield return $"Профиль: {ProfileId}";
            yield return $"Тир: {CurrentTierId}";
            yield return "Клик — детали. Двойной клик / Start — запуск.";
            yield return "Колёсико — прокрутка дерева.";
            yield return "Связи: обязательные → шина → следующий тир;";
            yield return "опциональные висят сбоку шины.";
            yield return "T / Exit: закрыть.";
            yield break;
        }

        yield return node.DisplayName;
        yield return node.IsMandatory ? "Тип: обязательное" : "Тип: опциональное";
        yield return $"Статус: {node.StatusLabel}";
        yield return node.Description;
        yield return $"Стоимость: {node.CostLabel}";
        yield return $"Прогресс: {node.EffortLabel}";
        yield return $"Доступ: {node.PrereqLabel}";
        if (node.TrackId is not null)
        {
            yield return $"Трек: {node.TrackId}";
        }

        if (SupportsAllocationToggle)
        {
            yield return "Alloc: cycle/tactical";
        }
    }

    public bool TryPickNode(Vector2i mouse, float scrollOffsetY, out TechnologyId technologyId)
    {
        technologyId = default!;
        var point = new Vector2f(mouse.X, mouse.Y);
        if (!Contains(ContentViewport, point))
        {
            return false;
        }

        var contentPoint = new Vector2f(
            point.X - ContentViewport.Left,
            point.Y - ContentViewport.Top + scrollOffsetY);
        foreach (var node in Nodes)
        {
            if (Contains(node.Bounds, contentPoint))
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

    public bool ContainsContentViewport(Vector2i mouse) =>
        Contains(ContentViewport, new Vector2f(mouse.X, mouse.Y));

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
        ResearchTreeNodeStatus.Completed => "завершено",
        ResearchTreeNodeStatus.Available => "доступно",
        ResearchTreeNodeStatus.Active => "активно",
        _ => "закрыто"
    };

    private static string SymbolFor(string displayName)
    {
        var letter = displayName.FirstOrDefault(char.IsLetter);
        return letter == default ? "?" : char.ToUpperInvariant(letter).ToString();
    }

    private static string BuildPrereqLabel(ResearchTechnologySnapshot tech, string currentTierId, bool isMandatory)
    {
        if (tech.IsCompleted)
        {
            return "-";
        }

        if (tech.IsLocked)
        {
            return "закрыто эксклюзивом";
        }

        if (!tech.IsAvailable)
        {
            if (tech.Prerequisites.Count > 0)
            {
                return "нужно: " + string.Join(
                    ", ",
                    tech.Prerequisites.Select(id => ResearchDisplayNames.GetDisplayName(id)));
            }

            return tech.TierId == currentTierId
                ? "недоступно"
                : isMandatory
                    ? $"нужна шина {tech.TierId}"
                    : $"нужен {tech.TierId}";
        }

        return tech.RequiresExclusiveConfirmation ? "подтвердить эксклюзив" : "готово";
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
