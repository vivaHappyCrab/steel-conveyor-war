namespace SteelConveyorWar.Core;

/// <summary>
/// Emptiest-first without floating division: compare <c>a.buf/a.cap</c> vs <c>b.buf/b.cap</c>
/// via cross-multiply, then entity id. See ADR 0001.
/// </summary>
public sealed class EnergyFillRatioComparer : IComparer<WorldEntity>
{
    public static readonly EnergyFillRatioComparer Instance = new();

    public int Compare(WorldEntity? x, WorldEntity? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return CompareRatios(
            x.EnergyBuffer,
            x.EnergyBufferCapacity,
            x.Id,
            y.EnergyBuffer,
            y.EnergyBufferCapacity,
            y.Id);
    }

    public static int CompareRatios(int bufferA, int capacityA, int idA, int bufferB, int capacityB, int idB)
    {
        var left = (long)bufferA * capacityB;
        var right = (long)bufferB * capacityA;
        var cmp = left.CompareTo(right);
        return cmp != 0 ? cmp : idA.CompareTo(idB);
    }
}
