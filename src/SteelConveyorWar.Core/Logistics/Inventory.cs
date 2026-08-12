namespace SteelConveyorWar.Core;

public sealed class Inventory
{
    private readonly Dictionary<ItemId, int> _items = new();

    /// <summary>
    /// Monotonic counter bumped on every mutating add/remove/clear. Used by factory idle-skip
    /// (R17) to detect input changes without scanning item contents each tick.
    /// </summary>
    public int MutationVersion { get; private set; }

    public IReadOnlyDictionary<ItemId, int> Items => _items.AsReadOnly();

    public int GetTotalStacks(GameplayTablesCatalog tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return _items.Sum(pair => GetStackCount(pair.Key, pair.Value, tables));
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
        MutationVersion++;
    }

    public bool Has(ItemId item, int amount)
    {
        return Count(item) >= amount;
    }

    public bool HasAll(IReadOnlyDictionary<ItemId, int> costs)
    {
        return costs.All(cost => Has(cost.Key, cost.Value));
    }

    /// <summary>How many full cost sets this inventory can pay (0 if any required item is missing).</summary>
    public int AffordableSets(IReadOnlyDictionary<ItemId, int> costs)
    {
        if (costs.Count == 0)
        {
            return 0;
        }

        var affordable = int.MaxValue;
        foreach (var cost in costs)
        {
            if (cost.Value <= 0)
            {
                continue;
            }

            affordable = Math.Min(affordable, Count(cost.Key) / cost.Value);
        }

        return affordable == int.MaxValue ? 0 : affordable;
    }

    internal bool TryRemove(ItemId item, int amount)
    {
        // R28: reject negative amounts. Otherwise Has() returns true and
        // remaining = Count - (negative) would *increase* the stored quantity.
        if (amount < 0)
        {
            return false;
        }

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

        MutationVersion++;
        return true;
    }

    internal bool TryRemoveAll(IReadOnlyDictionary<ItemId, int> costs)
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

    internal bool TryTakeFirst(Func<ItemId, bool>? predicate, out ItemId item)
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

    internal bool TryAddWithinStackLimit(ItemId item, int amount, GameplayTablesCatalog tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var maxStack = tables.GetMaxStackSize(item);
        if (amount < 0 || Count(item) + amount > maxStack)
        {
            return false;
        }

        Add(item, amount);
        return true;
    }

    internal bool TryAddWithinTotalStackLimit(ItemId item, int amount, int maxStacks, GameplayTablesCatalog tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (amount < 0 || maxStacks <= 0)
        {
            return false;
        }

        var beforeStacks = GetStackCount(item, Count(item), tables);
        var afterStacks = GetStackCount(item, Count(item) + amount, tables);
        if (GetTotalStacks(tables) - beforeStacks + afterStacks > maxStacks)
        {
            return false;
        }

        Add(item, amount);
        return true;
    }

    internal void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        MutationVersion++;
    }

    private static int GetStackCount(ItemId item, int amount, GameplayTablesCatalog tables)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var maxStack = tables.GetMaxStackSize(item);
        return (amount + maxStack - 1) / maxStack;
    }
}
