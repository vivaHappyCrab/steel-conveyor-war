using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class UiPrimitives
{
    internal static void DrawUiButton(IRenderTarget target, Font? font, FloatRect bounds, string label, Color fill)
    {
        using var button = new RectangleShape(new Vector2f(bounds.Width, bounds.Height))
        {
            Position = new Vector2f(bounds.Left, bounds.Top),
            FillColor = fill,
            OutlineColor = new Color(140, 160, 190),
            OutlineThickness = 1f
        };
        target.Draw(button);
        if (font is null)
        {
            return;
        }

        using var text = new Text(font, label, 12)
        {
            FillColor = Color.White,
            Position = new Vector2f(bounds.Left + 8f, bounds.Top + 5f)
        };
        target.Draw(text);
    }

}
