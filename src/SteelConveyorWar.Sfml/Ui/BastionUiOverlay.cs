using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class BastionUiOverlay
{
    internal static void ApplyBastionOrderCommand(
        GameSimulation simulation,
        int bastionId,
        BastionOrderCommand command,
        ref BastionPendingInputMode pendingMode,
        List<TilePosition> patrolWaypoints)
    {
        patrolWaypoints.Clear();
        switch (command)
        {
            case BastionOrderCommand.ActiveDefense:
                pendingMode = BastionPendingInputMode.None;
                simulation.TryIssueBastionOrder(bastionId, new BastionOrder(BastionOrderKind.Defend));
                break;
            case BastionOrderCommand.Patrol:
                pendingMode = BastionPendingInputMode.PatrolWaypoints;
                break;
            case BastionOrderCommand.Attack:
                pendingMode = BastionPendingInputMode.AttackTarget;
                break;
            case BastionOrderCommand.Scout:
                pendingMode = BastionPendingInputMode.ScoutTarget;
                break;
        }
    }

    internal static bool TryAdjustBastionTemplate(string key, GameSimulation simulation, WorldEntity bastion, int templateUnitIndex)
    {
        if (bastion.OwnerId is null)
        {
            return false;
        }

        var kinds = BastionCompositionPanelModel.UnlockedUnitKinds(simulation, bastion.OwnerId.Value);
        if (kinds.Length == 0)
        {
            return false;
        }

        var unitKind = kinds[Math.Clamp(templateUnitIndex, 0, kinds.Length - 1)];
        var current = bastion.BastionTemplate.GetValueOrDefault(unitKind);
        var delta = key switch
        {
            "Equal" or "Add" => 1,
            "Hyphen" or "Subtract" => -1,
            _ => 0
        };
        if (delta == 0)
        {
            return false;
        }

        simulation.TrySetBastionTemplate(bastion.Id, unitKind, Math.Max(0, current + delta));
        return true;
    }

    internal static void DrawBastionCompositionPanel(
        IRenderTarget target,
        GameSimulation simulation,
        WorldEntity bastion,
        int templateUnitIndex,
        Font? font,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        Vector2i mousePosition)
    {
        var slots = BastionCompositionPanelModel.BuildSlots(simulation, bastion);
        var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
        var bounds = BastionCompositionPanelModel.GetPanelBounds(
            windowWidth,
            windowHeight,
            panelX,
            bottomReserved,
            out var contentX,
            out var contentY);
        var exitBounds = BastionCompositionPanelModel.GetExitButtonBounds(bounds);
        using var backdrop = new RectangleShape(new Vector2f(bounds.Width, bounds.Height))
        {
            Position = new Vector2f(bounds.Left, bounds.Top),
            FillColor = new Color(10, 14, 20, 220),
            OutlineColor = new Color(110, 140, 180),
            OutlineThickness = 1f
        };
        target.Draw(backdrop);

        if (font is not null)
        {
            using var title = new Text(font, "Bastion composition")
            {
                CharacterSize = 14,
                FillColor = new Color(230, 230, 210),
                Position = new Vector2f(bounds.Left + BastionCompositionPanelModel.PanelPadding, bounds.Top + 8f)
            };
            target.Draw(title);
        }

        UiPrimitives.DrawUiButton(target, font, exitBounds, "Exit", new Color(70, 80, 100));

        string? tooltip = null;
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            var slotBounds = BastionCompositionPanelModel.GetSlotBounds(contentX, contentY, i);
            var selected = slots.Length > 0 && i == Math.Clamp(templateUnitIndex, 0, Math.Max(0, slots.Length - 1));
            using var frame = new RectangleShape(new Vector2f(slotBounds.Width, slotBounds.Height))
            {
                Position = new Vector2f(slotBounds.Left, slotBounds.Top),
                FillColor = selected ? new Color(60, 90, 130, 235) : new Color(40, 50, 65, 230),
                OutlineColor = selected ? new Color(255, 230, 120) : new Color(100, 120, 150),
                OutlineThickness = selected ? 2f : 1f
            };
            target.Draw(frame);

            if (font is not null)
            {
                using var glyph = new Text(font, slot.Glyph)
                {
                    CharacterSize = 20,
                    FillColor = Color.White,
                    Position = new Vector2f(slotBounds.Left + 34f, slotBounds.Top + 6f)
                };
                target.Draw(glyph);

                using var counts = new Text(font, BastionCompositionPanelModel.CountLabel(slot.LiveCount, slot.TemplateMax))
                {
                    CharacterSize = 13,
                    FillColor = new Color(220, 230, 240),
                    Position = new Vector2f(slotBounds.Left + 22f, slotBounds.Top + 30f)
                };
                target.Draw(counts);
            }

            DrawCompositionButton(
                target,
                font,
                BastionCompositionPanelModel.GetMinusButtonBounds(slotBounds),
                "-",
                mousePosition,
                ref tooltip,
                BastionCompositionPanelModel.Tooltip(slot) + " · -");
            DrawCompositionButton(
                target,
                font,
                BastionCompositionPanelModel.GetPlusButtonBounds(slotBounds),
                "+",
                mousePosition,
                ref tooltip,
                BastionCompositionPanelModel.Tooltip(slot) + " · +");
        }

        if (tooltip is not null && font is not null)
        {
            using var tip = new Text(font, tooltip)
            {
                CharacterSize = 13,
                FillColor = Color.White,
                Position = new Vector2f(bounds.Left, bounds.Top - 20f)
            };
            target.Draw(tip);
        }
    }

    internal static void DrawCompositionButton(
        IRenderTarget target,
        Font? font,
        FloatRect buttonBounds,
        string label,
        Vector2i mousePosition,
        ref string? tooltip,
        string hoverTooltip)
    {
        var hovered = mousePosition.X >= buttonBounds.Left
            && mousePosition.Y >= buttonBounds.Top
            && mousePosition.X < buttonBounds.Left + buttonBounds.Width
            && mousePosition.Y < buttonBounds.Top + buttonBounds.Height;
        using var button = new RectangleShape(new Vector2f(buttonBounds.Width, buttonBounds.Height))
        {
            Position = new Vector2f(buttonBounds.Left, buttonBounds.Top),
            FillColor = hovered ? new Color(90, 120, 160, 240) : new Color(55, 65, 80, 235),
            OutlineColor = new Color(140, 160, 190),
            OutlineThickness = 1f
        };
        target.Draw(button);

        if (font is not null)
        {
            using var text = new Text(font, label)
            {
                CharacterSize = 14,
                FillColor = Color.White,
                Position = new Vector2f(buttonBounds.Left + 6f, buttonBounds.Top + 1f)
            };
            target.Draw(text);
        }

        if (hovered)
        {
            tooltip = hoverTooltip;
        }
    }

    internal static bool TryHandleBastionPendingMapClick(
        GameSimulation simulation,
        WorldEntity? selectedEntity,
        PlayerId localPlayer,
        BastionPendingInputMode pendingMode,
        List<TilePosition> patrolWaypoints,
        TilePosition tile,
        bool confirmPatrol,
        out bool consumed)
    {
        consumed = false;
        if (pendingMode == BastionPendingInputMode.None
            || selectedEntity?.Kind != EntityKind.Bastion
            || selectedEntity.OwnerId != localPlayer)
        {
            return false;
        }

        if (pendingMode == BastionPendingInputMode.AttackTarget)
        {
            consumed = simulation.TryIssueBastionOrder(
                selectedEntity.Id,
                new BastionOrder(BastionOrderKind.AttackArea, tile));
            return true;
        }

        if (pendingMode == BastionPendingInputMode.ScoutTarget)
        {
            consumed = simulation.TryIssueBastionOrder(
                selectedEntity.Id,
                new BastionOrder(BastionOrderKind.Scout, tile));
            return true;
        }

        if (pendingMode == BastionPendingInputMode.PatrolWaypoints)
        {
            if (confirmPatrol)
            {
                if (patrolWaypoints.Count is >= 2 and <= 4)
                {
                    consumed = simulation.TryIssueBastionOrder(
                        selectedEntity.Id,
                        new BastionOrder(BastionOrderKind.Patrol, Waypoints: patrolWaypoints.ToArray()));
                }

                return true;
            }

            if (patrolWaypoints.Count < 4)
            {
                patrolWaypoints.Add(tile);
            }

            consumed = true;
            return true;
        }

        return false;
    }

    internal static FloatRect GetOrderBarBounds(uint windowWidth, uint windowHeight, float panelX, int slotCount, out float startX, out float barY)
    {
        var totalWidth = slotCount * SfmlUiLayout.BuildBarSlotSize;
        var available = Math.Max(SfmlUiLayout.BuildBarSlotSize, panelX - 16f);
        startX = Math.Max(8f, (available - totalWidth) * 0.5f);
        barY = windowHeight - SfmlUiLayout.BuildBarSlotSize - SfmlUiLayout.BuildBarBottomMargin;
        return new FloatRect(new Vector2f(startX, barY), new Vector2f(Math.Min(totalWidth, available), SfmlUiLayout.BuildBarSlotSize));
    }

    internal static bool TryPickBastionOrderCommand(
        Vector2i mousePosition,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        out BastionOrderCommand command)
    {
        command = default;
        var bounds = GetOrderBarBounds(
            windowWidth,
            windowHeight,
            panelX,
            BastionOrderBarModel.Commands.Length,
            out var startX,
            out var barY);
        if (mousePosition.X < bounds.Left
            || mousePosition.Y < bounds.Top
            || mousePosition.X >= bounds.Left + bounds.Width
            || mousePosition.Y >= bounds.Top + bounds.Height)
        {
            return false;
        }

        var index = (int)((mousePosition.X - startX) / SfmlUiLayout.BuildBarSlotSize);
        return BastionOrderBarModel.TryGetCommand(index, out command);
    }

    internal static void DrawBastionOrderBar(
        IRenderTarget target,
        WorldEntity bastion,
        BastionPendingInputMode pendingMode,
        Font? font,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        Vector2i mousePosition)
    {
        var bounds = GetOrderBarBounds(
            windowWidth,
            windowHeight,
            panelX,
            BastionOrderBarModel.Commands.Length,
            out var startX,
            out var barY);
        using var backdrop = new RectangleShape(new Vector2f(bounds.Width + 8f, bounds.Height + 8f))
        {
            Position = new Vector2f(bounds.Left - 4f, bounds.Top - 4f),
            FillColor = new Color(10, 14, 20, 210),
            OutlineColor = new Color(90, 110, 140),
            OutlineThickness = 1f
        };
        target.Draw(backdrop);

        string? tooltip = null;
        for (var i = 0; i < BastionOrderBarModel.Commands.Length; i++)
        {
            var command = BastionOrderBarModel.Commands[i];
            var slotX = startX + i * SfmlUiLayout.BuildBarSlotSize;
            var selected = BastionOrderBarModel.IsCommandHighlighted(command, pendingMode, bastion.Order.Kind);
            var fill = selected
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
                using var glyph = new Text(font, BastionOrderBarModel.Glyph(command))
                {
                    CharacterSize = 18,
                    FillColor = Color.White,
                    Position = new Vector2f(slotX + 14f, barY + 10f)
                };
                target.Draw(glyph);

                var badge = BastionOrderBarModel.ShortcutBadge(command);
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
                tooltip = BastionOrderBarModel.Label(command);
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
