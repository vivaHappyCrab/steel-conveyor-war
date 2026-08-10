using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class EnergyStatsOverlay
{
    internal static void DrawEnergyStatsOverlay(
        IRenderTarget target,
        EnergyStatsPanelModel panel,
        Font? font,
        Vector2i mousePosition)
    {
        using var backdrop = new RectangleShape(new Vector2f(panel.OverlayBounds.Width, panel.OverlayBounds.Height))
        {
            Position = new Vector2f(panel.OverlayBounds.Left, panel.OverlayBounds.Top),
            FillColor = new Color(12, 16, 24, 230),
            OutlineColor = new Color(110, 140, 180),
            OutlineThickness = 1f
        };
        target.Draw(backdrop);

        if (font is not null)
        {
            using var title = new Text(font, "Energy statistics", 14)
            {
                FillColor = new Color(230, 230, 210),
                Position = new Vector2f(panel.OverlayBounds.Left + 12f, panel.OverlayBounds.Top + 8f)
            };
            target.Draw(title);
        }

        UiPrimitives.DrawUiButton(target, font, panel.ExitButtonBounds, "Exit", new Color(70, 80, 100));

        for (var i = 0; i < panel.IntervalButtonBounds.Count; i++)
        {
            var kind = EnergyStatsPanelModel.IntervalOptions[i];
            var selected = kind == panel.SelectedInterval;
            UiPrimitives.DrawUiButton(
                target,
                font,
                panel.IntervalButtonBounds[i],
                EnergyStatsPanelModel.IntervalLabel(kind),
                selected ? new Color(70, 110, 160) : new Color(50, 58, 72));
        }

        if (font is not null)
        {
            using var consumeTitle = new Text(font, "Energy Consumption", 13)
            {
                FillColor = new Color(220, 220, 200),
                Position = new Vector2f(panel.ConsumeGraphBounds.Left, panel.ConsumeGraphBounds.Top - EnergyStatsPanelModel.GraphTitleGap)
            };
            target.Draw(consumeTitle);
            using var produceTitle = new Text(font, "Energy Production", 13)
            {
                FillColor = new Color(220, 220, 200),
                Position = new Vector2f(panel.ProduceGraphBounds.Left, panel.ProduceGraphBounds.Top - EnergyStatsPanelModel.GraphTitleGap)
            };
            target.Draw(produceTitle);
        }

        var sharedMax = 1f;
        foreach (var value in panel.Stats.DemandSeries)
        {
            sharedMax = Math.Max(sharedMax, value);
        }

        foreach (var value in panel.Stats.ProducedSeries)
        {
            sharedMax = Math.Max(sharedMax, value);
        }

        foreach (var row in panel.Stats.ConsumerRows)
        {
            foreach (var value in row.Series)
            {
                sharedMax = Math.Max(sharedMax, value);
            }
        }

        foreach (var row in panel.Stats.ProducerRows)
        {
            foreach (var value in row.Series)
            {
                sharedMax = Math.Max(sharedMax, value);
            }
        }

        var windowSeconds = (int)panel.SelectedInterval;
        DrawEnergyGraph(
            target,
            font,
            panel.ConsumeGraphBounds,
            panel.Stats.DemandSeries,
            panel.Stats.ConsumerRows.Select(row => (EnergyStatsPanelModel.ColorForKind(row.Kind, isProducer: false), row.Series)).ToArray(),
            sharedMax,
            windowSeconds);
        DrawEnergyGraph(
            target,
            font,
            panel.ProduceGraphBounds,
            panel.Stats.ProducedSeries,
            panel.Stats.ProducerRows.Select(row => (EnergyStatsPanelModel.ColorForKind(row.Kind, isProducer: true), row.Series)).ToArray(),
            sharedMax,
            windowSeconds);

        DrawEnergyLegendColumn(
            target,
            font,
            panel.ConsumerRows,
            panel.ConsumeTotalLabel,
            panel.ConsumeGraphBounds.Left,
            panel.ConsumeGraphBounds.Top + panel.ConsumeGraphBounds.Height + 12f);
        DrawEnergyLegendColumn(
            target,
            font,
            panel.ProducerRows,
            panel.ProduceTotalLabel,
            panel.ProduceGraphBounds.Left,
            panel.ProduceGraphBounds.Top + panel.ProduceGraphBounds.Height + 12f);

        var tip = panel.TooltipAt(mousePosition);
        if (tip is not null && font is not null)
        {
            using var tipText = new Text(font, tip, 13)
            {
                FillColor = Color.White,
                Position = new Vector2f(mousePosition.X + 14f, mousePosition.Y + 14f)
            };
            target.Draw(tipText);
        }
    }

    internal static void DrawEnergyGraph(
        IRenderTarget target,
        Font? font,
        FloatRect bounds,
        IReadOnlyList<float> totalSeries,
        (Color Color, IReadOnlyList<float> Series)[] kindSeries,
        float maxValue,
        int windowSeconds)
    {
        using var frame = new RectangleShape(new Vector2f(bounds.Width, bounds.Height))
        {
            Position = new Vector2f(bounds.Left, bounds.Top),
            FillColor = new Color(20, 26, 34, 220),
            OutlineColor = new Color(90, 110, 140),
            OutlineThickness = 1f
        };
        target.Draw(frame);

        var plot = new FloatRect(
            new Vector2f(bounds.Left + EnergyStatsPanelModel.AxisLeftPad, bounds.Top),
            new Vector2f(
                Math.Max(1f, bounds.Width - EnergyStatsPanelModel.AxisLeftPad),
                Math.Max(1f, bounds.Height - EnergyStatsPanelModel.AxisBottomPad)));

        using var plotFrame = new RectangleShape(new Vector2f(plot.Width, plot.Height))
        {
            Position = new Vector2f(plot.Left, plot.Top),
            FillColor = Color.Transparent,
            OutlineColor = new Color(90, 110, 140),
            OutlineThickness = 1f
        };
        target.Draw(plotFrame);

        if (font is not null)
        {
            var yLabels = new[] { maxValue, maxValue / 2f, 0f };
            var yPositions = new[] { plot.Top + 2f, plot.Top + plot.Height * 0.5f - 6f, plot.Top + plot.Height - 14f };
            for (var i = 0; i < yLabels.Length; i++)
            {
                using var label = new Text(font, FormatAxisValue(yLabels[i]), 11)
                {
                    FillColor = new Color(180, 190, 200),
                    Position = new Vector2f(bounds.Left + 2f, yPositions[i])
                };
                target.Draw(label);
            }

            var xLabels = new[] { "0", FormatEnergyWindowMid(windowSeconds), FormatEnergyWindowEnd(windowSeconds) };
            var xPositions = new[]
            {
                plot.Left,
                plot.Left + plot.Width * 0.5f - 12f,
                plot.Left + plot.Width - 28f
            };
            for (var i = 0; i < xLabels.Length; i++)
            {
                using var label = new Text(font, xLabels[i], 11)
                {
                    FillColor = new Color(180, 190, 200),
                    Position = new Vector2f(xPositions[i], plot.Top + plot.Height + 2f)
                };
                target.Draw(label);
            }
        }

        DrawEnergyPolyline(target, plot, totalSeries, maxValue, EnergyStatsPanelModel.TotalSeriesColor);
        foreach (var (color, series) in kindSeries)
        {
            DrawEnergyPolyline(target, plot, series, maxValue, color);
        }
    }

    internal static string FormatAxisValue(float value) =>
        value >= 10f || value == MathF.Floor(value) ? ((int)MathF.Round(value)).ToString() : value.ToString("0.#");

    internal static string FormatEnergyWindowEnd(int windowSeconds) =>
        windowSeconds >= 60 ? $"{windowSeconds / 60}m" : $"{windowSeconds}s";

    internal static string FormatEnergyWindowMid(int windowSeconds) =>
        FormatEnergyWindowEnd(Math.Max(1, windowSeconds / 2));

    internal static void DrawEnergyPolyline(
        IRenderTarget target,
        FloatRect bounds,
        IReadOnlyList<float> series,
        float maxValue,
        Color color)
    {
        if (series.Count < 2)
        {
            return;
        }

        var vertexArray = new VertexArray(PrimitiveType.LineStrip);
        for (var i = 0; i < series.Count; i++)
        {
            var x = bounds.Left + i / (float)(series.Count - 1) * bounds.Width;
            var y = bounds.Top + bounds.Height - series[i] / maxValue * (bounds.Height - 4f) - 2f;
            vertexArray.Append(new Vertex(new Vector2f(x, y), color));
        }

        target.Draw(vertexArray);
    }

    internal static void DrawEnergyLegendColumn(
        IRenderTarget target,
        Font? font,
        IReadOnlyList<EnergyStatsLegendRow> rows,
        string totalLabel,
        float totalLeft,
        float fallbackTop)
    {
        foreach (var row in rows)
        {
            using var icon = new RectangleShape(new Vector2f(row.IconBounds.Width, row.IconBounds.Height))
            {
                Position = new Vector2f(row.IconBounds.Left, row.IconBounds.Top),
                FillColor = new Color(40, 50, 65),
                OutlineColor = new Color(120, 140, 170),
                OutlineThickness = 1f
            };
            target.Draw(icon);

            if (font is not null)
            {
                using var glyph = new Text(font, row.Glyph, 12)
                {
                    FillColor = Color.White,
                    Position = new Vector2f(row.IconBounds.Left + 5f, row.IconBounds.Top + 2f)
                };
                target.Draw(glyph);
            }

            using var colorBar = new RectangleShape(new Vector2f(row.ColorBounds.Width, row.ColorBounds.Height))
            {
                Position = new Vector2f(row.ColorBounds.Left, row.ColorBounds.Top),
                FillColor = row.SeriesColor
            };
            target.Draw(colorBar);

            if (font is not null)
            {
                using var value = new Text(font, $"{row.AveragePerTick:0.##}/t", 12)
                {
                    FillColor = new Color(220, 230, 240),
                    Position = new Vector2f(row.ValueBounds.Left, row.ValueBounds.Top + 4f)
                };
                target.Draw(value);
            }
        }

        if (font is null)
        {
            return;
        }

        var totalY = rows.Count == 0
            ? fallbackTop
            : rows[^1].IconBounds.Top + rows[^1].IconBounds.Height + 10f;
        using var total = new Text(font, totalLabel, 13)
        {
            FillColor = EnergyStatsPanelModel.TotalSeriesColor,
            Position = new Vector2f(totalLeft, totalY)
        };
        target.Draw(total);
    }

}
