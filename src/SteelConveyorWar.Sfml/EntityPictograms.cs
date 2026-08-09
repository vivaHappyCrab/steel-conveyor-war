using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class EntityPictograms
{
    public static void DrawBuilding(IRenderTarget target, EntityKind kind, float left, float top, float width, float height, Color ink)
    {
        var cx = left + width / 2f;
        var cy = top + height / 2f;
        var s = Math.Min(width, height);

        switch (kind)
        {
            case EntityKind.Bastion:
                DrawDiamond(target, cx, cy, s * 0.35f, ink);
                break;
            case EntityKind.Hub:
                DrawRectOutline(target, cx - s * 0.25f, cy - s * 0.25f, s * 0.5f, s * 0.5f, ink);
                DrawLine(target, cx - s * 0.25f, cy, cx + s * 0.25f, cy, ink);
                break;
            case EntityKind.Mine:
            case EntityKind.CoalMine:
            case EntityKind.OilWell:
                DrawLine(target, cx - s * 0.3f, cy - s * 0.2f, cx, cy + s * 0.3f, ink);
                DrawLine(target, cx + s * 0.3f, cy - s * 0.2f, cx, cy + s * 0.3f, ink);
                break;
            case EntityKind.Smelter:
            case EntityKind.Refinery:
                DrawLine(target, cx, cy + s * 0.3f, cx - s * 0.25f, cy - s * 0.1f, ink);
                DrawLine(target, cx, cy + s * 0.3f, cx + s * 0.25f, cy - s * 0.1f, ink);
                DrawLine(target, cx - s * 0.15f, cy - s * 0.25f, cx + s * 0.15f, cy - s * 0.25f, ink);
                break;
            case EntityKind.SolarPanel:
                DrawLine(target, cx - s * 0.3f, cy, cx + s * 0.3f, cy, ink);
                DrawLine(target, cx, cy - s * 0.3f, cx, cy + s * 0.3f, ink);
                DrawLine(target, cx - s * 0.2f, cy - s * 0.2f, cx + s * 0.2f, cy + s * 0.2f, ink);
                DrawLine(target, cx - s * 0.2f, cy + s * 0.2f, cx + s * 0.2f, cy - s * 0.2f, ink);
                break;
            case EntityKind.CoalPlant:
                DrawRectOutline(target, cx - s * 0.2f, cy - s * 0.05f, s * 0.4f, s * 0.3f, ink);
                DrawLine(target, cx, cy - s * 0.05f, cx, cy - s * 0.3f, ink);
                break;
            case EntityKind.Assembler:
                DrawCircleOutline(target, cx, cy, s * 0.22f, ink);
                DrawLine(target, cx, cy - s * 0.22f, cx, cy + s * 0.22f, ink);
                DrawLine(target, cx - s * 0.22f, cy, cx + s * 0.22f, cy, ink);
                break;
            case EntityKind.TankFactory:
            case EntityKind.DroneCenter:
                DrawCircleOutline(target, cx, cy, s * 0.28f, ink);
                DrawCircleOutline(target, cx, cy, s * 0.12f, ink);
                break;
            case EntityKind.Laboratory:
                DrawLine(target, cx - s * 0.15f, cy + s * 0.3f, cx - s * 0.15f, cy - s * 0.1f, ink);
                DrawLine(target, cx + s * 0.15f, cy + s * 0.3f, cx + s * 0.15f, cy - s * 0.1f, ink);
                DrawLine(target, cx - s * 0.15f, cy - s * 0.1f, cx + s * 0.15f, cy - s * 0.1f, ink);
                DrawCircleOutline(target, cx, cy - s * 0.22f, s * 0.1f, ink);
                break;
            case EntityKind.Wall:
            case EntityKind.SteelWall:
                DrawLine(target, cx - s * 0.3f, cy - s * 0.15f, cx + s * 0.3f, cy - s * 0.15f, ink);
                DrawLine(target, cx - s * 0.3f, cy, cx + s * 0.3f, cy, ink);
                DrawLine(target, cx - s * 0.3f, cy + s * 0.15f, cx + s * 0.3f, cy + s * 0.15f, ink);
                break;
            case EntityKind.MachineGunTurret:
            case EntityKind.CannonTurret:
            case EntityKind.AntiAirTurret:
                DrawCircleOutline(target, cx, cy + s * 0.05f, s * 0.12f, ink);
                DrawLine(target, cx, cy + s * 0.05f, cx, cy - s * 0.3f, ink);
                break;
            default:
                DrawRectOutline(target, cx - s * 0.2f, cy - s * 0.2f, s * 0.4f, s * 0.4f, ink);
                break;
        }
    }

    public static void DrawUnitMark(IRenderTarget target, EntityKind kind, Vector2f center, float size, Color ink)
    {
        switch (kind)
        {
            case EntityKind.Commander:
                DrawLine(target, center.X - size * 0.35f, center.Y, center.X + size * 0.35f, center.Y, ink);
                DrawLine(target, center.X, center.Y - size * 0.35f, center.X, center.Y + size * 0.35f, ink);
                break;
            case EntityKind.Scout:
                DrawLine(target, center.X, center.Y - size * 0.25f, center.X, center.Y + size * 0.15f, ink);
                break;
            case EntityKind.LightBot:
            case EntityKind.MediumBot:
            case EntityKind.AntiAirBot:
                DrawLine(target, center.X - size * 0.25f, center.Y + size * 0.15f, center.X + size * 0.25f, center.Y + size * 0.15f, ink);
                DrawLine(target, center.X, center.Y - size * 0.2f, center.X, center.Y + size * 0.15f, ink);
                break;
            case EntityKind.BasicTank:
            case EntityKind.MediumTank:
                DrawRectOutline(target, center.X - size * 0.28f, center.Y - size * 0.18f, size * 0.56f, size * 0.36f, ink);
                DrawLine(target, center.X, center.Y, center.X + size * 0.3f, center.Y, ink);
                break;
            case EntityKind.RocketLauncher:
                DrawLine(target, center.X - size * 0.1f, center.Y + size * 0.25f, center.X - size * 0.1f, center.Y - size * 0.25f, ink);
                DrawLine(target, center.X + size * 0.1f, center.Y + size * 0.25f, center.X + size * 0.1f, center.Y - size * 0.25f, ink);
                break;
        }
    }

    private static void DrawLine(IRenderTarget target, float x1, float y1, float x2, float y2, Color color)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.01f)
        {
            return;
        }

        using var line = new RectangleShape(new Vector2f(length, 2f))
        {
            FillColor = color,
            Origin = new Vector2f(0f, 1f),
            Position = new Vector2f(x1, y1),
            Rotation = MathF.Atan2(dy, dx) * 180f / MathF.PI
        };
        target.Draw(line);
    }

    private static void DrawRectOutline(IRenderTarget target, float x, float y, float w, float h, Color color)
    {
        using var rect = new RectangleShape(new Vector2f(w, h))
        {
            Position = new Vector2f(x, y),
            FillColor = Color.Transparent,
            OutlineColor = color,
            OutlineThickness = 1.5f
        };
        target.Draw(rect);
    }

    private static void DrawCircleOutline(IRenderTarget target, float cx, float cy, float radius, Color color)
    {
        using var circle = new CircleShape(radius)
        {
            Position = new Vector2f(cx, cy),
            Origin = new Vector2f(radius, radius),
            FillColor = Color.Transparent,
            OutlineColor = color,
            OutlineThickness = 1.5f
        };
        target.Draw(circle);
    }

    private static void DrawDiamond(IRenderTarget target, float cx, float cy, float radius, Color color)
    {
        using var diamond = new ConvexShape(4)
        {
            FillColor = Color.Transparent,
            OutlineColor = color,
            OutlineThickness = 1.5f,
            Position = new Vector2f(cx, cy)
        };
        diamond.SetPoint(0, new Vector2f(0, -radius));
        diamond.SetPoint(1, new Vector2f(radius, 0));
        diamond.SetPoint(2, new Vector2f(0, radius));
        diamond.SetPoint(3, new Vector2f(-radius, 0));
        target.Draw(diamond);
    }
}
