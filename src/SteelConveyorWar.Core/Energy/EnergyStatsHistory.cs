namespace SteelConveyorWar.Core;

/// <summary>
/// Presentation-only per-player energy sample ring (not hashed / not gameplay-affecting).
/// Capacity covers 5 minutes at <see cref="GameSimulation.TicksPerSecond"/>.
/// </summary>
public sealed class EnergyStatsHistory
{
    public const int MaxWindowSeconds = 5 * 60;
    public static int Capacity { get; } = MaxWindowSeconds * GameSimulation.TicksPerSecond;

    private static readonly EntityKind[] ProducerKinds =
        MvpDefinitions.PowerProduction.Keys.OrderBy(kind => (int)kind).ToArray();

    private static readonly EntityKind[] ConsumerKinds =
        MvpDefinitions.PowerDemand.Keys.OrderBy(kind => (int)kind).ToArray();

    private readonly int[] _totalProduced;
    private readonly int[] _totalDemand;
    private readonly int[,] _producedByKind;
    private readonly int[,] _demandByKind;
    private int _count;
    private int _next;

    public EnergyStatsHistory()
    {
        _totalProduced = new int[Capacity];
        _totalDemand = new int[Capacity];
        _producedByKind = new int[ProducerKinds.Length, Capacity];
        _demandByKind = new int[ConsumerKinds.Length, Capacity];
    }

    public static IReadOnlyList<EntityKind> AllProducerKinds => ProducerKinds;

    public static IReadOnlyList<EntityKind> AllConsumerKinds => ConsumerKinds;

    public int SampleCount => _count;

    public void Record(
        int totalProduced,
        int totalDemand,
        IReadOnlyDictionary<EntityKind, int> producedByKind,
        IReadOnlyDictionary<EntityKind, int> demandByKind)
    {
        var index = _next;
        _totalProduced[index] = totalProduced;
        _totalDemand[index] = totalDemand;

        for (var i = 0; i < ProducerKinds.Length; i++)
        {
            _producedByKind[i, index] = producedByKind.GetValueOrDefault(ProducerKinds[i]);
        }

        for (var i = 0; i < ConsumerKinds.Length; i++)
        {
            _demandByKind[i, index] = demandByKind.GetValueOrDefault(ConsumerKinds[i]);
        }

        _next = (index + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
    }

    public EnergyStatsWindow Query(int windowSeconds)
    {
        var ticks = Math.Clamp(windowSeconds, 1, MaxWindowSeconds) * GameSimulation.TicksPerSecond;
        var available = Math.Min(ticks, _count);
        if (available <= 0)
        {
            return EnergyStatsWindow.Empty;
        }

        var producedSeries = new int[available];
        var demandSeries = new int[available];
        var producedByKindSeries = new Dictionary<EntityKind, int[]>();
        var demandByKindSeries = new Dictionary<EntityKind, int[]>();
        var producedSums = new Dictionary<EntityKind, long>();
        var demandSums = new Dictionary<EntityKind, long>();

        for (var i = 0; i < ProducerKinds.Length; i++)
        {
            producedByKindSeries[ProducerKinds[i]] = new int[available];
            producedSums[ProducerKinds[i]] = 0;
        }

        for (var i = 0; i < ConsumerKinds.Length; i++)
        {
            demandByKindSeries[ConsumerKinds[i]] = new int[available];
            demandSums[ConsumerKinds[i]] = 0;
        }

        long producedTotal = 0;
        long demandTotal = 0;
        for (var i = 0; i < available; i++)
        {
            var ringIndex = RingIndex(available, i);
            producedSeries[i] = _totalProduced[ringIndex];
            demandSeries[i] = _totalDemand[ringIndex];
            producedTotal += producedSeries[i];
            demandTotal += demandSeries[i];

            for (var k = 0; k < ProducerKinds.Length; k++)
            {
                var value = _producedByKind[k, ringIndex];
                producedByKindSeries[ProducerKinds[k]][i] = value;
                producedSums[ProducerKinds[k]] += value;
            }

            for (var k = 0; k < ConsumerKinds.Length; k++)
            {
                var value = _demandByKind[k, ringIndex];
                demandByKindSeries[ConsumerKinds[k]][i] = value;
                demandSums[ConsumerKinds[k]] += value;
            }
        }

        static double Average(long sum, int count) => count <= 0 ? 0 : sum / (double)count;

        var producerRows = ProducerKinds
            .Select(kind => new EnergyStatsKindRow(
                kind,
                Average(producedSums[kind], available),
                producedByKindSeries[kind]))
            .Where(row => row.Series.Any(v => v > 0) || row.AveragePerTick > 0)
            .ToArray();

        var consumerRows = ConsumerKinds
            .Select(kind => new EnergyStatsKindRow(
                kind,
                Average(demandSums[kind], available),
                demandByKindSeries[kind]))
            .Where(row => row.Series.Any(v => v > 0) || row.AveragePerTick > 0)
            .ToArray();

        return new EnergyStatsWindow(
            available,
            Average(producedTotal, available),
            Average(demandTotal, available),
            producedSeries,
            demandSeries,
            producerRows,
            consumerRows);
    }

    private int RingIndex(int available, int chronologicalIndex)
    {
        // chronologicalIndex 0 = oldest in window; available-1 = newest.
        var newest = (_next - 1 + Capacity) % Capacity;
        var ageFromNewest = available - 1 - chronologicalIndex;
        return (newest - ageFromNewest + Capacity) % Capacity;
    }
}

public sealed record EnergyStatsKindRow(EntityKind Kind, double AveragePerTick, IReadOnlyList<int> Series);

public sealed record EnergyStatsWindow(
    int SampleCount,
    double AverageProducedPerTick,
    double AverageDemandPerTick,
    IReadOnlyList<int> ProducedSeries,
    IReadOnlyList<int> DemandSeries,
    IReadOnlyList<EnergyStatsKindRow> ProducerRows,
    IReadOnlyList<EnergyStatsKindRow> ConsumerRows)
{
    public static EnergyStatsWindow Empty { get; } = new(
        0,
        0,
        0,
        Array.Empty<int>(),
        Array.Empty<int>(),
        Array.Empty<EnergyStatsKindRow>(),
        Array.Empty<EnergyStatsKindRow>());
}
