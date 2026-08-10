using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public enum EnergyStatsWindowKind
{
    Seconds10 = 10,
    Seconds30 = 30,
    Minutes1 = 60,
    Minutes5 = 300,
    Minutes10 = 600
}

public sealed record EnergyStatsLegendRow(
    EntityKind Kind,
    string Glyph,
    string DisplayName,
    Color SeriesColor,
    double AveragePerTick,
    FloatRect IconBounds,
    FloatRect ColorBounds,
    FloatRect ValueBounds);

public sealed class EnergyStatsPanelModel
{
    public const float IntervalButtonWidth = 72f;
    public const float IntervalButtonHeight = 28f;
    public const float IntervalGap = 8f;
    public const float GraphHeight = 160f;
    public const float AxisLeftPad = 36f;
    public const float AxisBottomPad = 18f;
    public const float GraphTitleGap = 18f;
    public const float RowHeight = 28f;
    public const float IconSize = 22f;
    public const float ColorBarWidth = 8f;

    public static readonly EnergyStatsWindowKind[] IntervalOptions =
    [
        EnergyStatsWindowKind.Seconds10,
        EnergyStatsWindowKind.Seconds30,
        EnergyStatsWindowKind.Minutes1,
        EnergyStatsWindowKind.Minutes5,
        EnergyStatsWindowKind.Minutes10
    ];

    public static readonly Color TotalSeriesColor = new(220, 60, 60);

    private static readonly Color[] SeriesPalette =
    [
        new Color(70, 190, 90),
        new Color(230, 200, 60),
        new Color(70, 140, 230),
        new Color(200, 110, 220),
        new Color(240, 140, 60),
        new Color(80, 200, 200),
        new Color(180, 180, 100),
        new Color(150, 100, 80),
        new Color(100, 160, 120),
        new Color(160, 120, 200),
        new Color(120, 180, 255)
    ];

    public required FloatRect OverlayBounds { get; init; }
    public required FloatRect ExitButtonBounds { get; init; }
    public required IReadOnlyList<FloatRect> IntervalButtonBounds { get; init; }
    public required EnergyStatsWindowKind SelectedInterval { get; init; }
    public required FloatRect ConsumeGraphBounds { get; init; }
    public required FloatRect ProduceGraphBounds { get; init; }
    public required IReadOnlyList<EnergyStatsLegendRow> ConsumerRows { get; init; }
    public required IReadOnlyList<EnergyStatsLegendRow> ProducerRows { get; init; }
    public required EnergyStatsWindow Stats { get; init; }
    public required string ConsumeTotalLabel { get; init; }
    public required string ProduceTotalLabel { get; init; }

    public static EnergyStatsPanelModel Build(
        EnergyStatsWindow stats,
        EnergyStatsWindowKind selectedInterval,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        float bottomReserved)
    {
        var overlay = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
        var exit = new FloatRect(
            new Vector2f(overlay.Left + overlay.Width - 78f, overlay.Top + 6f),
            new Vector2f(70f, ResearchTreePanelModel.ButtonHeight));

        var intervalBounds = new List<FloatRect>();
        var intervalY = overlay.Top + ResearchTreePanelModel.TitleChromeHeight;
        var intervalX = overlay.Left + ResearchTreePanelModel.OverlayPadding;
        for (var i = 0; i < IntervalOptions.Length; i++)
        {
            intervalBounds.Add(new FloatRect(
                new Vector2f(intervalX + i * (IntervalButtonWidth + IntervalGap), intervalY),
                new Vector2f(IntervalButtonWidth, IntervalButtonHeight)));
        }

        var contentTop = intervalY + IntervalButtonHeight + 10f + GraphTitleGap;
        var contentLeft = overlay.Left + ResearchTreePanelModel.OverlayPadding;
        var contentWidth = overlay.Width - ResearchTreePanelModel.OverlayPadding * 2f;
        var columnGap = 16f;
        var columnWidth = (contentWidth - columnGap) * 0.5f;
        // Outer graph bounds include shared axis pads so both plot areas stay GraphHeight × equal width.
        var outerGraphHeight = GraphHeight + AxisBottomPad;
        var consumeGraph = new FloatRect(new Vector2f(contentLeft, contentTop), new Vector2f(columnWidth, outerGraphHeight));
        var produceGraph = new FloatRect(
            new Vector2f(contentLeft + columnWidth + columnGap, contentTop),
            new Vector2f(columnWidth, outerGraphHeight));

        var tableTop = contentTop + outerGraphHeight + 12f;
        var consumerRows = BuildLegendRows(stats.ConsumerRows, contentLeft, tableTop, columnWidth, isProducer: false);
        var producerRows = BuildLegendRows(stats.ProducerRows, contentLeft + columnWidth + columnGap, tableTop, columnWidth, isProducer: true);

        return new EnergyStatsPanelModel
        {
            OverlayBounds = overlay,
            ExitButtonBounds = exit,
            IntervalButtonBounds = intervalBounds,
            SelectedInterval = selectedInterval,
            ConsumeGraphBounds = consumeGraph,
            ProduceGraphBounds = produceGraph,
            ConsumerRows = consumerRows,
            ProducerRows = producerRows,
            Stats = stats,
            ConsumeTotalLabel = $"Сумма: {stats.AverageDemandPerTick:0.##}/t",
            ProduceTotalLabel = $"Сумма: {stats.AverageProducedPerTick:0.##}/t"
        };
    }

    public static string IntervalLabel(EnergyStatsWindowKind kind) => kind switch
    {
        EnergyStatsWindowKind.Seconds10 => "10 сек",
        EnergyStatsWindowKind.Seconds30 => "30 сек",
        EnergyStatsWindowKind.Minutes1 => "1 мин",
        EnergyStatsWindowKind.Minutes5 => "5 мин",
        EnergyStatsWindowKind.Minutes10 => "10 мин",
        _ => kind.ToString()
    };

    public static Color ColorForKind(EntityKind kind, bool isProducer)
    {
        var list = isProducer ? EnergyStatsHistory.AllProducerKinds : EnergyStatsHistory.AllConsumerKinds;
        var index = 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == kind)
            {
                index = i;
                break;
            }
        }

        return SeriesPalette[index % SeriesPalette.Length];
    }

    public static string DisplayName(EntityKind kind) => kind.ToString();

    public bool HitExit(Vector2i mouse) => Contains(ExitButtonBounds, mouse);

    public bool ContainsOverlay(Vector2i mouse) => Contains(OverlayBounds, mouse);

    public bool TryHitInterval(Vector2i mouse, out EnergyStatsWindowKind kind)
    {
        for (var i = 0; i < IntervalButtonBounds.Count; i++)
        {
            if (Contains(IntervalButtonBounds[i], mouse))
            {
                kind = IntervalOptions[i];
                return true;
            }
        }

        kind = SelectedInterval;
        return false;
    }

    public string? TooltipAt(Vector2i mouse)
    {
        foreach (var row in ConsumerRows.Concat(ProducerRows))
        {
            if (Contains(row.IconBounds, mouse))
            {
                return row.DisplayName;
            }
        }

        return null;
    }

    private static List<EnergyStatsLegendRow> BuildLegendRows(
        IReadOnlyList<EnergyStatsKindRow> rows,
        float left,
        float top,
        float columnWidth,
        bool isProducer)
    {
        var result = new List<EnergyStatsLegendRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var y = top + i * (RowHeight + 4f);
            var icon = new FloatRect(new Vector2f(left, y), new Vector2f(IconSize, IconSize));
            var color = new FloatRect(new Vector2f(left + IconSize + 6f, y + 2f), new Vector2f(ColorBarWidth, IconSize - 4f));
            var value = new FloatRect(
                new Vector2f(left + IconSize + ColorBarWidth + 14f, y),
                new Vector2f(Math.Max(40f, columnWidth - IconSize - ColorBarWidth - 20f), IconSize));
            result.Add(new EnergyStatsLegendRow(
                row.Kind,
                BuildBarModel.Glyph(row.Kind),
                DisplayName(row.Kind),
                ColorForKind(row.Kind, isProducer),
                row.AveragePerTick,
                icon,
                color,
                value));
        }

        return result;
    }

    private static bool Contains(FloatRect rect, Vector2i point) =>
        point.X >= rect.Left
        && point.Y >= rect.Top
        && point.X < rect.Left + rect.Width
        && point.Y < rect.Top + rect.Height;
}
