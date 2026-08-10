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
        foreach (var player in _players)
        {
            player.DecayVisibility();
            foreach (var entity in World.Entities.Where(entity =>
                         entity.IsAlive
                         && !entity.IsGarrisoned
                         && entity.OwnerId is not null
                         && AreAllied(entity.OwnerId.Value, player.Id)))
            {
                var radius = MvpDefinitions.GetStats(entity.Kind).VisionRadius;
                if (entity.OwnerId is not null)
                {
                    radius = ResolveStat(entity.OwnerId.Value, ResearchStatIds.VisionRadius, radius, minValue: 0);
                }
                for (var y = entity.Position.Y - radius; y <= entity.Position.Y + radius; y++)
                {
                    for (var x = entity.Position.X - radius; x <= entity.Position.X + radius; x++)
                    {
                        var position = new TilePosition(x, y);
                        if (World.IsInside(position) && entity.Position.IsWithinEuclideanRange(position, radius))
                        {
                            player.SetVisible(position);
                        }
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
