namespace SteelConveyorWar.Core;

/// <summary>
/// R06: standalone power/energy system extracted from the <see cref="GameSimulation"/> god-object.
/// Produces power, fills energy buffers emptiest-first, drains per-building demand, and records
/// presentation-only energy history. Owns its per-tick accumulators and talks to the rest of the
/// simulation only through <see cref="ISimulationSystemContext"/>, so it can be unit-tested in isolation.
/// </summary>
internal sealed class PowerSystem
{
    private readonly ISimulationSystemContext _context;

    // Per-tick energy accumulators (presentation-only; not hashed). Owned here now that Power is the
    // single writer/reader of the produce/record path; TryConsumeBuildingEnergy adds consumption.
    private readonly Dictionary<PlayerId, int> _tickPowerProduced = new();
    private readonly Dictionary<PlayerId, int> _tickPowerConsumed = new();
    private readonly Dictionary<PlayerId, Dictionary<EntityKind, int>> _tickProducedByKind = new();
    private readonly Dictionary<PlayerId, Dictionary<EntityKind, int>> _tickConsumedByKind = new();

    /// <summary>
    /// Orders fill candidates by fill fraction ascending (<c>buffer/capacity</c> via cross-multiply), then entity id.
    /// </summary>
    private static readonly Comparer<(int Buffer, int Capacity, int Id)> EnergyFillPriorityComparer =
        Comparer<(int Buffer, int Capacity, int Id)>.Create(static (left, right) =>
            EnergyFillRatioComparer.CompareRatios(
                left.Buffer,
                left.Capacity,
                left.Id,
                right.Buffer,
                right.Capacity,
                right.Id));

    public PowerSystem(ISimulationSystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Produces power and fills energy buffers emptiest-first.</summary>
    public void Tick() => UpdatePower();

    /// <summary>
    /// Records this tick's production and <b>actual</b> buffer drains into presentation-only energy history.
    /// Must run after all <see cref="TryConsumeBuildingEnergy"/> call sites for the tick.
    /// </summary>
    public void RecordStats()
    {
        foreach (var player in _context.Players)
        {
            player.EnergyStats.Record(
                _context.Tick,
                _tickPowerProduced.GetValueOrDefault(player.Id),
                _tickPowerConsumed.GetValueOrDefault(player.Id),
                _tickProducedByKind.GetValueOrDefault(player.Id) ?? new Dictionary<EntityKind, int>(),
                _tickConsumedByKind.GetValueOrDefault(player.Id) ?? new Dictionary<EntityKind, int>());
        }
    }

    /// <summary>
    /// Drains <see cref="MvpDefinitions.GetPowerDemand"/> from the building buffer when it can afford to run.
    /// Returns false when the buffer is too low (work must pause). No demand configured → success (unpowered-free).
    /// Successful drains accumulate into this tick's energy-stats consumption sample.
    /// </summary>
    // R14: internal — an authoritative energy mutation, callable only from in-assembly tick code
    // (the class itself is already internal; this makes the intent explicit).
    internal bool TryConsumeBuildingEnergy(WorldEntity building)
    {
        var demand = MvpDefinitions.GetPowerDemand(building.Kind);
        if (demand <= 0)
        {
            return true;
        }

        if (building.EnergyBuffer < demand)
        {
            return false;
        }

        building.EnergyBuffer -= demand;
        if (building.OwnerId is not null)
        {
            var ownerId = building.OwnerId.Value;
            _tickPowerConsumed[ownerId] = _tickPowerConsumed.GetValueOrDefault(ownerId) + demand;
            if (!_tickConsumedByKind.TryGetValue(ownerId, out var byKind))
            {
                byKind = new Dictionary<EntityKind, int>();
                _tickConsumedByKind[ownerId] = byKind;
            }

            byKind[building.Kind] = byKind.GetValueOrDefault(building.Kind) + demand;
        }

        return true;
    }

    private void UpdatePower()
    {
        ResetEnergyTickAccumulators();

        foreach (var player in _context.Players)
        {
            player.PowerProduced = 0;
            player.PowerDemand = 0;
        }

        foreach (var entity in _context.World.Entities.Where(entity => entity.IsAlive && entity.OwnerId is not null))
        {
            var player = _context.GetPlayer(entity.OwnerId!.Value);
            var produced = entity.Kind switch
            {
                EntityKind.SolarPanel => MvpDefinitions.PowerProduction.GetValueOrDefault(EntityKind.SolarPanel),
                EntityKind.CoalPlant when entity.InputBuffer.TryRemove(ItemId.Coal, 1) => MvpDefinitions.PowerProduction.GetValueOrDefault(EntityKind.CoalPlant),
                _ => 0
            };
            if (produced > 0)
            {
                player.PowerProduced += produced;
                _tickPowerProduced[player.Id] = _tickPowerProduced.GetValueOrDefault(player.Id) + produced;
                var producedMap = _tickProducedByKind[player.Id];
                producedMap[entity.Kind] = producedMap.GetValueOrDefault(entity.Kind) + produced;
            }

            // HUD demand = installed consumer rating (not actual drain this tick).
            player.PowerDemand += MvpDefinitions.GetPowerDemand(entity.Kind);
        }

        foreach (var player in _context.Players)
        {
            FillEnergyBuffersEmptiestFirst(player);
        }
    }

    private void ResetEnergyTickAccumulators()
    {
        _tickPowerProduced.Clear();
        _tickPowerConsumed.Clear();
        _tickProducedByKind.Clear();
        _tickConsumedByKind.Clear();
        foreach (var player in _context.Players)
        {
            _tickProducedByKind[player.Id] = new Dictionary<EntityKind, int>();
            _tickConsumedByKind[player.Id] = new Dictionary<EntityKind, int>();
            _tickPowerProduced[player.Id] = 0;
            _tickPowerConsumed[player.Id] = 0;
        }
    }

    /// <summary>
    /// Distributes this tick's <see cref="PlayerState.PowerProduced"/> into owned consumer buffers
    /// emptiest-first: lowest <c>EnergyBuffer/Capacity</c> (exact rational order via cross-multiply),
    /// then lowest entity id. Uses a min-heap so each unit is O(log N) instead of re-sorting all consumers.
    /// </summary>
    private void FillEnergyBuffersEmptiestFirst(PlayerState player)
    {
        var remaining = player.PowerProduced;
        if (remaining <= 0)
        {
            return;
        }

        var heap = new PriorityQueue<WorldEntity, (int Buffer, int Capacity, int Id)>(EnergyFillPriorityComparer);
        foreach (var entity in _context.World.Entities)
        {
            if (!entity.IsAlive
                || entity.OwnerId != player.Id
                || entity.EnergyBufferCapacity <= 0
                || entity.EnergyBuffer >= entity.EnergyBufferCapacity)
            {
                continue;
            }

            heap.Enqueue(entity, (entity.EnergyBuffer, entity.EnergyBufferCapacity, entity.Id));
        }

        while (remaining > 0 && heap.Count > 0)
        {
            var target = heap.Dequeue();
            target.EnergyBuffer++;
            remaining--;
            if (target.EnergyBuffer < target.EnergyBufferCapacity)
            {
                heap.Enqueue(target, (target.EnergyBuffer, target.EnergyBufferCapacity, target.Id));
            }
        }
    }
}
