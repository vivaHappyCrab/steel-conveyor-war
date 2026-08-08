namespace SteelConveyorWar.Core;

public sealed class Inventory
{
    private readonly Dictionary<ItemId, int> _items = new();

    public IReadOnlyDictionary<ItemId, int> Items => _items.AsReadOnly();

    public int TotalStacks
    {
        get
        {
            return _items.Sum(pair => GetStackCount(pair.Key, pair.Value));
        }
    }

    public int Count(ItemId item)
    {
        return _items.GetValueOrDefault(item);
    }

    internal void Add(ItemId item, int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be non-negative.");
        }

        if (amount == 0)
        {
            return;
        }

        _items[item] = Count(item) + amount;
    }

    public bool Has(ItemId item, int amount)
    {
        return Count(item) >= amount;
    }

    public bool HasAll(IReadOnlyDictionary<ItemId, int> costs)
    {
        return costs.All(cost => Has(cost.Key, cost.Value));
    }

    public bool TryRemove(ItemId item, int amount)
    {
        if (!Has(item, amount))
        {
            return false;
        }

        var remaining = Count(item) - amount;
        if (remaining == 0)
        {
            _items.Remove(item);
        }
        else
        {
            _items[item] = remaining;
        }

        return true;
    }

    public bool TryRemoveAll(IReadOnlyDictionary<ItemId, int> costs)
    {
        if (!HasAll(costs))
        {
            return false;
        }

        foreach (var cost in costs)
        {
            TryRemove(cost.Key, cost.Value);
        }

        return true;
    }

    public bool TryTakeFirst(Func<ItemId, bool>? predicate, out ItemId item)
    {
        foreach (var pair in _items.OrderBy(pair => pair.Key))
        {
            if (pair.Value > 0 && (predicate is null || predicate(pair.Key)))
            {
                item = pair.Key;
                TryRemove(pair.Key, 1);
                return true;
            }
        }

        item = default;
        return false;
    }

    public bool TryAddWithinStackLimit(ItemId item, int amount)
    {
        var maxStack = MvpDefinitions.GetMaxStackSize(item);
        if (amount < 0 || Count(item) + amount > maxStack)
        {
            return false;
        }

        Add(item, amount);
        return true;
    }

    public bool TryAddWithinTotalStackLimit(ItemId item, int amount, int maxStacks)
    {
        if (amount < 0 || maxStacks <= 0)
        {
            return false;
        }

        var beforeStacks = GetStackCount(item, Count(item));
        var afterStacks = GetStackCount(item, Count(item) + amount);
        if (TotalStacks - beforeStacks + afterStacks > maxStacks)
        {
            return false;
        }

        Add(item, amount);
        return true;
    }

    internal void Clear()
    {
        _items.Clear();
    }

    private static int GetStackCount(ItemId item, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var maxStack = MvpDefinitions.GetMaxStackSize(item);
        return (amount + maxStack - 1) / maxStack;
    }
}
