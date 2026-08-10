namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Produces power and fills energy buffers emptiest-first.
    /// </summary>
    private static class PowerSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.UpdatePower();
        }

        public static void RecordStats(GameSimulation sim)
        {
            sim.RecordEnergyStatsSample();
        }
    }

    /// <summary>
    /// Drains <see cref="MvpDefinitions.GetPowerDemand"/> from the building buffer when it can afford to run.
    /// Returns false when the buffer is too low (work must pause). No demand configured → success (unpowered-free).
    /// Successful drains accumulate into this tick's energy-stats consumption sample.
    /// </summary>
    public bool TryConsumeBuildingEnergy(WorldEntity building)
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

        foreach (var player in _players)
        {
            player.PowerProduced = 0;
            player.PowerDemand = 0;
        }

        foreach (var entity in World.Entities.Where(entity => entity.IsAlive && entity.OwnerId is not null))
        {
            var player = GetPlayer(entity.OwnerId!.Value);
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

        foreach (var player in _players)
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
        foreach (var player in _players)
        {
            _tickProducedByKind[player.Id] = new Dictionary<EntityKind, int>();
            _tickConsumedByKind[player.Id] = new Dictionary<EntityKind, int>();
            _tickPowerProduced[player.Id] = 0;
            _tickPowerConsumed[player.Id] = 0;
        }
    }

    /// <summary>
    /// Records this tick's production and <b>actual</b> buffer drains into presentation-only energy history.
    /// Must run after all <see cref="TryConsumeBuildingEnergy"/> call sites for the tick.
    /// </summary>
    private void RecordEnergyStatsSample()
    {
        foreach (var player in _players)
        {
            player.EnergyStats.Record(
                Tick,
                _tickPowerProduced.GetValueOrDefault(player.Id),
                _tickPowerConsumed.GetValueOrDefault(player.Id),
                _tickProducedByKind.GetValueOrDefault(player.Id) ?? new Dictionary<EntityKind, int>(),
                _tickConsumedByKind.GetValueOrDefault(player.Id) ?? new Dictionary<EntityKind, int>());
        }
    }

    private void FillEnergyBuffersEmptiestFirst(PlayerState player)
    {
        var remaining = player.PowerProduced;
        if (remaining <= 0)
        {
            return;
        }

        var consumers = World.Entities
            .Where(entity =>
                entity.IsAlive
                && entity.OwnerId == player.Id
                && entity.EnergyBufferCapacity > 0)
            .ToList();

        if (consumers.Count == 0)
        {
            return;
        }

        while (remaining > 0)
        {
            var target = consumers
                .Where(entity => entity.EnergyBuffer < entity.EnergyBufferCapacity)
                .OrderBy(entity => (double)entity.EnergyBuffer / entity.EnergyBufferCapacity)
                .ThenBy(entity => entity.Id)
                .FirstOrDefault();
            if (target is null)
            {
                break;
            }

            target.EnergyBuffer++;
            remaining--;
        }
    }
}
