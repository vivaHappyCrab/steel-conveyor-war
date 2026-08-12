using System.Collections.Frozen;
using System.Collections.Immutable;

namespace SteelConveyorWar.Core;

/// <summary>
/// H07: deep-freeze helpers for authoritative content catalogs so public
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> / <see cref="IReadOnlyList{T}"/> graphs
/// cannot be cast-mutated after construction.
/// </summary>
internal static class ContentFreeze
{
    public static IReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> source,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        if (source is FrozenDictionary<TKey, TValue> frozen)
        {
            return frozen;
        }

        return comparer is null
            ? source.ToFrozenDictionary()
            : source.ToFrozenDictionary(comparer);
    }

    public static IReadOnlyDictionary<TKey, IReadOnlyDictionary<TInnerKey, TInnerValue>> NestedDictionary<TKey, TInnerKey, TInnerValue>(
        IReadOnlyDictionary<TKey, IReadOnlyDictionary<TInnerKey, TInnerValue>> source,
        IEqualityComparer<TKey>? keyComparer = null,
        IEqualityComparer<TInnerKey>? innerComparer = null)
        where TKey : notnull
        where TInnerKey : notnull
    {
        if (source is FrozenDictionary<TKey, IReadOnlyDictionary<TInnerKey, TInnerValue>> frozen
            && AreAllFrozen(frozen.Values))
        {
            return frozen;
        }

        return keyComparer is null
            ? source.ToFrozenDictionary(
                pair => pair.Key,
                pair => Dictionary(pair.Value, innerComparer))
            : source.ToFrozenDictionary(
                pair => pair.Key,
                pair => Dictionary(pair.Value, innerComparer),
                keyComparer);
    }

    public static IReadOnlyList<T> List<T>(IEnumerable<T> source)
    {
        if (source is ImmutableArray<T> immutable)
        {
            return immutable;
        }

        return source.ToImmutableArray();
    }

    private static bool AreAllFrozen<TInnerKey, TInnerValue>(
        IEnumerable<IReadOnlyDictionary<TInnerKey, TInnerValue>> values)
        where TInnerKey : notnull
    {
        foreach (var value in values)
        {
            if (value is not FrozenDictionary<TInnerKey, TInnerValue>)
            {
                return false;
            }
        }

        return true;
    }
}
