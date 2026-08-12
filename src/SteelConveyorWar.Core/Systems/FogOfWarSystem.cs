namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Per-player vision discs and tech-signature hotspots.
    /// R16: skip full repaint when vision sources are unchanged; cache vision disks;
    /// rebuild tech signatures with reusable buffers (no LINQ/GroupBy).
    /// </summary>
    private static class FogOfWarSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.UpdateFogOfWar();
            sim.UpdateTechSignatures();
        }
    }

    private readonly record struct FogVisionSource(int EntityId, int OwnerValue, int X, int Y, int Radius);

    private readonly record struct TechSignatureSource(
        int EntityId,
        int OwnerValue,
        int ZoneX,
        int ZoneY,
        int Intensity);

    private readonly List<FogVisionSource> _fogVisionSourcesCurrent = new();
    private readonly List<FogVisionSource> _fogVisionSourcesPrevious = new();
    private bool _fogVisionFingerprintReady;
    private readonly Dictionary<int, CachedVisionDisk> _fogVisionDiskByEntityId = new();
    private readonly HashSet<int> _fogActiveVisionEntityIds = new();

    private readonly List<TechSignatureSource> _techSignatureSourcesCurrent = new();
    private readonly List<TechSignatureSource> _techSignatureSourcesPrevious = new();
    private bool _techSignatureFingerprintReady;
    private readonly Dictionary<(int ZoneX, int ZoneY), int> _techZoneIntensityScratch = new();
    private readonly List<(int ZoneX, int ZoneY, int Order)> _techZoneOrderScratch = new();
    private readonly List<TechSignatureHotspot> _techHotspotScratch = new();
    private readonly List<int> _techHotspotOrderScratch = new();
    private readonly List<int> _fogStaleDiskIdScratch = new();
    private readonly HashSet<long> _fogDirtyTileKeys = new();
    private readonly List<TilePosition> _fogDirtyTileScratch = new();
    private readonly HashSet<int> _fogChangedVisionIds = new();
    private readonly Dictionary<int, FogVisionSource> _fogPreviousById = new();
    private readonly Dictionary<int, FogVisionSource> _fogCurrentById = new();

    private sealed class CachedVisionDisk
    {
        public int OriginX;
        public int OriginY;
        public int Radius;
        public readonly List<TilePosition> Tiles = new();
    }

    private void UpdateFogOfWar()
    {
        CollectFogVisionSources(_fogVisionSourcesCurrent);

        if (_fogVisionFingerprintReady
            && FogVisionSourcesEqual(_fogVisionSourcesPrevious, _fogVisionSourcesCurrent))
        {
            for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
            {
                _players[playerIndex].ClearFogDirty();
            }

            return;
        }

        if (!_fogVisionFingerprintReady)
        {
            RepaintFogFull();
            return;
        }

        RepaintFogDirtyRegions();
    }

    private void RepaintFogFull()
    {
        for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
        {
            var player = _players[playerIndex];
            player.BeginFogDirtyTracking();
            player.DecayVisibility();
        }

        PaintAllVisionSources();
        FinishFogPass();
    }

    /// <summary>
    /// M06: when vision sources move, decay/paint only old∪new disks of changed sources.
    /// Static sources keep Visible without a full-map decay.
    /// </summary>
    private void RepaintFogDirtyRegions()
    {
        _fogChangedVisionIds.Clear();
        _fogDirtyTileKeys.Clear();
        _fogDirtyTileScratch.Clear();
        _fogPreviousById.Clear();
        _fogCurrentById.Clear();

        for (var i = 0; i < _fogVisionSourcesPrevious.Count; i++)
        {
            var source = _fogVisionSourcesPrevious[i];
            _fogPreviousById[source.EntityId] = source;
        }

        for (var i = 0; i < _fogVisionSourcesCurrent.Count; i++)
        {
            var source = _fogVisionSourcesCurrent[i];
            _fogCurrentById[source.EntityId] = source;
        }

        foreach (var pair in _fogPreviousById)
        {
            if (!_fogCurrentById.TryGetValue(pair.Key, out var current) || current != pair.Value)
            {
                _fogChangedVisionIds.Add(pair.Key);
                // Cache still holds the previous disk until GetOrBuild refreshes it.
                if (_fogVisionDiskByEntityId.TryGetValue(pair.Key, out var oldDisk))
                {
                    AddDirtyTiles(oldDisk.Tiles);
                }
            }
        }

        foreach (var pair in _fogCurrentById)
        {
            if (!_fogPreviousById.ContainsKey(pair.Key))
            {
                _fogChangedVisionIds.Add(pair.Key);
            }
        }

        // Refresh disks for current sources; union new disks of changed ids into dirty set.
        _fogActiveVisionEntityIds.Clear();
        var entities = World.Entities;
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            var entity = entities[entityIndex];
            if (!entity.IsAlive || entity.IsGarrisoned || entity.OwnerId is null)
            {
                continue;
            }

            var ownerId = entity.OwnerId.Value;
            var radius = ResolveStat(
                ownerId,
                ResearchStatIds.VisionRadius,
                GameplayTables.GetStats(entity.Kind).VisionRadius,
                minValue: 0);

            var tiles = GetOrBuildVisionDisk(entity.Id, entity.Position, radius);
            _fogActiveVisionEntityIds.Add(entity.Id);
            if (_fogChangedVisionIds.Contains(entity.Id))
            {
                AddDirtyTiles(tiles);
            }
        }

        PruneStaleVisionDisks();

        for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
        {
            _players[playerIndex].BeginFogDirtyTracking();
        }

        for (var i = 0; i < _fogDirtyTileScratch.Count; i++)
        {
            var tile = _fogDirtyTileScratch[i];
            for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
            {
                _players[playerIndex].DecayVisibilityAt(tile);
            }
        }

        // Re-paint dirty tiles covered by any current vision source.
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            var entity = entities[entityIndex];
            if (!entity.IsAlive || entity.IsGarrisoned || entity.OwnerId is null)
            {
                continue;
            }

            if (!_fogVisionDiskByEntityId.TryGetValue(entity.Id, out var disk))
            {
                continue;
            }

            _fogAlliedScratch.Clear();
            var ownerId = entity.OwnerId.Value;
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

            for (var tileIndex = 0; tileIndex < disk.Tiles.Count; tileIndex++)
            {
                var position = disk.Tiles[tileIndex];
                if (!_fogDirtyTileKeys.Contains(PackFogTile(position)))
                {
                    continue;
                }

                for (var allyIndex = 0; allyIndex < _fogAlliedScratch.Count; allyIndex++)
                {
                    _fogAlliedScratch[allyIndex].SetVisible(position);
                }
            }
        }

        for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
        {
            _players[playerIndex].EndFogDirtyTracking();
        }

        _fogVisionSourcesPrevious.Clear();
        _fogVisionSourcesPrevious.AddRange(_fogVisionSourcesCurrent);
        _fogVisionFingerprintReady = true;
    }

    private void AddDirtyTiles(List<TilePosition> tiles)
    {
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (_fogDirtyTileKeys.Add(PackFogTile(tile)))
            {
                _fogDirtyTileScratch.Add(tile);
            }
        }
    }

    private static long PackFogTile(TilePosition position)
        => ((long)position.X << 32) | (uint)position.Y;

    private void PaintAllVisionSources()
    {
        _fogActiveVisionEntityIds.Clear();
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
                GameplayTables.GetStats(entity.Kind).VisionRadius,
                minValue: 0);

            var tiles = GetOrBuildVisionDisk(entity.Id, entity.Position, radius);
            _fogActiveVisionEntityIds.Add(entity.Id);

            for (var tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
            {
                var position = tiles[tileIndex];
                for (var allyIndex = 0; allyIndex < _fogAlliedScratch.Count; allyIndex++)
                {
                    _fogAlliedScratch[allyIndex].SetVisible(position);
                }
            }
        }

        PruneStaleVisionDisks();
    }

    private void FinishFogPass()
    {
        for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
        {
            _players[playerIndex].EndFogDirtyTracking();
        }

        _fogVisionSourcesPrevious.Clear();
        _fogVisionSourcesPrevious.AddRange(_fogVisionSourcesCurrent);
        _fogVisionFingerprintReady = true;
    }

    private void CollectFogVisionSources(List<FogVisionSource> destination)
    {
        destination.Clear();
        var entities = World.Entities;
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            var entity = entities[entityIndex];
            if (!entity.IsAlive || entity.IsGarrisoned || entity.OwnerId is null)
            {
                continue;
            }

            var ownerId = entity.OwnerId.Value;
            var radius = ResolveStat(
                ownerId,
                ResearchStatIds.VisionRadius,
                GameplayTables.GetStats(entity.Kind).VisionRadius,
                minValue: 0);
            var origin = entity.Position;
            destination.Add(new FogVisionSource(entity.Id, ownerId.Value, origin.X, origin.Y, radius));
        }
    }

    private static bool FogVisionSourcesEqual(List<FogVisionSource> left, List<FogVisionSource> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private List<TilePosition> GetOrBuildVisionDisk(int entityId, TilePosition origin, int radius)
    {
        if (_fogVisionDiskByEntityId.TryGetValue(entityId, out var cached)
            && cached.OriginX == origin.X
            && cached.OriginY == origin.Y
            && cached.Radius == radius)
        {
            return cached.Tiles;
        }

        if (cached is null)
        {
            cached = new CachedVisionDisk();
            _fogVisionDiskByEntityId[entityId] = cached;
        }

        cached.OriginX = origin.X;
        cached.OriginY = origin.Y;
        cached.Radius = radius;
        cached.Tiles.Clear();

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

                cached.Tiles.Add(position);
            }
        }

        return cached.Tiles;
    }

    private void PruneStaleVisionDisks()
    {
        // Remove caches for entities that are no longer vision sources (dead / garrisoned / unowned).
        _fogStaleDiskIdScratch.Clear();
        foreach (var pair in _fogVisionDiskByEntityId)
        {
            if (!_fogActiveVisionEntityIds.Contains(pair.Key))
            {
                _fogStaleDiskIdScratch.Add(pair.Key);
            }
        }

        for (var i = 0; i < _fogStaleDiskIdScratch.Count; i++)
        {
            _fogVisionDiskByEntityId.Remove(_fogStaleDiskIdScratch[i]);
        }

        _fogStaleDiskIdScratch.Clear();
    }

    private void UpdateTechSignatures()
    {
        CollectTechSignatureSources(_techSignatureSourcesCurrent);

        if (_techSignatureFingerprintReady
            && TechSignatureSourcesEqual(_techSignatureSourcesPrevious, _techSignatureSourcesCurrent))
        {
            return;
        }

        for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
        {
            RebuildTechSignaturesForPlayer(_players[playerIndex]);
        }

        _techSignatureSourcesPrevious.Clear();
        _techSignatureSourcesPrevious.AddRange(_techSignatureSourcesCurrent);
        _techSignatureFingerprintReady = true;
    }

    private void CollectTechSignatureSources(List<TechSignatureSource> destination)
    {
        destination.Clear();
        var entities = World.Entities;
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            var entity = entities[entityIndex];
            if (!entity.IsAlive || entity.OwnerId is null)
            {
                continue;
            }

            if (!GameplayTables.TechSignatureIntensity.TryGetValue(entity.Kind, out var intensity) || intensity <= 0)
            {
                continue;
            }

            var ownerId = entity.OwnerId.Value;
            var position = entity.Position;
            destination.Add(new TechSignatureSource(
                entity.Id,
                ownerId.Value,
                position.X / 8,
                position.Y / 8,
                intensity));
        }
    }

    private static bool TechSignatureSourcesEqual(List<TechSignatureSource> left, List<TechSignatureSource> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private void RebuildTechSignaturesForPlayer(PlayerState player)
    {
        _techZoneIntensityScratch.Clear();
        _techZoneOrderScratch.Clear();
        _techHotspotScratch.Clear();
        _techHotspotOrderScratch.Clear();

        // Match prior LINQ GroupBy order: first occurrence of each zone in entity scan order
        // among non-allied intensity>0 sources, then OrderByDescending(Intensity) (stable).
        for (var sourceIndex = 0; sourceIndex < _techSignatureSourcesCurrent.Count; sourceIndex++)
        {
            var source = _techSignatureSourcesCurrent[sourceIndex];
            var ownerId = new PlayerId(source.OwnerValue);
            if (AreAllied(ownerId, player.Id))
            {
                continue;
            }

            var zone = (source.ZoneX, source.ZoneY);
            if (_techZoneIntensityScratch.TryGetValue(zone, out var existing))
            {
                _techZoneIntensityScratch[zone] = existing + source.Intensity;
            }
            else
            {
                _techZoneIntensityScratch[zone] = source.Intensity;
                _techZoneOrderScratch.Add((source.ZoneX, source.ZoneY, _techZoneOrderScratch.Count));
            }
        }

        for (var i = 0; i < _techZoneOrderScratch.Count; i++)
        {
            var (zoneX, zoneY, order) = _techZoneOrderScratch[i];
            var intensity = _techZoneIntensityScratch[(zoneX, zoneY)];
            _techHotspotScratch.Add(new TechSignatureHotspot(zoneX, zoneY, intensity));
            _techHotspotOrderScratch.Add(order);
        }

        // Deterministic tie-break: intensity desc, then discovery order (LINQ OrderByDescending is stable).
        for (var i = 1; i < _techHotspotScratch.Count; i++)
        {
            var hotspot = _techHotspotScratch[i];
            var order = _techHotspotOrderScratch[i];
            var j = i - 1;
            while (j >= 0)
            {
                var shouldInsertBefore = hotspot.Intensity > _techHotspotScratch[j].Intensity
                    || (hotspot.Intensity == _techHotspotScratch[j].Intensity && order < _techHotspotOrderScratch[j]);
                if (!shouldInsertBefore)
                {
                    break;
                }

                _techHotspotScratch[j + 1] = _techHotspotScratch[j];
                _techHotspotOrderScratch[j + 1] = _techHotspotOrderScratch[j];
                j--;
            }

            _techHotspotScratch[j + 1] = hotspot;
            _techHotspotOrderScratch[j + 1] = order;
        }

        player.SetTechSignatures(_techHotspotScratch);
    }
}
