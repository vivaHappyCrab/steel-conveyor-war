namespace SteelConveyorWar.Core;

public sealed class ConveyorItem
{
    public ConveyorItem(ItemId item, int progressTicks = 0)
    {
        Item = item;
        ProgressTicks = progressTicks;
    }

    public ItemId Item { get; }

    public int ProgressTicks { get; set; }
}
