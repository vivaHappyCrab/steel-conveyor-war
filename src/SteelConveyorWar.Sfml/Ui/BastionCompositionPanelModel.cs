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
        float bottomReserved,
        out float contentX,
        out float contentY)
    {
        var bounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
        contentX = bounds.Left + PanelPadding;
        contentY = bounds.Top + ResearchTreePanelModel.TitleChromeHeight + PanelPadding;
        return bounds;
    }

    /// <summary>Legacy overload used by hit-tests that already know bottom reserved via caller defaults.</summary>
    public static FloatRect GetPanelBounds(
        uint windowWidth,
        uint windowHeight,
        float panelX,
        int slotCount,
        out float contentX,
        out float contentY)
    {
        _ = slotCount;
        const float bottomReserved = 48f + 10f + 8f;
        return GetPanelBounds(windowWidth, windowHeight, panelX, bottomReserved, out contentX, out contentY);
    }

    public static FloatRect GetExitButtonBounds(FloatRect overlayBounds) =>
        new(
            new Vector2f(overlayBounds.Left + overlayBounds.Width - 78f, overlayBounds.Top + 6f),
            new Vector2f(70f, ResearchTreePanelModel.ButtonHeight));

    public static bool HitExit(
        Vector2i mousePosition,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        float bottomReserved)
    {
        var bounds = GetPanelBounds(windowWidth, windowHeight, panelX, bottomReserved, out _, out _);
        return Contains(GetExitButtonBounds(bounds), mousePosition);
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
        _ = slotCount;
        var bounds = GetPanelBounds(windowWidth, windowHeight, panelX, 0, out _, out _);
        return Contains(bounds, mousePosition);
    }

    private static bool Contains(FloatRect rect, Vector2i point)
    {
        return point.X >= rect.Left
            && point.Y >= rect.Top
            && point.X < rect.Left + rect.Width
            && point.Y < rect.Top + rect.Height;
    }
}
