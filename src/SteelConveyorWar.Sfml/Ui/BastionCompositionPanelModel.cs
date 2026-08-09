using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public readonly record struct BastionCompositionSlot(
    EntityKind UnitKind,
    int LiveCount,
    int TemplateMax,
    string Glyph);

public enum BastionCompositionAdjust
{
    Decrement = -1,
    Increment = 1
}

public static class BastionCompositionPanelModel
{
    public const float PanelPadding = 12f;
    public const float SlotWidth = 88f;
    public const float SlotHeight = 72f;
    public const float SlotGap = 8f;
    public const float ButtonSize = 22f;
    public const int SlotsPerRow = 4;

    public static EntityKind[] UnlockedUnitKinds(GameSimulation simulation, PlayerId ownerId)
    {
        return BastionOrderBarModel.TemplateUnitKinds
            .Where(kind => simulation.IsUnitProductionUnlocked(ownerId, kind))
            .ToArray();
    }

    public static BastionCompositionSlot[] BuildSlots(GameSimulation simulation, WorldEntity bastion)
    {
        if (bastion.Kind != EntityKind.Bastion || bastion.OwnerId is null)
        {
            return [];
        }

        var ownerId = bastion.OwnerId.Value;
        return UnlockedUnitKinds(simulation, ownerId)
            .Select(kind => new BastionCompositionSlot(
                kind,
                simulation.GetBastionUnitSupply(bastion.Id, kind),
                bastion.BastionTemplate.GetValueOrDefault(kind),
                Glyph(kind)))
            .ToArray();
    }

    public static string Glyph(EntityKind unitKind)
    {
        return unitKind switch
        {
            EntityKind.BasicTank => "T",
            EntityKind.LightBot => "L",
            EntityKind.Scout => "S",
            EntityKind.MediumTank => "M",
            EntityKind.MediumBot => "B",
            EntityKind.AntiAirBot => "A",
            EntityKind.RocketLauncher => "R",
            _ => BuildBarModel.Glyph(unitKind)
        };
    }

    public static string CountLabel(int liveCount, int templateMax) => $"{liveCount}/{templateMax}";

    public static string Tooltip(BastionCompositionSlot slot) =>
        $"{slot.UnitKind} · live {slot.LiveCount} · max {slot.TemplateMax}";

    public static FloatRect GetPanelBounds(
        uint windowWidth,
        uint windowHeight,
        float panelX,
        int slotCount,
        out float contentX,
        out float contentY)
    {
        var rows = Math.Max(1, (int)Math.Ceiling(slotCount / (double)SlotsPerRow));
        var columns = Math.Min(SlotsPerRow, Math.Max(1, slotCount));
        var contentWidth = columns * SlotWidth + Math.Max(0, columns - 1) * SlotGap;
        var contentHeight = rows * SlotHeight + Math.Max(0, rows - 1) * SlotGap;
        var width = contentWidth + PanelPadding * 2f;
        var height = contentHeight + PanelPadding * 2f + 22f;
        var playfieldWidth = Math.Max(1f, panelX);
        var left = Math.Max(8f, (playfieldWidth - width) * 0.5f);
        var top = Math.Max(TopBarClearance(), (windowHeight - height) * 0.5f - 20f);
        contentX = left + PanelPadding;
        contentY = top + PanelPadding + 20f;
        return new FloatRect(new Vector2f(left, top), new Vector2f(width, height));
    }

    public static FloatRect GetSlotBounds(float contentX, float contentY, int slotIndex)
    {
        var row = slotIndex / SlotsPerRow;
        var col = slotIndex % SlotsPerRow;
        var x = contentX + col * (SlotWidth + SlotGap);
        var y = contentY + row * (SlotHeight + SlotGap);
        return new FloatRect(new Vector2f(x, y), new Vector2f(SlotWidth, SlotHeight));
    }

    public static FloatRect GetMinusButtonBounds(FloatRect slotBounds)
    {
        return new FloatRect(
            new Vector2f(slotBounds.Left + 8f, slotBounds.Top + slotBounds.Height - ButtonSize - 8f),
            new Vector2f(ButtonSize, ButtonSize));
    }

    public static FloatRect GetPlusButtonBounds(FloatRect slotBounds)
    {
        return new FloatRect(
            new Vector2f(slotBounds.Left + slotBounds.Width - ButtonSize - 8f, slotBounds.Top + slotBounds.Height - ButtonSize - 8f),
            new Vector2f(ButtonSize, ButtonSize));
    }

    public static bool TryPickAdjust(
        Vector2i mousePosition,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        BastionCompositionSlot[] slots,
        out int slotIndex,
        out BastionCompositionAdjust adjust)
    {
        slotIndex = -1;
        adjust = default;
        if (slots.Length == 0)
        {
            return false;
        }

        var bounds = GetPanelBounds(windowWidth, windowHeight, panelX, slots.Length, out var contentX, out var contentY);
        if (!Contains(bounds, mousePosition))
        {
            return false;
        }

        for (var i = 0; i < slots.Length; i++)
        {
            var slotBounds = GetSlotBounds(contentX, contentY, i);
            var minus = GetMinusButtonBounds(slotBounds);
            if (Contains(minus, mousePosition))
            {
                slotIndex = i;
                adjust = BastionCompositionAdjust.Decrement;
                return true;
            }

            var plus = GetPlusButtonBounds(slotBounds);
            if (Contains(plus, mousePosition))
            {
                slotIndex = i;
                adjust = BastionCompositionAdjust.Increment;
                return true;
            }
        }

        return false;
    }

    public static bool ContainsPanel(
        Vector2i mousePosition,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        int slotCount)
    {
        if (slotCount <= 0)
        {
            return false;
        }

        var bounds = GetPanelBounds(windowWidth, windowHeight, panelX, slotCount, out _, out _);
        return Contains(bounds, mousePosition);
    }

    private static float TopBarClearance() => 48f;

    private static bool Contains(FloatRect rect, Vector2i point)
    {
        return point.X >= rect.Left
            && point.Y >= rect.Top
            && point.X < rect.Left + rect.Width
            && point.Y < rect.Top + rect.Height;
    }
}
