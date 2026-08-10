namespace SteelConveyorWar.Core;

/// <summary>
/// Presentation-only per-player energy sample ring (not hashed / not gameplay-affecting).
/// Tick-resolution storage keyed by absolute <see cref="GameSimulation.Tick"/>.
/// <see cref="Query"/> emits averages over <b>fixed</b> absolute time buckets (aligned to tick 0),
/// so a completed bucket's graph point never changes as the window slides.
/// </summary>
public sealed class EnergyStatsHistory
{
    public const int MaxWindowSeconds = 10 * 60;
    public static int Capacity { get; } = MaxWindowSeconds * GameSimulation.TicksPerSecond;

    private static readonly EntityKind[] ProducerKinds =
        MvpDefinitions.PowerProduction.Keys.OrderBy(kind => (int)kind).ToArray();

    private static readonly EntityKind[] ConsumerKinds =
        MvpDefinitions.PowerDemand.Keys.OrderBy(kind => (int)kind).ToArray();

    private readonly long[] _sampleTick;
    private readonly int[] _totalProduced;
    private readonly int[] _totalDemand;
    private readonly int[,] _producedByKind;
    private readonly int[,] _demandByKind;
    private int _count;
    private int _next;

    public EnergyStatsHistory()
    {
        _sampleTick = new long[Capacity];
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
        long tick,
        int totalProduced,
        int totalConsumed,
        IReadOnlyDictionary<EntityKind, int> producedByKind,
        IReadOnlyDictionary<EntityKind, int> consumedByKind)
    {
        var index = _next;
        _sampleTick[index] = tick;
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
        if (_count <= 0)
        {
            return EnergyStatsWindow.Empty;
        }

        var window = Math.Clamp(windowSeconds, 1, MaxWindowSeconds);
        var bucketSeconds = DisplayBucketSeconds(window);
        var bucketTicks = bucketSeconds * GameSimulation.TicksPerSecond;
        var maxBuckets = Math.Max(1, window / bucketSeconds);

        var newestIndex = (_next - 1 + Capacity) % Capacity;
        var newestTick = _sampleTick[newestIndex];
        var oldestTick = _sampleTick[OldestRingIndex()];

        // Bucket B covers absolute ticks [B*bucketTicks, (B+1)*bucketTicks).
        // Only completed buckets are emitted so points stay fixed once closed.
        var lastCompleteBucket = (int)((newestTick + 1) / bucketTicks) - 1;
        if (lastCompleteBucket < 0)
        {
            return EnergyStatsWindow.Empty;
        }

        var firstAvailableBucket = (int)((oldestTick + bucketTicks - 1) / bucketTicks);
        var firstBucket = Math.Max(firstAvailableBucket, lastCompleteBucket - maxBuckets + 1);
        var bucketCount = lastCompleteBucket - firstBucket + 1;
        if (bucketCount <= 0)
        {
            return EnergyStatsWindow.Empty;
        }

        var producedSeries = new float[bucketCount];
        var demandSeries = new float[bucketCount];
        var producedByKindSeries = new Dictionary<EntityKind, float[]>();
        var demandByKindSeries = new Dictionary<EntityKind, float[]>();
        var producedSums = new Dictionary<EntityKind, long>();
        var demandSums = new Dictionary<EntityKind, long>();

        for (var i = 0; i < ProducerKinds.Length; i++)
        {
            producedByKindSeries[ProducerKinds[i]] = new float[bucketCount];
            producedSums[ProducerKinds[i]] = 0;
        }

        for (var i = 0; i < ConsumerKinds.Length; i++)
        {
            demandByKindSeries[ConsumerKinds[i]] = new float[bucketCount];
            demandSums[ConsumerKinds[i]] = 0;
        }

        long producedTotal = 0;
        long demandTotal = 0;
        var ticksUsed = bucketCount * bucketTicks;

        for (var b = 0; b < bucketCount; b++)
        {
            var bucketId = firstBucket + b;
            var bucketStartTick = (long)bucketId * bucketTicks;
            long bucketProduced = 0;
            long bucketDemand = 0;
            var kindProduced = new long[ProducerKinds.Length];
            var kindDemand = new long[ConsumerKinds.Length];

            for (var t = 0; t < bucketTicks; t++)
            {
                var tick = bucketStartTick + t;
                if (!TryGetRingIndexForTick(tick, newestTick, out var ringIndex))
                {
                    continue;
                }

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

            producedSeries[b] = (float)(bucketProduced / (double)bucketTicks);
            demandSeries[b] = (float)(bucketDemand / (double)bucketTicks);
            producedTotal += bucketProduced;
            demandTotal += bucketDemand;

            for (var k = 0; k < ProducerKinds.Length; k++)
            {
                var avg = (float)(kindProduced[k] / (double)bucketTicks);
                producedByKindSeries[ProducerKinds[k]][b] = avg;
                producedSums[ProducerKinds[k]] += kindProduced[k];
            }

            for (var k = 0; k < ConsumerKinds.Length; k++)
            {
                var avg = (float)(kindDemand[k] / (double)bucketTicks);
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

    private int OldestRingIndex()
    {
        if (_count < Capacity)
        {
            return 0;
        }

        return _next;
    }

    private bool TryGetRingIndexForTick(long tick, long newestTick, out int ringIndex)
    {
        ringIndex = 0;
        var age = newestTick - tick;
        if (age < 0 || age >= _count)
        {
            return false;
        }

        ringIndex = (int)(((_next - 1 + Capacity) % Capacity - age % Capacity + Capacity) % Capacity);
        return _sampleTick[ringIndex] == tick;
    }
}

public sealed record EnergyStatsKindRow(EntityKind Kind, double AveragePerTick, IReadOnlyList<float> Series);

public sealed record EnergyStatsWindow(
    int SampleCount,
    double AverageProducedPerTick,
    double AverageDemandPerTick,
    IReadOnlyList<float> ProducedSeries,
    IReadOnlyList<float> DemandSeries,
    IReadOnlyList<EnergyStatsKindRow> ProducerRows,
    IReadOnlyList<EnergyStatsKindRow> ConsumerRows)
{
    public static EnergyStatsWindow Empty { get; } = new(
        0,
        0,
        0,
        Array.Empty<float>(),
        Array.Empty<float>(),
        Array.Empty<EnergyStatsKindRow>(),
        Array.Empty<EnergyStatsKindRow>());
}
