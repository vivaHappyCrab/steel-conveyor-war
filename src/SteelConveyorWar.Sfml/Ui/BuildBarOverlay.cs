using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class BuildBarOverlay
{
    internal static FloatRect GetBuildBarBounds(uint windowWidth, uint windowHeight, float panelX, out float startX, out float barY)
    {
        return BastionUiOverlay.GetOrderBarBounds(windowWidth, windowHeight, panelX, BuildMenuCatalog.BuildableKinds.Length, out startX, out barY);
    }

    internal static bool TryPickBuildBarKind(Vector2i mousePosition, uint windowWidth, uint windowHeight, float panelX, out EntityKind kind)
    {
        kind = default;
        var bounds = GetBuildBarBounds(windowWidth, windowHeight, panelX, out var startX, out var barY);
        if (mousePosition.X < bounds.Position.X
            || mousePosition.Y < bounds.Position.Y
            || mousePosition.X >= bounds.Position.X + bounds.Size.X
            || mousePosition.Y >= bounds.Position.Y + bounds.Size.Y)
        {
            return false;
        }

        var index = (int)((mousePosition.X - startX) / SfmlUiLayout.BuildBarSlotSize);
        if (index < 0 || index >= BuildMenuCatalog.BuildableKinds.Length)
        {
            return false;
        }

        kind = BuildMenuCatalog.BuildableKinds[index];
        return true;
    }

    internal static void DrawBuildBar(
        IRenderTarget target,
        GameSimulation simulation,
        PlayerId localPlayer,
        int? selectedEntityId,
        EntityKind? pendingBuildKind,
        Direction pendingDirection,
        ItemRecipeId? pendingRecipe,
        Font? font,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        Vector2i mousePosition)
    {
        var commander = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
        if (commander?.Kind != EntityKind.Commander || commander.OwnerId != localPlayer)
        {
            commander = simulation.World.Entities.FirstOrDefault(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Commander && entity.IsAlive);
        }

        var bounds = GetBuildBarBounds(windowWidth, windowHeight, panelX, out var startX, out var barY);
        using var backdrop = new RectangleShape(new Vector2f(bounds.Size.X + 8f, bounds.Size.Y + 8f))
        {
            Position = new Vector2f(bounds.Position.X - 4f, bounds.Position.Y - 4f),
            FillColor = new Color(10, 14, 20, 210),
            OutlineColor = new Color(90, 110, 140),
            OutlineThickness = 1f
        };
        target.Draw(backdrop);

        string? tooltip = null;
        for (var i = 0; i < BuildMenuCatalog.BuildableKinds.Length; i++)
        {
            var kind = BuildMenuCatalog.BuildableKinds[i];
            var slotX = startX + i * SfmlUiLayout.BuildBarSlotSize;
            var affordable = BuildBarModel.AffordableBuilds(simulation, commander, kind);
            var selected = pendingBuildKind == kind;
            var fill = affordable <= 0
                ? new Color(35, 40, 48, 220)
                : selected
                    ? new Color(70, 110, 160, 235)
                    : new Color(45, 55, 70, 230);

            using var slot = new RectangleShape(new Vector2f(SfmlUiLayout.BuildBarSlotSize - 4f, SfmlUiLayout.BuildBarSlotSize - 4f))
            {
                Position = new Vector2f(slotX + 2f, barY + 2f),
                FillColor = fill,
                OutlineColor = selected ? new Color(255, 230, 120) : new Color(100, 120, 150),
                OutlineThickness = selected ? 2f : 1f
            };
            target.Draw(slot);

            if (font is not null)
            {
                using var glyph = new Text(font, BuildBarModel.Glyph(kind))
                {
                    CharacterSize = 18,
                    FillColor = affordable <= 0 ? new Color(110, 110, 120) : Color.White,
                    Position = new Vector2f(slotX + 14f, barY + 10f)
                };
                target.Draw(glyph);

                using var afford = new Text(font, BuildBarModel.AffordLabel(affordable))
                {
                    CharacterSize = 11,
                    FillColor = affordable <= 0 ? new Color(160, 80, 80) : new Color(200, 220, 180),
                    Position = new Vector2f(slotX + 4f, barY + SfmlUiLayout.BuildBarSlotSize - 18f)
                };
                target.Draw(afford);

                var badge = BuildBarModel.ShortcutBadge(i);
                if (badge is not null)
                {
                    using var keyBadge = new Text(font, badge)
                    {
                        CharacterSize = 11,
                        FillColor = new Color(230, 230, 200),
                        Position = new Vector2f(slotX + SfmlUiLayout.BuildBarSlotSize - 16f, barY + 2f)
                    };
                    target.Draw(keyBadge);
                }
            }

            if (mousePosition.X >= slotX
                && mousePosition.X < slotX + SfmlUiLayout.BuildBarSlotSize
                && mousePosition.Y >= barY
                && mousePosition.Y < barY + SfmlUiLayout.BuildBarSlotSize)
            {
                tooltip = BuildBarModel.Tooltip(
                    kind,
                    affordable,
                    selected && BuildBarModel.IsDirectedKind(kind) ? pendingDirection : Direction.East,
                    selected && kind == EntityKind.Assembler ? pendingRecipe : null);
            }
        }

        if (tooltip is not null && font is not null)
        {
            using var tip = new Text(font, tooltip)
            {
                CharacterSize = 13,
                FillColor = Color.White,
                Position = new Vector2f(bounds.Left, barY - 22f)
            };
            target.Draw(tip);
        }
    }
}
