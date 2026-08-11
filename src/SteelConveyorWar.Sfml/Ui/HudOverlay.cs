using SFML.Graphics;
using SFML.System;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

internal enum SidebarStorageKind { Input, Output }
internal readonly record struct SidebarStorageHit(FloatRect Bounds, ItemId Item, SidebarStorageKind Kind);

internal static class HudOverlay
{
    private static readonly Color MinimapClearColor = new(8, 10, 12, 220);
    private static readonly RectangleShape MinimapPixel = new();
    private static readonly List<MinimapEntitySnapshot> MinimapEntityScratch = new();
    private static readonly List<MinimapEntitySnapshot> MinimapPreviousEntities = new();

    private static RenderTexture? _minimapTexture;
    private static Sprite? _minimapSprite;
    private static PlayerId? _minimapCachedPlayer;
    private static int _minimapCachedMapW;
    private static int _minimapCachedMapH;
    private static uint _minimapCachedSize;
    private static int _minimapFramesSinceFull;

    internal static void DrawTopBar(IRenderTarget target, GameSimulation simulation, PlayerId localPlayer, Font? font, float playfieldWidth)
    {
        using var bar = new RectangleShape(new Vector2f(playfieldWidth, SfmlUiLayout.TopBarHeight))
        {
            Position = new Vector2f(0f, 0f),
            FillColor = new Color(12, 16, 22, 235),
            OutlineColor = new Color(80, 95, 120),
            OutlineThickness = 1f
        };
        target.Draw(bar);
        if (font is null)
        {
            return;
        }

        var player = simulation.GetPlayer(localPlayer);
        var commander = simulation.World.Entities.FirstOrDefault(entity =>
            entity.OwnerId == localPlayer && entity.Kind == EntityKind.Commander && entity.IsAlive);
        var stock = commander is null
            ? "BMK: -"
            : "BMK: " + string.Join(" ", commander.Inventory.Items
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{ShortItem(pair.Key)}:{pair.Value}"));

        using var energy = new Text(font, $"Energy {player.PowerProduced}/{player.PowerDemand}", 14)
        {
            FillColor = player.PowerDemand > player.PowerProduced ? new Color(220, 70, 70) : Color.White,
            Position = new Vector2f(10f, 8f)
        };
        target.Draw(energy);
        using var inventory = new Text(font, stock, 13)
        {
            FillColor = new Color(210, 220, 230),
            Position = new Vector2f(180f, 9f)
        };
        target.Draw(inventory);
    }

    internal static void DrawMinimap(
        IRenderTarget target,
        GameSimulation simulation,
        PlayerId localPlayer,
        uint windowWidth,
        float cameraX,
        float cameraY,
        float playfieldWidth,
        float playfieldHeight)
    {
        var world = simulation.World;
        var mapW = world.Size.Width;
        var mapH = world.Size.Height;
        if (mapW <= 0 || mapH <= 0)
        {
            return;
        }

        var left = windowWidth - SfmlUiLayout.MinimapSize;
        var top = 0f;
        var minimapSize = (uint)SfmlUiLayout.MinimapSize;

        using var frame = new RectangleShape(new Vector2f(SfmlUiLayout.MinimapSize, SfmlUiLayout.MinimapSize))
        {
            Position = new Vector2f(left, top),
            FillColor = MinimapClearColor,
            OutlineColor = new Color(90, 105, 130),
            OutlineThickness = 1f
        };
        target.Draw(frame);

        var scaleX = SfmlUiLayout.MinimapSize / mapW;
        var scaleY = SfmlUiLayout.MinimapSize / mapH;
        var pixelW = Math.Max(1f, scaleX);
        var pixelH = Math.Max(1f, scaleY);

        CollectMinimapEntitySnapshots(simulation, localPlayer, MinimapEntityScratch);

        var needsFull = MinimapDirtyTracker.NeedsFullRebuild(
            hasCache: _minimapTexture is not null,
            cacheSize: _minimapCachedSize,
            currentSize: minimapSize,
            cacheMapWidth: _minimapCachedMapW,
            cacheMapHeight: _minimapCachedMapH,
            mapWidth: mapW,
            mapHeight: mapH,
            cachePlayer: _minimapCachedPlayer,
            currentPlayer: localPlayer,
            framesSinceFullRebuild: _minimapFramesSinceFull);

        var fogDirty = simulation.GetFogDirtyTiles(localPlayer);
        var plan = MinimapDirtyTracker.Plan(
            needsFull,
            fogDirty,
            MinimapPreviousEntities,
            MinimapEntityScratch);

        EnsureMinimapTexture(minimapSize, mapW, mapH, localPlayer);

        var texture = _minimapTexture!;
        switch (plan.Kind)
        {
            case MinimapRedrawKind.FullRebuild:
                texture.Clear(MinimapClearColor);
                DrawMinimapTerrainFull(texture, simulation, localPlayer, mapW, mapH, scaleX, scaleY, pixelW, pixelH);
                DrawMinimapEntities(texture, simulation, localPlayer, scaleX, scaleY, pixelW, pixelH);
                texture.Display();
                _minimapFramesSinceFull = 0;
                break;

            case MinimapRedrawKind.PatchTiles:
                DrawMinimapTerrainTiles(
                    texture,
                    simulation,
                    localPlayer,
                    plan.TilesToRedraw,
                    mapW,
                    mapH,
                    scaleX,
                    scaleY,
                    pixelW,
                    pixelH);
                // Terrain patches overwrite markers on those tiles; refresh the full entity layer.
                DrawMinimapEntities(texture, simulation, localPlayer, scaleX, scaleY, pixelW, pixelH);
                texture.Display();
                _minimapFramesSinceFull++;
                break;

            default:
                _minimapFramesSinceFull++;
                break;
        }

        MinimapPreviousEntities.Clear();
        MinimapPreviousEntities.AddRange(MinimapEntityScratch);

        _minimapSprite!.Position = new Vector2f(left, top);
        target.Draw(_minimapSprite);

        var viewLeft = cameraX / SfmlUiLayout.TileSize;
        var viewTop = cameraY / SfmlUiLayout.TileSize;
        var viewWidth = playfieldWidth / SfmlUiLayout.TileSize;
        var viewHeight = playfieldHeight / SfmlUiLayout.TileSize;
        using var viewport = new RectangleShape(new Vector2f(
            Math.Max(2f, viewWidth * scaleX),
            Math.Max(2f, viewHeight * scaleY)))
        {
            Position = new Vector2f(left + viewLeft * scaleX, top + viewTop * scaleY),
            FillColor = Color.Transparent,
            OutlineColor = new Color(255, 245, 180),
            OutlineThickness = 1f
        };
        target.Draw(viewport);
    }

    private static void EnsureMinimapTexture(uint size, int mapW, int mapH, PlayerId localPlayer)
    {
        if (_minimapTexture is not null
            && _minimapCachedSize == size
            && _minimapCachedMapW == mapW
            && _minimapCachedMapH == mapH
            && _minimapCachedPlayer == localPlayer)
        {
            return;
        }

        _minimapSprite?.Dispose();
        _minimapSprite = null;
        _minimapTexture?.Dispose();
        _minimapTexture = new RenderTexture(new Vector2u(size, size));
        _minimapSprite = new Sprite(_minimapTexture.Texture);
        _minimapCachedSize = size;
        _minimapCachedMapW = mapW;
        _minimapCachedMapH = mapH;
        _minimapCachedPlayer = localPlayer;
        _minimapFramesSinceFull = MinimapDirtyTracker.DefaultFullRebuildInterval;
        MinimapPreviousEntities.Clear();
    }

    private static void CollectMinimapEntitySnapshots(
        GameSimulation simulation,
        PlayerId localPlayer,
        List<MinimapEntitySnapshot> into)
    {
        into.Clear();
        var localTeam = simulation.GetPlayer(localPlayer).TeamId;
        var entities = simulation.World.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (!entity.IsAlive || entity.IsGarrisoned || entity.OwnerId is null)
            {
                continue;
            }

            // Match main playfield FoW: explored tiles keep terrain, but live enemy/ally
            // positions only render while Visible (GDD §13).
            if (!WorldRenderer.IsVisibleToLocalPlayer(simulation, localPlayer, entity))
            {
                continue;
            }

            var owner = entity.OwnerId.Value;
            byte markerKind;
            if (owner == localPlayer)
            {
                markerKind = 0;
            }
            else if (simulation.GetPlayer(owner).TeamId == localTeam)
            {
                markerKind = 1;
            }
            else
            {
                markerKind = 2;
            }

            var footprint = MvpDefinitions.GetFootprint(entity.Kind);
            into.Add(new MinimapEntitySnapshot(
                entity.Id,
                entity.Position.X,
                entity.Position.Y,
                Math.Max(1, footprint.Width),
                Math.Max(1, footprint.Height),
                markerKind));
        }

        into.Sort(static (a, b) => a.EntityId.CompareTo(b.EntityId));
    }

    private static void DrawMinimapTerrainFull(
        RenderTexture texture,
        GameSimulation simulation,
        PlayerId localPlayer,
        int mapW,
        int mapH,
        float scaleX,
        float scaleY,
        float pixelW,
        float pixelH)
    {
        for (var y = 0; y < mapH; y++)
        {
            for (var x = 0; x < mapW; x++)
            {
                DrawMinimapTerrainTile(
                    texture,
                    simulation,
                    localPlayer,
                    new TilePosition(x, y),
                    scaleX,
                    scaleY,
                    pixelW,
                    pixelH);
            }
        }
    }

    private static void DrawMinimapTerrainTiles(
        RenderTexture texture,
        GameSimulation simulation,
        PlayerId localPlayer,
        IReadOnlyList<TilePosition> tiles,
        int mapW,
        int mapH,
        float scaleX,
        float scaleY,
        float pixelW,
        float pixelH)
    {
        for (var i = 0; i < tiles.Count; i++)
        {
            var position = tiles[i];
            if (position.X < 0 || position.Y < 0 || position.X >= mapW || position.Y >= mapH)
            {
                continue;
            }

            DrawMinimapTerrainTile(
                texture,
                simulation,
                localPlayer,
                position,
                scaleX,
                scaleY,
                pixelW,
                pixelH);
        }
    }

    private static void DrawMinimapTerrainTile(
        RenderTexture texture,
        GameSimulation simulation,
        PlayerId localPlayer,
        TilePosition position,
        float scaleX,
        float scaleY,
        float pixelW,
        float pixelH)
    {
        var visibility = simulation.GetVisibility(localPlayer, position);
        MinimapPixel.Size = new Vector2f(pixelW, pixelH);
        MinimapPixel.Position = new Vector2f(position.X * scaleX, position.Y * scaleY);
        MinimapPixel.FillColor = visibility == VisibilityState.Unknown
            ? MinimapClearColor
            : GetMinimapTerrainColor(simulation.World.GetTerrain(position), visibility);
        texture.Draw(MinimapPixel);
    }

    private static void DrawMinimapEntities(
        RenderTexture texture,
        GameSimulation simulation,
        PlayerId localPlayer,
        float scaleX,
        float scaleY,
        float pixelW,
        float pixelH)
    {
        var localTeam = simulation.GetPlayer(localPlayer).TeamId;
        var entities = simulation.World.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (!entity.IsAlive || entity.IsGarrisoned || entity.OwnerId is null)
            {
                continue;
            }

            if (!WorldRenderer.IsVisibleToLocalPlayer(simulation, localPlayer, entity))
            {
                continue;
            }

            var owner = entity.OwnerId.Value;
            Color buildingColor;
            if (owner == localPlayer)
            {
                buildingColor = new Color(70, 160, 255);
            }
            else if (simulation.GetPlayer(owner).TeamId == localTeam)
            {
                buildingColor = new Color(255, 0, 255);
            }
            else
            {
                buildingColor = new Color(220, 50, 50);
            }

            var footprint = MvpDefinitions.GetFootprint(entity.Kind);
            var w = Math.Max(pixelW, footprint.Width * scaleX);
            var h = Math.Max(pixelH, footprint.Height * scaleY);
            MinimapPixel.Size = new Vector2f(w, h);
            MinimapPixel.Position = new Vector2f(entity.Position.X * scaleX, entity.Position.Y * scaleY);
            MinimapPixel.FillColor = buildingColor;
            texture.Draw(MinimapPixel);
        }
    }

    internal static Color GetMinimapTerrainColor(TerrainType terrain, VisibilityState visibility)
    {
        var color = terrain switch
        {
            TerrainType.IronOre => new Color(110, 120, 130),
            TerrainType.CopperOre => new Color(170, 110, 55),
            TerrainType.Coal => new Color(55, 55, 58),
            TerrainType.Oil => new Color(70, 45, 95),
            _ => new Color(48, 78, 48)
        };

        return visibility == VisibilityState.Explored
            ? new Color((byte)(color.R * 2 / 3), (byte)(color.G * 2 / 3), (byte)(color.B * 2 / 3))
            : color;
    }

    internal static string ShortItem(ItemId item)
    {
        return item switch
        {
            ItemId.IronPlate => "Fe",
            ItemId.CopperPlate => "Cu",
            ItemId.Coal => "Coal",
            ItemId.Steel => "St",
            ItemId.IronOre => "FeOre",
            ItemId.CopperOre => "CuOre",
            _ => item.ToString()
        };
    }

    internal static void DrawHud(
        IRenderTarget target,
        GameSimulation simulation,
        PlayerId localPlayer,
        int? selectedEntityId,
        bool isBuildMenuOpen,
        EntityKind? pendingBuildKind,
        Direction pendingDirection,
        ItemRecipeId? pendingRecipe,
        int recipePage,
        int templateUnitIndex,
        BastionPendingInputMode bastionPendingMode,
        int patrolWaypointCount,
        Font? font,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        float panelTop,
        bool isResearchOverlayOpen,
        TechnologyId? researchSelectedId,
        List<SidebarStorageHit> sidebarStorageHits)
    {
        DrawPanel(target, windowWidth, windowHeight, panelX, panelTop);
        sidebarStorageHits.Clear();
        if (font is null)
        {
            return;
        }

        var lines = new List<string>();
        var lineItemTags = new Dictionary<int, (ItemId Item, SidebarStorageKind Kind)>();
        var textStartY = panelTop + SfmlUiLayout.HudTextStartY;

        if (isResearchOverlayOpen)
        {
            var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
            var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
            var tree = ResearchTreePanelModel.FromSnapshot(
                simulation.GetResearchSnapshot(localPlayer),
                overlayBounds,
                researchSelectedId);
            lines.AddRange(tree.ToDetailLines());
            if (tree.SupportsAllocationToggle)
            {
                lines.Add("Alloc: use overlay Alloc button");
            }

            var displayResearchLines = WrapHudLines(lines, SfmlUiLayout.HudWrapCharacters).ToList();
            DrawTextLines(target, font, displayResearchLines, panelX, textStartY);
            return;
        }

        var selected = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
        // R27: never read live data for a selected entity the local player cannot currently see.
        // The session also clears such selections each tick; this is a defensive second gate so the
        // HUD can never leak a hidden enemy's HP/energy/position/orders.
        if (selected is not null && !WorldRenderer.IsVisibleToLocalPlayer(simulation, localPlayer, selected))
        {
            selected = null;
        }

        if (selected is not null)
        {
            lines.Add($"Selected: {selected.Kind} #{selected.Id}");
            lines.Add($"Owner: {(selected.OwnerId?.Value.ToString() ?? "-")}");
            lines.Add($"HP: {selected.Health}/{selected.MaxHealth}");
            lines.Add($"Footprint: {MvpDefinitions.GetFootprint(selected.Kind).Width}x{MvpDefinitions.GetFootprint(selected.Kind).Height}");
            if (selected.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter)
            {
                lines.Add($"Direction: {selected.Direction}");
            }

            var demand = MvpDefinitions.PowerDemand.GetValueOrDefault(selected.Kind);
            var production = MvpDefinitions.PowerProduction.GetValueOrDefault(selected.Kind);
            if (demand > 0)
            {
                lines.Add($"Power demand: {demand}");
                lines.Add($"Energy: {selected.EnergyBuffer}/{selected.EnergyBufferCapacity}");
            }

            if (production > 0)
            {
                lines.Add($"Power output: {production}");
            }

            if (selected.OwnerId is not null)
            {
                var owner = simulation.GetPlayer(selected.OwnerId.Value);
                lines.Add($"Grid: {owner.PowerProduced}/{owner.PowerDemand}");
            }

            lines.Add($"Output: {(selected.PendingOutputItem?.ToString() ?? "-")}");
            if (selected.WorkTicksTotal > 0)
            {
                var done = selected.WorkTicksTotal - selected.WorkTicksRemaining;
                lines.Add($"Craft: {done}/{selected.WorkTicksTotal}");
            }

            if (selected.ProductionTargetKind is not null)
            {
                lines.Add($"Producing: {selected.ProductionTargetKind}");
            }

            if (selected.Kind == EntityKind.Commander)
            {
                lines.Add($"Build radius: {MvpDefinitions.CommanderBuildRadius}");
                lines.Add($"World: {selected.WorldPosition.ToTileSpaceX():0.00},{selected.WorldPosition.ToTileSpaceY():0.00}");
                lines.Add($"Move target: {(selected.MoveTarget is null ? "-" : $"{selected.MoveTarget.Value.X},{selected.MoveTarget.Value.Y}")}");
                lines.Add($"Queued: {(selected.QueuedBuildOrder is null ? "-" : $"{selected.QueuedBuildOrder.TargetKind}@{selected.QueuedBuildOrder.TargetPosition.X},{selected.QueuedBuildOrder.TargetPosition.Y}")}");
                lines.Add($"DemolishQ: {(selected.QueuedDemolishOrder is null ? "-" : $"#{selected.QueuedDemolishOrder.TargetEntityId}")}");
                lines.Add("S: stop");
                lines.Add("B: build menu");
                lines.Add("Q: copy hovered building");
                lines.Add("F1: select BMK + center");
                lines.Add("T: research tree");
                lines.Add("RMB: move");
                lines.Add("Ctrl+LMB: withdraw hub/output");
                lines.Add("Ctrl+RMB: deposit hub/input");
                lines.Add("Sidebar RMB input / LMB output");
                lines.Add("Arrows/MMB/edge: pan camera");
            }

            if (IsCombatHudKind(selected.Kind))
            {
                var stats = MvpDefinitions.GetStats(selected.Kind);
                lines.Add($"Projectile: {stats.ProjectileKind}");
                lines.Add($"Vision: {stats.VisionRadius}");
                lines.Add($"Damage: {stats.AttackDamage}");
                lines.Add($"Fire rate: {stats.AttackCooldownTicks}t");
                lines.Add($"Splash: {stats.SplashRadius}");
                lines.Add($"Armor: {stats.Armor}");
            }

            if (selected.Kind == EntityKind.Assembler)
            {
                lines.Add($"Recipe: {(selected.SelectedItemRecipe?.ToString() ?? "none")}");
                lines.Add("1-4: set assembler recipe");
            }

            if (selected.Kind == EntityKind.Smelter)
            {
                lines.Add($"Smelt recipe: {(selected.ActiveSmeltRecipe?.ToString() ?? "none")}");
            }

            if (MvpDefinitions.FactoryKinds.Contains(selected.Kind))
            {
                lines.Add($"Manual produce: {(selected.IsManualProductionTarget ? "yes" : "no")}");
                lines.Add($"Recipe: {(selected.ProductionTargetKind?.ToString() ?? "none")}");
                lines.Add("1-N: set factory recipe");
            }

            if (selected.Kind == EntityKind.Bastion)
            {
                var ownerId = selected.OwnerId ?? localPlayer;
                var capacity = simulation.GetBastionTemplateCapacity(ownerId);
                var templateSum = selected.BastionTemplate.Values.Sum();
                lines.Add($"Order: {BastionOrderBarModel.FormatOrder(selected.Order)}");
                lines.Add($"Template: {templateSum}/{capacity}");
                var unlocked = BastionCompositionPanelModel.UnlockedUnitKinds(simulation, ownerId);
                if (unlocked.Length > 0)
                {
                    var unitKind = unlocked[Math.Clamp(templateUnitIndex, 0, unlocked.Length - 1)];
                    var unitCount = selected.BastionTemplate.GetValueOrDefault(unitKind);
                    var live = simulation.GetBastionUnitSupply(selected.Id, unitKind);
                    lines.Add($"Edit: {unitKind} = {live}/{unitCount}");
                }

                lines.Add("Center panel: +/- template max");
                lines.Add("[/]: unit type  +/-: count");
                lines.Add("A/S/D/F: Attack/Scout/Defend/Patrol");
                lines.Add("1-0: switch owned bastions");
                var pendingHint = BastionOrderBarModel.PendingHint(bastionPendingMode, patrolWaypointCount);
                if (!string.IsNullOrEmpty(pendingHint))
                {
                    lines.Add(pendingHint);
                }
            }

            if (selected.Kind == EntityKind.Laboratory)
            {
                lines.Add("T: open research tree");
            }

            AddRecipeLines(lines, GetRecipeLines(selected, simulation, localPlayer, recipePage), recipePage: 0);

            if (MvpDefinitions.HasPlayerInventory(selected.Kind))
            {
                lines.Add("Inventory:");
                if (selected.Kind == EntityKind.Hub)
                {
                    AddHubInventoryLinesWithHits(lines, lineItemTags, selected.Inventory, SidebarStorageKind.Input);
                }
                else
                {
                    AddInventoryLines(lines, selected.Inventory);
                }
            }

            if (selected.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter)
            {
                lines.Add("R / Shift+R: rotate");
            }

            if (selected.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor)
            {
                lines.Add($"Conveyor slots: {selected.ConveyorItems.Count}/{MvpDefinitions.ConveyorMaxItemsPerTile}");
                foreach (var slot in selected.ConveyorItems.Take(2))
                {
                    lines.Add($"  {slot.Item} ({slot.ProgressTicks}/{MvpDefinitions.ConveyorMoveTicks})");
                }
            }

            if (selected.Kind == EntityKind.Inserter)
            {
                lines.Add($"Held: {(selected.HeldItem?.ToString() ?? "-")}");
                lines.Add($"Transfer: {selected.HeldTransferTicksRemaining}/{MvpDefinitions.InserterTransferTicks}");
            }

            if (selected.InputBuffer.Items.Count > 0
                || selected.OutputBuffer.Items.Count > 0
                || IsBufferedBuildingForUi(selected.Kind)
                || TryGetRecipeInputNeeds(selected, simulation, localPlayer, out _))
            {
                lines.Add("Input:");
                if (TryGetRecipeInputNeeds(selected, simulation, localPlayer, out var needs))
                {
                    AddRecipeInputLinesWithHits(lines, lineItemTags, selected.InputBuffer, needs);
                }
                else
                {
                    AddInventoryLinesWithHits(lines, lineItemTags, selected.InputBuffer, SidebarStorageKind.Input);
                }

                lines.Add("Output:");
                AddInventoryLinesWithHits(lines, lineItemTags, selected.OutputBuffer, SidebarStorageKind.Output);
            }
        }

        if (isBuildMenuOpen)
        {
            lines.Add("");
            lines.Add("Build mode:");
            lines.Add($"Pending: {pendingBuildKind}");
            if (pendingBuildKind is not null && BuildBarModel.IsDirectedKind(pendingBuildKind.Value))
            {
                lines.Add($"Ghost dir: {pendingDirection} (R/Shift+R)");
            }

            if (pendingBuildKind == EntityKind.Assembler && pendingRecipe is not null)
            {
                lines.Add($"Ghost recipe: {pendingRecipe}");
            }

            lines.Add("Bottom bar: pick building");
            lines.Add("LMB: place/queue build");
            lines.Add("RMB hold 1s: demolish");
            lines.Add("S: stop build/demolish/move");
        }

        var displayLines = new List<string>();
        var panelWidth = windowWidth - panelX;
        for (var rawIndex = 0; rawIndex < lines.Count; rawIndex++)
        {
            var wrappedChunk = WrapHudLines(new[] { lines[rawIndex] }, SfmlUiLayout.HudWrapCharacters).ToList();
            if (lineItemTags.TryGetValue(rawIndex, out var tag) && displayLines.Count < SfmlUiLayout.MaxHudLines)
            {
                var bounds = new FloatRect(
                    new Vector2f(panelX + 4f, textStartY + displayLines.Count * SfmlUiLayout.HudLineHeight),
                    new Vector2f(panelWidth - 8f, SfmlUiLayout.HudLineHeight));
                sidebarStorageHits.Add(new SidebarStorageHit(bounds, tag.Item, tag.Kind));
            }

            displayLines.AddRange(wrappedChunk);
        }

        DrawTextLines(target, font, displayLines, panelX, textStartY);
        if (selected is not null)
        {
            DrawBuildingProgressBars(target, selected, panelX, panelTop, windowWidth);
        }
    }

    internal static void AddInventoryLinesWithHits(
        List<string> lines,
        Dictionary<int, (ItemId Item, SidebarStorageKind Kind)> lineItemTags,
        Inventory inventory,
        SidebarStorageKind kind)
    {
        if (inventory.Items.Count == 0)
        {
            lines.Add("  -");
            return;
        }

        foreach (var item in inventory.Items.OrderBy(pair => pair.Key).Take(8))
        {
            var lineIndex = lines.Count;
            lines.Add($"  {item.Key}: {item.Value}/{MvpDefinitions.GetMaxStackSize(item.Key)}");
            lineItemTags[lineIndex] = (item.Key, kind);
        }
    }

    internal static void AddHubInventoryLinesWithHits(
        List<string> lines,
        Dictionary<int, (ItemId Item, SidebarStorageKind Kind)> lineItemTags,
        Inventory inventory,
        SidebarStorageKind kind)
    {
        if (inventory.Items.Count == 0)
        {
            lines.Add("  -");
            return;
        }

        var lineBudget = 12;
        foreach (var item in inventory.Items.OrderBy(pair => pair.Key))
        {
            var maxStack = MvpDefinitions.GetMaxStackSize(item.Key);
            var remaining = item.Value;
            if (remaining <= 0)
            {
                continue;
            }

            while (remaining > 0 && lineBudget > 0)
            {
                var stack = Math.Min(maxStack, remaining);
                var lineIndex = lines.Count;
                lines.Add($"  {item.Key}: {stack}/{maxStack}");
                lineItemTags[lineIndex] = (item.Key, kind);
                remaining -= stack;
                lineBudget--;
            }

            if (lineBudget <= 0)
            {
                break;
            }
        }
    }

    internal static void AddRecipeInputLinesWithHits(
        List<string> lines,
        Dictionary<int, (ItemId Item, SidebarStorageKind Kind)> lineItemTags,
        Inventory inputBuffer,
        IReadOnlyDictionary<ItemId, int> needs)
    {
        if (needs.Count == 0)
        {
            lines.Add("  -");
            return;
        }

        foreach (var need in needs.OrderBy(pair => pair.Key))
        {
            var count = inputBuffer.Count(need.Key);
            var lineIndex = lines.Count;
            lines.Add($"  {need.Key}: {count}/{need.Value}");
            lineItemTags[lineIndex] = (need.Key, SidebarStorageKind.Input);
        }
    }

    internal static bool TryGetRecipeInputNeeds(
        WorldEntity selected,
        GameSimulation simulation,
        PlayerId localPlayer,
        out IReadOnlyDictionary<ItemId, int> needs)
    {
        switch (selected.Kind)
        {
            case EntityKind.Assembler when selected.SelectedItemRecipe is not null
                && MvpDefinitions.ItemRecipes.TryGetValue(selected.SelectedItemRecipe.Value, out var itemRecipe):
                needs = itemRecipe.Inputs;
                return true;

            case EntityKind.Smelter when selected.ActiveSmeltRecipe is not null:
                needs = selected.ActiveSmeltRecipe.Value switch
                {
                    SmeltRecipeId.IronPlate => new Dictionary<ItemId, int> { [ItemId.IronOre] = 1 },
                    SmeltRecipeId.CopperPlate => new Dictionary<ItemId, int> { [ItemId.CopperOre] = 1 },
                    SmeltRecipeId.Steel => new Dictionary<ItemId, int> { [ItemId.IronPlate] = 2, [ItemId.Coal] = 1 },
                    _ => new Dictionary<ItemId, int>()
                };
                return needs.Count > 0;

            case EntityKind.TankFactory:
            case EntityKind.DroneCenter:
                if (selected.ProductionTargetKind is not null
                    && MvpDefinitions.ProductionRecipes.TryGetValue(selected.ProductionTargetKind.Value, out var unitRecipe))
                {
                    needs = unitRecipe.Inputs;
                    return true;
                }

                needs = new Dictionary<ItemId, int>();
                return false;

            case EntityKind.Laboratory:
                {
                    var ownerId = selected.OwnerId ?? localPlayer;
                    var snapshot = simulation.GetResearchSnapshot(ownerId);
                    TechnologyId? activeId = null;
                    foreach (var track in snapshot.Tracks.OrderBy(track => track.Id, StringComparer.Ordinal))
                    {
                        if (track.ActiveSerialTarget is not null)
                        {
                            activeId = track.ActiveSerialTarget;
                            break;
                        }
                    }

                    if (activeId is null)
                    {
                        foreach (var track in snapshot.Tracks.OrderBy(track => track.Id, StringComparer.Ordinal))
                        {
                            if (track.ProjectWeights.Count == 0)
                            {
                                continue;
                            }

                            activeId = track.ProjectWeights.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).First();
                            break;
                        }
                    }

                    if (activeId is null)
                    {
                        needs = new Dictionary<ItemId, int>();
                        return false;
                    }

                    var tech = snapshot.Technologies.FirstOrDefault(t => t.Id == activeId);
                    if (tech is null || tech.SciencePacks.Count == 0)
                    {
                        needs = new Dictionary<ItemId, int>();
                        return false;
                    }

                    needs = tech.SciencePacks.ToDictionary(pack => pack.Item, pack => pack.Amount);
                    return true;
                }

            default:
                needs = new Dictionary<ItemId, int>();
                return false;
        }
    }

    internal static bool TryHandleSidebarStorageClick(
        GameSimulation simulation,
        SfmlCommandGateway commands,
        PlayerId localPlayer,
        int? selectedEntityId,
        List<SidebarStorageHit> hits,
        Vector2i mousePosition,
        string button)
    {
        if (selectedEntityId is null || hits.Count == 0)
        {
            return false;
        }

        var selected = simulation.World.GetEntity(selectedEntityId.Value);
        if (selected is null || selected.OwnerId != localPlayer)
        {
            return false;
        }

        var commander = simulation.World.Entities.FirstOrDefault(entity =>
            entity.IsAlive && entity.Kind == EntityKind.Commander && entity.OwnerId == localPlayer);
        if (commander is null)
        {
            return false;
        }

        var point = new Vector2f(mousePosition.X, mousePosition.Y);
        foreach (var hit in hits)
        {
            if (!hit.Bounds.Contains(point))
            {
                continue;
            }

            if (button == "Right" && hit.Kind == SidebarStorageKind.Input)
            {
                return commands.DepositItemTypeToHubOrInput(commander.Id, localPlayer, selected.Id, hit.Item);
            }

            if (button == "Left" && hit.Kind == SidebarStorageKind.Output)
            {
                return commands.WithdrawItemTypeFromHubOrOutput(commander.Id, localPlayer, selected.Id, hit.Item);
            }

            if (button == "Left"
                && hit.Kind == SidebarStorageKind.Input
                && selected.Kind == EntityKind.Hub)
            {
                return commands.WithdrawItemTypeFromHubOrOutput(commander.Id, localPlayer, selected.Id, hit.Item);
            }
        }

        return false;
    }

    internal static void DrawBuildingProgressBars(
        IRenderTarget target,
        WorldEntity selected,
        float panelX,
        float panelTop,
        uint windowWidth)
    {
        var barX = panelX + 8f;
        var barWidth = Math.Max(40f, windowWidth - panelX - 16f);
        var barY = panelTop + 4f;

        if (selected.EnergyBufferCapacity > 0)
        {
            var ratio = selected.EnergyBuffer / (float)selected.EnergyBufferCapacity;
            var color = ratio >= 0.9f
                ? new Color(60, 180, 75)
                : ratio >= 0.5f
                    ? new Color(220, 180, 40)
                    : new Color(200, 60, 60);
            DrawProgressBar(target, barX, barY, barWidth, 6f, ratio, color);
            barY += 10f;
        }

        if (selected.WorkTicksTotal > 0)
        {
            var ratio = 1f - selected.WorkTicksRemaining / (float)selected.WorkTicksTotal;
            DrawProgressBar(target, barX, barY, barWidth, 6f, ratio, new Color(80, 140, 220));
        }
    }

    internal static void DrawProgressBar(
        IRenderTarget target,
        float x,
        float y,
        float width,
        float height,
        float ratio,
        Color fill)
    {
        ratio = Math.Clamp(ratio, 0f, 1f);
        using var background = new RectangleShape(new Vector2f(width, height))
        {
            Position = new Vector2f(x, y),
            FillColor = new Color(30, 35, 45),
            OutlineColor = new Color(90, 100, 120),
            OutlineThickness = 1f
        };
        target.Draw(background);
        if (ratio <= 0f)
        {
            return;
        }

        using var bar = new RectangleShape(new Vector2f(width * ratio, height))
        {
            Position = new Vector2f(x, y),
            FillColor = fill
        };
        target.Draw(bar);
    }

    internal static void DrawPanel(IRenderTarget target, uint windowWidth, uint windowHeight, float panelX, float panelTop)
    {
        using var panel = new RectangleShape(new Vector2f(windowWidth - panelX, windowHeight - panelTop))
        {
            Position = new Vector2f(panelX, panelTop),
            FillColor = new Color(12, 16, 22, 230),
            OutlineColor = new Color(80, 95, 120),
            OutlineThickness = 1f
        };
        target.Draw(panel);
    }

    internal static void AddInventoryLines(List<string> lines, Inventory inventory)
    {
        if (inventory.Items.Count == 0)
        {
            lines.Add("  -");
            return;
        }

        foreach (var item in inventory.Items.OrderBy(pair => pair.Key).Take(8))
        {
            lines.Add($"  {item.Key}: {item.Value}/{MvpDefinitions.GetMaxStackSize(item.Key)}");
        }
    }

    internal static void AddRecipeLines(List<string> lines, IEnumerable<string> recipeLines, int recipePage)
    {
        var wrapped = WrapHudLines(recipeLines, SfmlUiLayout.HudWrapCharacters).ToList();
        if (wrapped.Count == 0)
        {
            return;
        }

        var pageCount = Math.Max(1, (int)Math.Ceiling(wrapped.Count / (double)SfmlUiLayout.RecipeLinesPerPage));
        var page = Math.Clamp(recipePage, 0, pageCount - 1);
        lines.Add($"Recipes {page + 1}/{pageCount} ([/] or PgUp/PgDn):");
        foreach (var line in wrapped.Skip(page * SfmlUiLayout.RecipeLinesPerPage).Take(SfmlUiLayout.RecipeLinesPerPage))
        {
            lines.Add(line);
        }
    }

    internal static IEnumerable<string> WrapHudLines(IEnumerable<string> lines, int maxCharacters)
    {
        foreach (var line in lines)
        {
            if (line.Length <= maxCharacters)
            {
                yield return line;
                continue;
            }

            var indentLength = line.TakeWhile(char.IsWhiteSpace).Count();
            var indent = new string(' ', indentLength);
            var remaining = line.TrimStart();
            while (remaining.Length > maxCharacters - indentLength)
            {
                var take = Math.Max(1, remaining.LastIndexOf(' ', Math.Min(remaining.Length - 1, maxCharacters - indentLength)));
                yield return indent + remaining[..take].TrimEnd();
                remaining = remaining[take..].TrimStart();
            }

            if (remaining.Length > 0)
            {
                yield return indent + remaining;
            }
        }
    }

    internal static bool IsBufferedBuildingForUi(EntityKind kind)
    {
        return kind is EntityKind.Mine
            or EntityKind.CoalMine
            or EntityKind.OilWell
            or EntityKind.Smelter
            or EntityKind.Refinery
            or EntityKind.Assembler
            or EntityKind.Hub
            or EntityKind.TankFactory
            or EntityKind.DroneCenter
            or EntityKind.Laboratory
            or EntityKind.CoalPlant
            or EntityKind.MachineGunTurret
            or EntityKind.CannonTurret
            or EntityKind.AntiAirTurret;
    }

    internal static IEnumerable<string> GetRecipeLines(WorldEntity selected, GameSimulation simulation, PlayerId localPlayer, int researchPage)
    {
        if (MvpDefinitions.FactoryKinds.Contains(selected.Kind))
        {
            var index = 1;
            foreach (var recipe in GetFactoryRecipes(selected.Kind))
            {
                yield return $"  {index}: {recipe.OutputKind}: {FormatCost(recipe.Inputs)}";
                index++;
            }
        }
        else if (selected.Kind == EntityKind.Laboratory)
        {
            var playerId = selected.OwnerId ?? localPlayer;
            var panel = ResearchPanelModel.FromSnapshot(simulation.GetResearchSnapshot(playerId), researchPage);
            foreach (var line in panel.ToHudLines())
            {
                yield return line;
            }
        }
        else if (selected.Kind == EntityKind.Smelter)
        {
            yield return "Recipes: Ore->Plate, FePlate+Coal->Steel";
        }
        else if (selected.Kind == EntityKind.Assembler)
        {
            yield return "Assembler recipes:";
            foreach (var recipe in MvpDefinitions.ItemRecipes.Values)
            {
                yield return $"  {recipe.Id}: {FormatCost(recipe.Inputs)} -> {recipe.OutputAmount} {recipe.OutputItem}";
            }
        }
        else if (selected.Kind == EntityKind.Refinery)
        {
            yield return "Recipes: CrudeOil->Fuel";
        }
    }

    internal static string FormatCost(IReadOnlyDictionary<ItemId, int> cost)
    {
        return string.Join(", ", cost.Select(pair => $"{pair.Value} {pair.Key}"));
    }

    internal static void DrawTextLines(IRenderTarget target, Font font, IReadOnlyList<string> lines, float panelX, float startY)
    {
        for (var i = 0; i < lines.Count && i < SfmlUiLayout.MaxHudLines; i++)
        {
            using var text = new Text(font, lines[i], 12)
            {
                FillColor = Color.White,
                Position = new Vector2f(panelX + 8f, startY + i * SfmlUiLayout.HudLineHeight)
            };
            target.Draw(text);
        }
    }

    internal static bool IsCombatHudKind(EntityKind kind)
    {
        return kind == EntityKind.Commander
            || MvpDefinitions.UnitKinds.Contains(kind)
            || kind is EntityKind.MachineGunTurret or EntityKind.CannonTurret or EntityKind.AntiAirTurret;
    }

    internal static IEnumerable<ProductionRecipe> GetFactoryRecipes(EntityKind factoryKind)
    {
        return MvpDefinitions.ProductionRecipes.Values
            .Where(recipe => factoryKind == EntityKind.DroneCenter
                ? recipe.OutputKind == EntityKind.Scout
                : recipe.OutputKind != EntityKind.Scout)
            .OrderBy(recipe => (int)recipe.OutputKind);
    }

}
