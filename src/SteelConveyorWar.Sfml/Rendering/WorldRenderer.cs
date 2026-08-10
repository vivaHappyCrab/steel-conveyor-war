using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal static class WorldRenderer
{
    internal static void DrawWorld(
        IRenderTarget target,
        GameSimulation simulation,
        PlayerId localPlayer,
        int? selectedEntityId,
        EntityKind? pendingBuildKind,
        Direction pendingDirection,
        TilePosition? hoverTile,
        IReadOnlyList<TilePosition> patrolWaypoints,
        IReadOnlyList<CombatShotEvent> combatShots,
        float cameraX,
        float cameraY,
        float playfieldWidth,
        float playfieldHeight)
    {
        var world = simulation.World;
        var tile = new RectangleShape(new Vector2f(SfmlUiLayout.TileSize - 1f, SfmlUiLayout.TileSize - 1f));
        var minX = Math.Max(0, (int)(cameraX / SfmlUiLayout.TileSize) - 1);
        var minY = Math.Max(0, (int)(cameraY / SfmlUiLayout.TileSize) - 1);
        var maxX = Math.Min(world.Size.Width - 1, (int)((cameraX + playfieldWidth) / SfmlUiLayout.TileSize) + 1);
        var maxY = Math.Min(world.Size.Height - 1, (int)((cameraY + playfieldHeight) / SfmlUiLayout.TileSize) + 1);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var position = new TilePosition(x, y);
                tile.Position = new Vector2f(x * SfmlUiLayout.TileSize, y * SfmlUiLayout.TileSize);
                tile.FillColor = GetTerrainColor(world.GetTerrain(position), simulation.GetVisibility(localPlayer, position));
                target.Draw(tile);
            }
        }

        DrawTechSignatures(target, simulation.GetTechSignatureHotspots(localPlayer));

        var visibleEntities = world.Entities
            .Where(entity => entity.IsAlive && !entity.IsGarrisoned && IsVisibleToLocalPlayer(simulation, localPlayer, entity))
            .ToList();

        foreach (var entity in visibleEntities.Where(entity => !IsUnitDrawKind(entity.Kind)))
        {
            DrawEntity(target, simulation, entity, selectedEntityId == entity.Id);
        }

        foreach (var entity in visibleEntities.Where(entity => IsUnitDrawKind(entity.Kind)))
        {
            DrawEntity(target, simulation, entity, selectedEntityId == entity.Id);
        }

        DrawCombatShots(target, combatShots);

        foreach (var waypoint in patrolWaypoints)
        {
            if (!world.IsInside(waypoint))
            {
                continue;
            }

            using var marker = new RectangleShape(new Vector2f(SfmlUiLayout.TileSize - 6f, SfmlUiLayout.TileSize - 6f))
            {
                Position = new Vector2f(waypoint.X * SfmlUiLayout.TileSize + 3f, waypoint.Y * SfmlUiLayout.TileSize + 3f),
                FillColor = new Color(255, 210, 80, 90),
                OutlineColor = new Color(255, 230, 120),
                OutlineThickness = 1f
            };
            target.Draw(marker);
        }

        if (pendingBuildKind is not null && hoverTile is not null && world.IsInside(hoverTile.Value))
        {
            DrawGhostPreview(target, pendingBuildKind.Value, hoverTile.Value, pendingDirection);
        }
    }

    internal static void DrawCombatShots(IRenderTarget target, IReadOnlyList<CombatShotEvent> combatShots)
    {
        foreach (var shot in combatShots)
        {
            var color = shot.ProjectileKind switch
            {
                ProjectileKind.Ballistic => new Color(255, 200, 80),
                ProjectileKind.AirToGround => new Color(120, 220, 255),
                _ => new Color(255, 240, 160)
            };
            var from = ToScreen(shot.From);
            var to = ToScreen(shot.To);
            var line = new[]
            {
                new Vertex(from, color),
                new Vertex(to, color)
            };
            target.Draw(line, PrimitiveType.Lines);
        }
    }

    internal static Color GetTerrainColor(TerrainType terrain, VisibilityState visibility)
    {
        if (visibility == VisibilityState.Unknown)
        {
            return new Color(5, 7, 8);
        }

        var color = terrain switch
        {
            TerrainType.IronOre => new Color(96, 104, 112),
            TerrainType.CopperOre => new Color(158, 96, 48),
            TerrainType.Coal => new Color(42, 42, 44),
            TerrainType.Oil => new Color(40, 26, 52),
            _ => new Color(43, 74, 43)
        };

        return visibility == VisibilityState.Explored
            ? new Color((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2))
            : color;
    }

    internal static void DrawTechSignatures(IRenderTarget target, IReadOnlyList<TechSignatureHotspot> hotspots)
    {
        foreach (var hotspot in hotspots)
        {
            var intensity = Math.Min(180, 40 + hotspot.Intensity * 20);
            using var zone = new RectangleShape(new Vector2f(SfmlUiLayout.TileSize * 8f, SfmlUiLayout.TileSize * 8f))
            {
                Position = new Vector2f(hotspot.ZoneX * SfmlUiLayout.TileSize * 8f, hotspot.ZoneY * SfmlUiLayout.TileSize * 8f),
                FillColor = new Color(210, 80, 30, (byte)intensity)
            };
            target.Draw(zone);
        }
    }

    internal static bool IsVisibleToLocalPlayer(GameSimulation simulation, PlayerId localPlayer, WorldEntity entity)
    {
        return entity.OwnerId == localPlayer || simulation.GetVisibility(localPlayer, entity.Position) == VisibilityState.Visible;
    }

    internal static void DrawEntity(IRenderTarget target, GameSimulation simulation, WorldEntity entity, bool isSelected)
    {
        var drawKind = entity.Kind == EntityKind.GhostBuild && entity.BuildTargetKind is not null
            ? entity.BuildTargetKind.Value
            : entity.Kind;
        var footprint = MvpDefinitions.GetFootprint(drawKind);
        var isMobile = MvpDefinitions.UnitKinds.Contains(entity.Kind) || entity.Kind == EntityKind.Commander;
        var center = isMobile
            ? ToScreen(entity.WorldPosition)
            : new Vector2f(
                entity.Position.X * SfmlUiLayout.TileSize + footprint.Width * SfmlUiLayout.TileSize / 2f,
                entity.Position.Y * SfmlUiLayout.TileSize + footprint.Height * SfmlUiLayout.TileSize / 2f);
        var color = GetEntityColor(entity);
        var ink = new Color(245, 245, 245, 220);

        if (isMobile)
        {
            DrawMobileUnit(target, entity, center, color, ink);
            if (isSelected)
            {
                DrawMobileSelection(target, entity);
            }

            return;
        }

        var buildingRect = (
            Left: entity.Position.X * SfmlUiLayout.TileSize + 1f,
            Top: entity.Position.Y * SfmlUiLayout.TileSize + 1f,
            Width: SfmlUiLayout.TileSize * footprint.Width - 2f,
            Height: SfmlUiLayout.TileSize * footprint.Height - 2f);
        using var building = new RectangleShape(new Vector2f(buildingRect.Width, buildingRect.Height))
        {
            FillColor = color,
            OutlineColor = entity.Kind == EntityKind.GhostBuild ? new Color(120, 190, 255) : new Color(20, 20, 20),
            OutlineThickness = 1f,
            Position = new Vector2f(buildingRect.Left, buildingRect.Top)
        };
        target.Draw(building);

        if (entity.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter)
        {
            DrawDirectionArrow(target, entity.Position, entity.Direction, entity.Kind == EntityKind.Inserter ? new Color(255, 230, 100) : new Color(40, 40, 20));
        }
        else
        {
            EntityPictograms.DrawBuilding(target, drawKind, buildingRect.Left, buildingRect.Top, buildingRect.Width, buildingRect.Height, ink);
        }

        if (entity.Kind is (EntityKind.Conveyor or EntityKind.UndergroundConveyor) && entity.ConveyorItems.Count > 0)
        {
            DrawConveyorItems(target, entity, simulation.GetResolvedConveyorMoveTicks(entity));
        }

        if (entity.Kind == EntityKind.Inserter && entity.HeldItem is not null)
        {
            DrawHeldItem(target, entity.Position, entity.HeldItem.Value);
        }

        if (isSelected)
        {
            DrawSelection(target, entity, footprint);
        }
    }

    internal static void DrawMobileUnit(IRenderTarget target, WorldEntity entity, Vector2f center, Color color, Color ink)
    {
        var radius = (float)(MvpDefinitions.GetCollisionSize(entity.Kind).Radius * SfmlUiLayout.TileSize);
        if (radius < SfmlUiLayout.TileSize * 0.2f)
        {
            radius = SfmlUiLayout.TileSize * 0.2f;
        }

        if (entity.Kind == EntityKind.Commander)
        {
            using var unit = new CircleShape(radius)
            {
                FillColor = color,
                OutlineColor = entity.OwnerId == new PlayerId(1) ? Color.White : new Color(230, 140, 140),
                OutlineThickness = 2f,
                Origin = new Vector2f(radius, radius),
                Position = center
            };
            target.Draw(unit);
        }
        else if (entity.Kind == EntityKind.Scout)
        {
            using var unit = new ConvexShape(3)
            {
                FillColor = color,
                OutlineColor = entity.OwnerId == new PlayerId(1) ? Color.White : new Color(230, 140, 140),
                OutlineThickness = 2f,
                Position = center
            };
            unit.SetPoint(0, new Vector2f(0, -radius));
            unit.SetPoint(1, new Vector2f(radius * 0.9f, radius * 0.75f));
            unit.SetPoint(2, new Vector2f(-radius * 0.9f, radius * 0.75f));
            target.Draw(unit);
        }
        else
        {
            var side = radius * 1.7f;
            using var unit = new RectangleShape(new Vector2f(side, side))
            {
                FillColor = color,
                OutlineColor = entity.OwnerId == new PlayerId(1) ? Color.White : new Color(230, 140, 140),
                OutlineThickness = 2f,
                Origin = new Vector2f(side / 2f, side / 2f),
                Position = center
            };
            target.Draw(unit);
        }

        EntityPictograms.DrawUnitMark(target, entity.Kind, center, radius, ink);
    }

    internal static void DrawGhostPreview(IRenderTarget target, EntityKind kind, TilePosition anchor, Direction pendingDirection)
    {
        var footprint = MvpDefinitions.GetFootprint(kind);
        using var preview = new RectangleShape(new Vector2f(SfmlUiLayout.TileSize * footprint.Width - 2f, SfmlUiLayout.TileSize * footprint.Height - 2f))
        {
            Position = new Vector2f(anchor.X * SfmlUiLayout.TileSize + 1f, anchor.Y * SfmlUiLayout.TileSize + 1f),
            FillColor = new Color(80, 150, 220, 80),
            OutlineColor = new Color(160, 220, 255),
            OutlineThickness = 2f
        };
        target.Draw(preview);

        if (BuildBarModel.IsDirectedKind(kind))
        {
            DrawDirectionArrow(target, anchor, pendingDirection, new Color(255, 240, 120));
        }
    }

    internal static void DrawSelection(IRenderTarget target, WorldEntity entity, WorldSize footprint)
    {
        using var outline = new RectangleShape(new Vector2f(SfmlUiLayout.TileSize * footprint.Width - 1f, SfmlUiLayout.TileSize * footprint.Height - 1f))
        {
            Position = new Vector2f(entity.Position.X * SfmlUiLayout.TileSize, entity.Position.Y * SfmlUiLayout.TileSize),
            FillColor = Color.Transparent,
            OutlineColor = new Color(255, 245, 120),
            OutlineThickness = 2f
        };
        target.Draw(outline);
    }

    internal static void DrawMobileSelection(IRenderTarget target, WorldEntity entity)
    {
        var center = ToScreen(entity.WorldPosition);
        var radius = (float)(MvpDefinitions.GetCollisionSize(entity.Kind).Radius * SfmlUiLayout.TileSize) + SfmlUiLayout.TileSize * 0.1f;
        if (radius < SfmlUiLayout.TileSize * 0.3f)
        {
            radius = SfmlUiLayout.TileSize * 0.3f;
        }
        if (entity.Kind == EntityKind.Commander)
        {
            using var outline = new CircleShape(radius)
            {
                Position = center,
                Origin = new Vector2f(radius, radius),
                FillColor = Color.Transparent,
                OutlineColor = new Color(255, 245, 120),
                OutlineThickness = 2f
            };
            target.Draw(outline);
            return;
        }

        if (entity.Kind == EntityKind.Scout)
        {
            using var outline = new ConvexShape(3)
            {
                Position = center,
                FillColor = Color.Transparent,
                OutlineColor = new Color(255, 245, 120),
                OutlineThickness = 2f
            };
            outline.SetPoint(0, new Vector2f(0, -radius));
            outline.SetPoint(1, new Vector2f(radius * 0.95f, radius * 0.8f));
            outline.SetPoint(2, new Vector2f(-radius * 0.95f, radius * 0.8f));
            target.Draw(outline);
            return;
        }

        var side = radius * 1.8f;
        using var square = new RectangleShape(new Vector2f(side, side))
        {
            Position = center,
            Origin = new Vector2f(side / 2f, side / 2f),
            FillColor = Color.Transparent,
            OutlineColor = new Color(255, 245, 120),
            OutlineThickness = 2f
        };
        target.Draw(square);
    }

    internal static Vector2f ToScreen(WorldPosition position)
    {
        return new Vector2f((float)(position.X * SfmlUiLayout.TileSize), (float)(position.Y * SfmlUiLayout.TileSize));
    }

    internal static void DrawDirectionArrow(IRenderTarget target, TilePosition position, Direction direction, Color color)
    {
        var center = new Vector2f(position.X * SfmlUiLayout.TileSize + SfmlUiLayout.TileSize / 2f, position.Y * SfmlUiLayout.TileSize + SfmlUiLayout.TileSize / 2f);
        var points = direction switch
        {
            Direction.North => new[] { new Vector2f(0, -8), new Vector2f(7, 7), new Vector2f(-7, 7) },
            Direction.East => new[] { new Vector2f(8, 0), new Vector2f(-7, 7), new Vector2f(-7, -7) },
            Direction.South => new[] { new Vector2f(0, 8), new Vector2f(7, -7), new Vector2f(-7, -7) },
            Direction.West => new[] { new Vector2f(-8, 0), new Vector2f(7, 7), new Vector2f(7, -7) },
            _ => Array.Empty<Vector2f>()
        };

        using var arrow = new ConvexShape(3)
        {
            FillColor = color,
            Position = center
        };
        for (uint i = 0; i < points.Length; i++)
        {
            arrow.SetPoint(i, points[i]);
        }

        target.Draw(arrow);
    }

    internal static void DrawConveyorItems(IRenderTarget target, WorldEntity conveyor, int moveTicks)
    {
        var items = conveyor.ConveyorItems;
        var (dirX, dirY) = conveyor.Direction switch
        {
            Direction.East => (1f, 0f),
            Direction.West => (-1f, 0f),
            Direction.South => (0f, 1f),
            Direction.North => (0f, -1f),
            _ => (1f, 0f)
        };

        var centerX = conveyor.Position.X * SfmlUiLayout.TileSize + SfmlUiLayout.TileSize / 2f;
        var centerY = conveyor.Position.Y * SfmlUiLayout.TileSize + SfmlUiLayout.TileSize / 2f;

        for (var i = 0; i < items.Count && i < 2; i++)
        {
            var t = moveTicks <= 0 ? 1f : Math.Clamp(items[i].ProgressTicks / (float)moveTicks, 0f, 1f);
            // Slot bases at 25% / 75% along belt; progress advances within the slot band toward the exit edge.
            var slotBase = 0.25f + 0.5f * i;
            var along = Math.Clamp(slotBase + t * 0.45f, 0.08f, 0.92f);
            var alongOffset = (along - 0.5f) * SfmlUiLayout.TileSize;
            var posX = centerX + dirX * alongOffset;
            var posY = centerY + dirY * alongOffset;
            using var marker = new CircleShape(SfmlUiLayout.TileSize * 0.16f)
            {
                FillColor = GetItemColor(items[i].Item),
                Origin = new Vector2f(SfmlUiLayout.TileSize * 0.16f, SfmlUiLayout.TileSize * 0.16f),
                Position = new Vector2f(posX, posY)
            };
            target.Draw(marker);
        }
    }

    internal static void DrawHeldItem(IRenderTarget target, TilePosition position, ItemId item)
    {
        using var marker = new CircleShape(SfmlUiLayout.TileSize * 0.14f)
        {
            FillColor = GetItemColor(item),
            OutlineColor = Color.White,
            OutlineThickness = 1f,
            Origin = new Vector2f(SfmlUiLayout.TileSize * 0.14f, SfmlUiLayout.TileSize * 0.14f),
            Position = new Vector2f(position.X * SfmlUiLayout.TileSize + SfmlUiLayout.TileSize * 0.75f, position.Y * SfmlUiLayout.TileSize + SfmlUiLayout.TileSize * 0.25f)
        };
        target.Draw(marker);
    }

    internal static Color GetEntityColor(WorldEntity entity)
    {
        return entity.Kind switch
        {
            EntityKind.Commander => entity.OwnerId == new PlayerId(1) ? new Color(70, 186, 255) : new Color(220, 70, 70),
            EntityKind.GhostBuild => new Color(70, 120, 180, 150),
            EntityKind.Bastion => new Color(95, 95, 190),
            EntityKind.Hub => new Color(180, 160, 90),
            EntityKind.Mine or EntityKind.CoalMine or EntityKind.OilWell => new Color(130, 120, 90),
            EntityKind.Smelter or EntityKind.Refinery => new Color(190, 110, 50),
            EntityKind.Assembler => new Color(150, 125, 60),
            EntityKind.SolarPanel => new Color(60, 100, 180),
            EntityKind.CoalPlant => new Color(70, 70, 70),
            EntityKind.Conveyor or EntityKind.UndergroundConveyor => new Color(180, 150, 45),
            EntityKind.Inserter => new Color(210, 180, 70),
            EntityKind.TankFactory or EntityKind.DroneCenter => new Color(120, 120, 150),
            EntityKind.Laboratory => new Color(110, 80, 170),
            EntityKind.Wall or EntityKind.SteelWall => new Color(100, 105, 110),
            EntityKind.MachineGunTurret or EntityKind.CannonTurret or EntityKind.AntiAirTurret => new Color(170, 80, 70),
            EntityKind.LightBot or EntityKind.MediumBot => new Color(120, 210, 110),
            EntityKind.BasicTank or EntityKind.MediumTank => new Color(70, 150, 90),
            EntityKind.Scout => new Color(230, 220, 90),
            EntityKind.AntiAirBot or EntityKind.RocketLauncher => new Color(90, 180, 170),
            _ => Color.Magenta
        };
    }

    internal static Color GetItemColor(ItemId item)
    {
        return item switch
        {
            ItemId.IronOre or ItemId.IronPlate => new Color(180, 190, 200),
            ItemId.CopperOre or ItemId.CopperPlate => new Color(210, 120, 50),
            ItemId.Coal => new Color(20, 20, 24),
            ItemId.CrudeOil or ItemId.Fuel => new Color(90, 60, 130),
            ItemId.Steel => new Color(140, 150, 160),
            ItemId.IronGear => new Color(170, 170, 150),
            ItemId.Composite => new Color(80, 190, 100),
            ItemId.SciencePackT1 => new Color(80, 180, 255),
            ItemId.SciencePackT2 => new Color(255, 180, 80),
            _ => Color.White
        };
    }

    internal static bool IsUnitDrawKind(EntityKind kind) =>
        MvpDefinitions.UnitKinds.Contains(kind) || kind == EntityKind.Commander;

}
