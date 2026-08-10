namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Per-player vision discs and tech-signature hotspots.
    /// </summary>
    private static class FogOfWarSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.UpdateFogOfWar();
            sim.UpdateTechSignatures();
        }
    }

    private void UpdateFogOfWar()
    {
        // Decay only previously visible tiles (dirty list), then repaint once per vision source.
        foreach (var player in _players)
        {
            player.DecayVisibility();
        }

        var entities = World.Entities;
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            var entity = entities[entityIndex];
            if (!entity.IsAlive || entity.IsGarrisoned || entity.OwnerId is null)
            {
                continue;
            }

            var ownerId = entity.OwnerId.Value;
            _fogAlliedScratch.Clear();
            for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
            {
                var player = _players[playerIndex];
                if (AreAllied(ownerId, player.Id))
                {
                    _fogAlliedScratch.Add(player);
                }
            }

            if (_fogAlliedScratch.Count == 0)
            {
                continue;
            }

            var radius = ResolveStat(
                ownerId,
                ResearchStatIds.VisionRadius,
                MvpDefinitions.GetStats(entity.Kind).VisionRadius,
                minValue: 0);

            var origin = entity.Position;
            var radiusSquared = radius * radius;
            for (var y = origin.Y - radius; y <= origin.Y + radius; y++)
            {
                for (var x = origin.X - radius; x <= origin.X + radius; x++)
                {
                    var dx = x - origin.X;
                    var dy = y - origin.Y;
                    if (dx * dx + dy * dy > radiusSquared)
                    {
                        continue;
                    }

                    var position = new TilePosition(x, y);
                    if (!World.IsInside(position))
                    {
                        continue;
                    }

                    for (var allyIndex = 0; allyIndex < _fogAlliedScratch.Count; allyIndex++)
                    {
                        _fogAlliedScratch[allyIndex].SetVisible(position);
                    }
                }
            }
        }
    }

    private void UpdateTechSignatures()
    {
        foreach (var player in _players)
        {
            var hotspots = World.Entities
                .Where(entity =>
                    entity.IsAlive
                    && entity.OwnerId is not null
                    && !AreAllied(entity.OwnerId.Value, player.Id))
                .Select(entity => new
                {
                    ZoneX = entity.Position.X / 8,
                    ZoneY = entity.Position.Y / 8,
                    Intensity = MvpDefinitions.TechSignatureIntensity.GetValueOrDefault(entity.Kind)
                })
                .Where(signal => signal.Intensity > 0)
                .GroupBy(signal => new { signal.ZoneX, signal.ZoneY })
                .Select(group => new TechSignatureHotspot(group.Key.ZoneX, group.Key.ZoneY, group.Sum(signal => signal.Intensity)))
                .OrderByDescending(signal => signal.Intensity)
                .ToList();
            player.SetTechSignatures(hotspots);
        }
    }
}
