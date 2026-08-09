namespace SteelConveyorWar.Core;

/// <summary>
/// Presentation-only per-player energy sample ring (not hashed / not gameplay-affecting).
/// Tick-resolution storage; <see cref="Query"/> downsamples into time buckets (avg per tick)
/// so graphs stay smooth — 1s for short windows, 5s for 5 min, 10s for 10 min.
/// </summary>
public sealed class EnergyStatsHistory
{
    public const int MaxWindowSeconds = 10 * 60;
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

    /// <summary>
    /// Graph point spacing for a window: 1s (≤1 min), 5s (5 min), 10s (10 min).
    /// </summary>
    public static int DisplayBucketSeconds(int windowSeconds)
    {
        if (windowSeconds >= 10 * 60)
        {
            return 10;
        }

        if (windowSeconds >= 5 * 60)
        {
            return 5;
        }

        return 1;
    }

    public void Record(
        int totalProduced,
        int totalConsumed,
        IReadOnlyDictionary<EntityKind, int> producedByKind,
        IReadOnlyDictionary<EntityKind, int> consumedByKind)
    {
        var index = _next;
        _totalProduced[index] = totalProduced;
        _totalDemand[index] = totalConsumed;

        for (var i = 0; i < ProducerKinds.Length; i++)
        {
            _producedByKind[i, index] = producedByKind.GetValueOrDefault(ProducerKinds[i]);
        }

        for (var i = 0; i < ConsumerKinds.Length; i++)
        {
            _demandByKind[i, index] = consumedByKind.GetValueOrDefault(ConsumerKinds[i]);
        }

        _next = (index + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
    }

    public EnergyStatsWindow Query(int windowSeconds)
    {
        var window = Math.Clamp(windowSeconds, 1, MaxWindowSeconds);
        var ticksWanted = window * GameSimulation.TicksPerSecond;
        var availableTicks = Math.Min(ticksWanted, _count);
        if (availableTicks <= 0)
        {
            return EnergyStatsWindow.Empty;
        }

        var bucketSeconds = DisplayBucketSeconds(window);
        var bucketTicks = bucketSeconds * GameSimulation.TicksPerSecond;
        var bucketCount = Math.Max(1, availableTicks / bucketTicks);
        // Align to newest: use the trailing bucketCount * bucketTicks ticks.
        var ticksUsed = Math.Min(availableTicks, bucketCount * bucketTicks);
        var tickOffset = availableTicks - ticksUsed; // skip incomplete oldest partial bucket
        bucketCount = ticksUsed / bucketTicks;
        if (bucketCount <= 0)
        {
            return EnergyStatsWindow.Empty;
        }

        var producedSeries = new int[bucketCount];
        var demandSeries = new int[bucketCount];
        var producedByKindSeries = new Dictionary<EntityKind, int[]>();
        var demandByKindSeries = new Dictionary<EntityKind, int[]>();
        var producedSums = new Dictionary<EntityKind, long>();
        var demandSums = new Dictionary<EntityKind, long>();

        for (var i = 0; i < ProducerKinds.Length; i++)
        {
            producedByKindSeries[ProducerKinds[i]] = new int[bucketCount];
            producedSums[ProducerKinds[i]] = 0;
        }

        for (var i = 0; i < ConsumerKinds.Length; i++)
        {
            demandByKindSeries[ConsumerKinds[i]] = new int[bucketCount];
            demandSums[ConsumerKinds[i]] = 0;
        }

        long producedTotal = 0;
        long demandTotal = 0;

        for (var b = 0; b < bucketCount; b++)
        {
            long bucketProduced = 0;
            long bucketDemand = 0;
            var kindProduced = new long[ProducerKinds.Length];
            var kindDemand = new long[ConsumerKinds.Length];

            for (var t = 0; t < bucketTicks; t++)
            {
                var chronological = tickOffset + b * bucketTicks + t;
                var ringIndex = RingIndex(availableTicks, chronological);
                bucketProduced += _totalProduced[ringIndex];
                bucketDemand += _totalDemand[ringIndex];
                for (var k = 0; k < ProducerKinds.Length; k++)
                {
                    kindProduced[k] += _producedByKind[k, ringIndex];
                }

                for (var k = 0; k < ConsumerKinds.Length; k++)
                {
                    kindDemand[k] += _demandByKind[k, ringIndex];
                }
            }

            // Average per tick within the bucket (graph Y = sustained rate).
            producedSeries[b] = (int)Math.Round(bucketProduced / (double)bucketTicks);
            demandSeries[b] = (int)Math.Round(bucketDemand / (double)bucketTicks);
            producedTotal += bucketProduced;
            demandTotal += bucketDemand;

            for (var k = 0; k < ProducerKinds.Length; k++)
            {
                var avg = (int)Math.Round(kindProduced[k] / (double)bucketTicks);
                producedByKindSeries[ProducerKinds[k]][b] = avg;
                producedSums[ProducerKinds[k]] += kindProduced[k];
            }

            for (var k = 0; k < ConsumerKinds.Length; k++)
            {
                var avg = (int)Math.Round(kindDemand[k] / (double)bucketTicks);
                demandByKindSeries[ConsumerKinds[k]][b] = avg;
                demandSums[ConsumerKinds[k]] += kindDemand[k];
            }
        }

        static double Average(long sum, int count) => count <= 0 ? 0 : sum / (double)count;

        var producerRows = ProducerKinds
            .Select(kind => new EnergyStatsKindRow(
                kind,
                Average(producedSums[kind], ticksUsed),
                producedByKindSeries[kind]))
            .Where(row => row.Series.Any(v => v > 0) || row.AveragePerTick > 0)
            .ToArray();

        var consumerRows = ConsumerKinds
            .Select(kind => new EnergyStatsKindRow(
                kind,
                Average(demandSums[kind], ticksUsed),
                demandByKindSeries[kind]))
            .Where(row => row.Series.Any(v => v > 0) || row.AveragePerTick > 0)
            .ToArray();

        return new EnergyStatsWindow(
            bucketCount,
            Average(producedTotal, ticksUsed),
            Average(demandTotal, ticksUsed),
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
