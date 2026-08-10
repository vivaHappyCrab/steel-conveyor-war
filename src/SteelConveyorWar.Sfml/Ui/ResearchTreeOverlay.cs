using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class ResearchTreeOverlay
{
    internal static void DrawResearchTreeOverlay(
        RenderWindow window,
        ResearchTreePanelModel tree,
        Font? font,
        float scrollY,
        uint windowWidth,
        uint windowHeight)
    {
        using var backdrop = new RectangleShape(new Vector2f(tree.OverlayBounds.Width, tree.OverlayBounds.Height))
        {
            Position = new Vector2f(tree.OverlayBounds.Left, tree.OverlayBounds.Top),
            FillColor = new Color(8, 12, 18, 230),
            OutlineColor = new Color(100, 120, 150),
            OutlineThickness = 1f
        };
        window.Draw(backdrop);

        if (font is not null)
        {
            using var title = new Text(font, $"Исследования [{tree.ProfileId}] · {tree.CurrentTierId}", 14)
            {
                FillColor = new Color(220, 230, 240),
                Position = new Vector2f(tree.OverlayBounds.Left + 10f, tree.OverlayBounds.Top + 6f)
            };
            window.Draw(title);
        }

        var viewport = tree.ContentViewport;
        using (var contentView = new View(new FloatRect(
                   new Vector2f(0f, scrollY),
                   new Vector2f(Math.Max(1f, viewport.Width), Math.Max(1f, viewport.Height)))))
        {
            contentView.Viewport = new FloatRect(
                new Vector2f(viewport.Left / windowWidth, viewport.Top / windowHeight),
                new Vector2f(viewport.Width / windowWidth, viewport.Height / windowHeight));
            window.SetView(contentView);

            var busColor = new Color(140, 160, 190);
            foreach (var edge in tree.Edges)
            {
                var line = new[]
                {
                    new Vertex(edge.From, busColor),
                    new Vertex(edge.To, busColor)
                };
                window.Draw(line, PrimitiveType.Lines);
            }

            foreach (var node in tree.Nodes)
            {
                var fill = node.Status switch
                {
                    ResearchTreeNodeStatus.Completed => new Color(50, 110, 210),
                    ResearchTreeNodeStatus.Available => new Color(50, 170, 80),
                    ResearchTreeNodeStatus.Active => new Color(70, 190, 210),
                    _ => new Color(180, 55, 55)
                };
                var outline = tree.SelectedId == node.Id
                    ? Color.White
                    : node.IsMandatory
                        ? new Color(220, 200, 120)
                        : new Color(20, 20, 24);
                using var icon = new RectangleShape(new Vector2f(node.Bounds.Width, node.Bounds.Height))
                {
                    Position = new Vector2f(node.Bounds.Left, node.Bounds.Top),
                    FillColor = fill,
                    OutlineColor = outline,
                    OutlineThickness = tree.SelectedId == node.Id ? 2f : 1f
                };
                window.Draw(icon);

                if (font is not null)
                {
                    using var glyph = new Text(font, node.Symbol, 14)
                    {
                        FillColor = Color.White,
                        Position = new Vector2f(node.Bounds.Left + 11f, node.Bounds.Top + 8f)
                    };
                    window.Draw(glyph);

                    var label = node.DisplayName.Length > 16 ? node.DisplayName[..15] + "…" : node.DisplayName;
                    using var name = new Text(font, label, 10)
                    {
                        FillColor = new Color(210, 220, 230),
                        Position = new Vector2f(node.Bounds.Left - 2f, node.Bounds.Top + node.Bounds.Height + 1f)
                    };
                    window.Draw(name);
                }
            }
        }

        window.SetView(window.DefaultView);

        UiPrimitives.DrawUiButton(window, font, tree.ExitButtonBounds, "Exit", new Color(70, 80, 100));
        var actionEnabled = tree.CanStartSelected || tree.CanCancelSelected;
        UiPrimitives.DrawUiButton(
            window,
            font,
            tree.ActionButtonBounds,
            tree.ActionButtonLabel,
            actionEnabled ? new Color(60, 120, 80) : new Color(50, 55, 65));
        if (tree.SupportsAllocationToggle)
        {
            UiPrimitives.DrawUiButton(window, font, tree.AllocationButtonBounds, "Alloc cycle/tact", new Color(70, 90, 130));
        }
    }

}
