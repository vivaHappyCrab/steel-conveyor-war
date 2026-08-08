using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public sealed class SfmlGameRunner
{
    private const float TileSize = 24f;
    private const float SidePanelWidth = 240f;
    private const int MaxHudLines = 34;
    private const int RecipeLinesPerPage = 8;
    private const int HudWrapCharacters = 32;

    public void Run(GameSimulation simulation, int? maxFrames = null, SfmlDisplayOptions? display = null)
    {
        display ??= SfmlDisplayOptions.Default;
        var windowWidth = display.Width;
        var windowHeight = display.Height;
        var panelX = Math.Max(0f, windowWidth - SidePanelWidth);

        using var window = new RenderWindow(
            new VideoMode(new Vector2u(windowWidth, windowHeight)),
            display.Title,
            Styles.Close,
            State.Windowed);
        window.Closed += (_, _) => window.Close();
        window.SetFramerateLimit(60);
        var localPlayer = new PlayerId(1);
        int? selectedEntityId = simulation.World.Entities.First(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Commander).Id;
        var isBuildMenuOpen = false;
        EntityKind? pendingBuildKind = null;
        var recipePage = 0;
        var font = TryLoadFont();

        window.KeyPressed += (_, args) =>
        {
            var key = args.Code.ToString();
            var selectedEntity = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);

            if (key == "B" && selectedEntity?.Kind == EntityKind.Commander && selectedEntity.OwnerId == localPlayer)
            {
                isBuildMenuOpen = !isBuildMenuOpen;
                pendingBuildKind = isBuildMenuOpen ? BuildMenuCatalog.BuildableKinds[0] : null;
                recipePage = 0;
                return;
            }

            if (key == "R" && selectedEntity is not null)
            {
                var counterClockwise = Keyboard.IsKeyPressed(Keyboard.Key.LShift) || Keyboard.IsKeyPressed(Keyboard.Key.RShift);
                simulation.TryRotateEntity(selectedEntity.Id, clockwise: !counterClockwise);
                return;
            }

            if (key is "PageDown" or "RBracket")
            {
                recipePage++;
                return;
            }

            if (key is "PageUp" or "LBracket")
            {
                recipePage = Math.Max(0, recipePage - 1);
                return;
            }

            if (!isBuildMenuOpen && selectedEntity?.Kind == EntityKind.Assembler && TryGetRecipeShortcut(key, out var recipeId))
            {
                simulation.TrySetAssemblerRecipe(selectedEntity.Id, recipeId);
                recipePage = 0;
                return;
            }

            if (!isBuildMenuOpen && selectedEntity?.Kind == EntityKind.Laboratory && TryGetNumberShortcut(key, out var researchIndex))
            {
                var ownerId = selectedEntity.OwnerId ?? localPlayer;
                var panel = ResearchPanelModel.FromSnapshot(simulation.GetResearchSnapshot(ownerId), recipePage);
                if (researchIndex < panel.PageEntries.Count)
                {
                    var entry = panel.PageEntries[researchIndex];
                    simulation.TrySelectResearch(
                        ownerId,
                        entry.Id,
                        confirmExclusive: entry.RequiresExclusiveConfirmation,
                        preferredTrackId: entry.TrackId);
                }

                return;
            }

            if (!isBuildMenuOpen && selectedEntity?.Kind == EntityKind.Laboratory && key == "T")
            {
                var ownerId = selectedEntity.OwnerId ?? localPlayer;
                var snapshot = simulation.GetResearchSnapshot(ownerId);
                var panel = ResearchPanelModel.FromSnapshot(snapshot, recipePage);
                if (panel.SupportsAllocationToggle)
                {
                    var cycle = snapshot.Tracks.FirstOrDefault(track => track.Id.Contains("cycle", StringComparison.OrdinalIgnoreCase)) ?? snapshot.Tracks[0];
                    var tactical = snapshot.Tracks.FirstOrDefault(track => track.Id != cycle.Id) ?? snapshot.Tracks[^1];
                    var fullCycle = cycle.AllocationBasisPoints >= 10_000;
                    simulation.TrySetTrackAllocation(ownerId, new Dictionary<string, int>
                    {
                        [cycle.Id] = fullCycle ? 7_000 : 10_000,
                        [tactical.Id] = fullCycle ? 3_000 : 0
                    });
                }

                return;
            }

            if (!isBuildMenuOpen)
            {
                return;
            }

            if (TryGetNumberShortcut(key, out var buildIndex) && buildIndex < BuildMenuCatalog.BuildableKinds.Length)
            {
                pendingBuildKind = BuildMenuCatalog.BuildableKinds[buildIndex];
            }
        };
        window.MouseButtonPressed += (_, args) =>
        {
            var mousePosition = Mouse.GetPosition(window);
            var tile = new TilePosition((int)(mousePosition.X / TileSize), (int)(mousePosition.Y / TileSize));
            if (!simulation.World.IsInside(tile))
            {
                return;
            }

            var button = args.Button.ToString();
            if (button == "Left")
            {
                var selectedEntity = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
                var clickedEntity = simulation.World.GetTopEntityAt(tile);
                var ctrlPressed = Keyboard.IsKeyPressed(Keyboard.Key.LControl) || Keyboard.IsKeyPressed(Keyboard.Key.RControl);
                if (ctrlPressed && selectedEntity?.Kind == EntityKind.Commander && clickedEntity is not null)
                {
                    simulation.TryCollectOutputBuffer(selectedEntity.Id, clickedEntity.Id);
                }
                else if (isBuildMenuOpen && pendingBuildKind is not null && selectedEntity?.Kind == EntityKind.Commander)
                {
                    simulation.TryQueueCommanderBuild(selectedEntity.Id, pendingBuildKind.Value, tile);
                }
                else
                {
                    selectedEntityId = clickedEntity is not null && IsVisibleToLocalPlayer(simulation, localPlayer, clickedEntity)
                        ? clickedEntity.Id
                        : null;
                    recipePage = 0;
                    isBuildMenuOpen = false;
                    pendingBuildKind = null;
                }
            }
            else if (button == "Right")
            {
                var selectedEntity = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
                if (selectedEntity?.Kind == EntityKind.Commander)
                {
                    simulation.TryIssueMoveCommand(selectedEntity.Id, tile);
                    return;
                }

                var bastion = selectedEntity?.Kind == EntityKind.Bastion
                    ? selectedEntity
                    : simulation.World.Entities.FirstOrDefault(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Bastion);
                if (bastion is not null)
                {
                    simulation.TryIssueBastionOrder(bastion.Id, new BastionOrder(BastionOrderKind.AttackArea, tile));
                }
            }
        };

        var clock = new Clock();
        var accumulator = 0f;
        var fixedDelta = 1f / GameSimulation.TicksPerSecond;

        var renderedFrames = 0;

        while (window.IsOpen)
        {
            window.DispatchEvents();

            accumulator += clock.Restart().AsSeconds();
            while (accumulator >= fixedDelta)
            {
                simulation.AdvanceTick();
                accumulator -= fixedDelta;
            }

            window.Clear(new Color(18, 22, 18));
            var hoverPosition = Mouse.GetPosition(window);
            var hoverTile = new TilePosition((int)(hoverPosition.X / TileSize), (int)(hoverPosition.Y / TileSize));
            DrawWorld(window, simulation, localPlayer, selectedEntityId, isBuildMenuOpen ? pendingBuildKind : null, hoverTile);
            DrawHud(window, simulation, localPlayer, selectedEntityId, isBuildMenuOpen, pendingBuildKind, recipePage, font, windowWidth, windowHeight, panelX);
            window.Display();

            renderedFrames++;
            if (maxFrames is not null && renderedFrames >= maxFrames)
            {
                window.Close();
            }
        }
    }

    private static void DrawWorld(IRenderTarget target, GameSimulation simulation, PlayerId localPlayer, int? selectedEntityId, EntityKind? pendingBuildKind, TilePosition hoverTile)
    {
        var world = simulation.World;
        var tile = new RectangleShape(new Vector2f(TileSize - 1f, TileSize - 1f));

        for (var y = 0; y < world.Size.Height; y++)
        {
            for (var x = 0; x < world.Size.Width; x++)
            {
                var position = new TilePosition(x, y);
                tile.Position = new Vector2f(x * TileSize, y * TileSize);
                tile.FillColor = GetTerrainColor(world.GetTerrain(position), simulation.GetVisibility(localPlayer, position));
                target.Draw(tile);
            }
        }

        DrawTechSignatures(target, simulation.GetTechSignatureHotspots(localPlayer));

        foreach (var entity in world.Entities.Where(entity => entity.IsAlive && !entity.IsGarrisoned))
        {
            if (!IsVisibleToLocalPlayer(simulation, localPlayer, entity))
            {
                continue;
            }

            DrawEntity(target, entity, selectedEntityId == entity.Id);
        }

        if (pendingBuildKind is not null && world.IsInside(hoverTile))
        {
            DrawGhostPreview(target, pendingBuildKind.Value, hoverTile);
        }
    }

    private static Color GetTerrainColor(TerrainType terrain, VisibilityState visibility)
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

    private static void DrawTechSignatures(IRenderTarget target, IReadOnlyList<TechSignatureHotspot> hotspots)
    {
        foreach (var hotspot in hotspots)
        {
            var intensity = Math.Min(180, 40 + hotspot.Intensity * 20);
            using var zone = new RectangleShape(new Vector2f(TileSize * 8f, TileSize * 8f))
            {
                Position = new Vector2f(hotspot.ZoneX * TileSize * 8f, hotspot.ZoneY * TileSize * 8f),
                FillColor = new Color(210, 80, 30, (byte)intensity)
            };
            target.Draw(zone);
        }
    }

    private static bool IsVisibleToLocalPlayer(GameSimulation simulation, PlayerId localPlayer, WorldEntity entity)
    {
        return entity.OwnerId == localPlayer || simulation.GetVisibility(localPlayer, entity.Position) == VisibilityState.Visible;
    }

    private static void DrawEntity(IRenderTarget target, WorldEntity entity, bool isSelected)
    {
        var footprint = MvpDefinitions.GetFootprint(entity.Kind == EntityKind.GhostBuild && entity.BuildTargetKind is not null
            ? entity.BuildTargetKind.Value
            : entity.Kind);
        var isMobile = MvpDefinitions.UnitKinds.Contains(entity.Kind) || entity.Kind == EntityKind.Commander;
        var center = isMobile
            ? ToScreen(entity.WorldPosition)
            : new Vector2f(
                entity.Position.X * TileSize + footprint.Width * TileSize / 2f,
                entity.Position.Y * TileSize + footprint.Height * TileSize / 2f);
        var color = GetEntityColor(entity);

        if (isMobile)
        {
            using var unit = new CircleShape(TileSize * 0.35f)
            {
                FillColor = color,
                OutlineColor = entity.OwnerId == new PlayerId(1) ? Color.White : new Color(230, 140, 140),
                OutlineThickness = 2f,
                Origin = new Vector2f(TileSize * 0.35f, TileSize * 0.35f),
                Position = center
            };
            target.Draw(unit);
            if (isSelected)
            {
                DrawMobileSelection(target, entity);
            }
            return;
        }

        using var building = new RectangleShape(new Vector2f(TileSize * footprint.Width - 2f, TileSize * footprint.Height - 2f))
        {
            FillColor = color,
            OutlineColor = entity.Kind == EntityKind.GhostBuild ? new Color(120, 190, 255) : new Color(20, 20, 20),
            OutlineThickness = 1f,
            Position = new Vector2f(entity.Position.X * TileSize + 1f, entity.Position.Y * TileSize + 1f)
        };
        target.Draw(building);

        if (entity.Kind is EntityKind.Conveyor or EntityKind.UndergroundConveyor or EntityKind.Inserter)
        {
            DrawDirectionArrow(target, entity.Position, entity.Direction, entity.Kind == EntityKind.Inserter ? new Color(255, 230, 100) : new Color(40, 40, 20));
        }

        if (entity.Kind is (EntityKind.Conveyor or EntityKind.UndergroundConveyor) && entity.ConveyorItems.Count > 0)
        {
            DrawConveyorItems(target, entity.Position, entity.ConveyorItems);
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

    private static void DrawGhostPreview(IRenderTarget target, EntityKind kind, TilePosition anchor)
    {
        var footprint = MvpDefinitions.GetFootprint(kind);
        using var preview = new RectangleShape(new Vector2f(TileSize * footprint.Width - 2f, TileSize * footprint.Height - 2f))
        {
            Position = new Vector2f(anchor.X * TileSize + 1f, anchor.Y * TileSize + 1f),
            FillColor = new Color(80, 150, 220, 80),
            OutlineColor = new Color(160, 220, 255),
            OutlineThickness = 2f
        };
        target.Draw(preview);
    }

    private static void DrawSelection(IRenderTarget target, WorldEntity entity, WorldSize footprint)
    {
        using var outline = new RectangleShape(new Vector2f(TileSize * footprint.Width - 1f, TileSize * footprint.Height - 1f))
        {
            Position = new Vector2f(entity.Position.X * TileSize, entity.Position.Y * TileSize),
            FillColor = Color.Transparent,
            OutlineColor = new Color(255, 245, 120),
            OutlineThickness = 2f
        };
        target.Draw(outline);
    }

    private static void DrawMobileSelection(IRenderTarget target, WorldEntity entity)
    {
        var center = ToScreen(entity.WorldPosition);
        using var outline = new CircleShape(TileSize * 0.45f)
        {
            Position = center,
            Origin = new Vector2f(TileSize * 0.45f, TileSize * 0.45f),
            FillColor = Color.Transparent,
            OutlineColor = new Color(255, 245, 120),
            OutlineThickness = 2f
        };
        target.Draw(outline);
    }

    private static Vector2f ToScreen(WorldPosition position)
    {
        return new Vector2f((float)(position.X * TileSize), (float)(position.Y * TileSize));
    }

    private static void DrawDirectionArrow(IRenderTarget target, TilePosition position, Direction direction, Color color)
    {
        var center = new Vector2f(position.X * TileSize + TileSize / 2f, position.Y * TileSize + TileSize / 2f);
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

    private static void DrawConveyorItems(IRenderTarget target, TilePosition position, IReadOnlyList<ConveyorItem> items)
    {
        for (var i = 0; i < items.Count && i < 2; i++)
        {
            var offsetX = i == 0 ? -4f : 4f;
            using var marker = new CircleShape(TileSize * 0.16f)
            {
                FillColor = GetItemColor(items[i].Item),
                Origin = new Vector2f(TileSize * 0.16f, TileSize * 0.16f),
                Position = new Vector2f(position.X * TileSize + TileSize / 2f + offsetX, position.Y * TileSize + TileSize / 2f)
            };
            target.Draw(marker);
        }
    }

    private static void DrawHeldItem(IRenderTarget target, TilePosition position, ItemId item)
    {
        using var marker = new CircleShape(TileSize * 0.14f)
        {
            FillColor = GetItemColor(item),
            OutlineColor = Color.White,
            OutlineThickness = 1f,
            Origin = new Vector2f(TileSize * 0.14f, TileSize * 0.14f),
            Position = new Vector2f(position.X * TileSize + TileSize * 0.75f, position.Y * TileSize + TileSize * 0.25f)
        };
        target.Draw(marker);
    }

    private static void DrawHud(
        IRenderTarget target,
        GameSimulation simulation,
        PlayerId localPlayer,
        int? selectedEntityId,
        bool isBuildMenuOpen,
        EntityKind? pendingBuildKind,
        int recipePage,
        Font? font,
        uint windowWidth,
        uint windowHeight,
        float panelX)
    {
        DrawPanel(target, windowWidth, windowHeight, panelX);
        if (font is null)
        {
            return;
        }

        var lines = new List<string>();
        var selected = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
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
            if (selected.ProductionTargetKind is not null)
            {
                lines.Add($"Producing: {selected.ProductionTargetKind}");
            }

            if (selected.Kind == EntityKind.Commander)
            {
                lines.Add($"Build radius: {MvpDefinitions.CommanderBuildRadius}");
                lines.Add($"World: {selected.WorldPosition.X:0.00},{selected.WorldPosition.Y:0.00}");
                lines.Add($"Move target: {(selected.MoveTarget is null ? "-" : $"{selected.MoveTarget.Value.X},{selected.MoveTarget.Value.Y}")}");
                lines.Add($"Queued: {(selected.QueuedBuildOrder is null ? "-" : $"{selected.QueuedBuildOrder.TargetKind}@{selected.QueuedBuildOrder.TargetPosition.X},{selected.QueuedBuildOrder.TargetPosition.Y}")}");
                lines.Add("B: build menu");
                lines.Add("RMB: move");
                lines.Add("Ctrl+LMB building: collect output");
            }

            if (selected.Kind == EntityKind.Assembler)
            {
                lines.Add($"Recipe: {(selected.SelectedItemRecipe?.ToString() ?? "-")}");
                lines.Add("1-5: set assembler recipe");
            }

            AddRecipeLines(lines, GetRecipeLines(selected, simulation, localPlayer, recipePage), recipePage: 0);

            lines.Add("Inventory:");
            AddInventoryLines(lines, selected.Inventory);

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

            if (selected.InputBuffer.Items.Count > 0 || selected.OutputBuffer.Items.Count > 0 || IsBufferedBuildingForUi(selected.Kind))
            {
                lines.Add("Input:");
                AddInventoryLines(lines, selected.InputBuffer);
                lines.Add("Output:");
                AddInventoryLines(lines, selected.OutputBuffer);
            }
        }

        if (isBuildMenuOpen)
        {
            lines.Add("");
            lines.Add("Build menu:");
            lines.Add($"Pending: {pendingBuildKind}");
            lines.Add("1 Mine 2 Coal 3 Oil");
            lines.Add("4 Smelt 5 Refinery 6 Belt");
            lines.Add("7 Inserter 8 Assembler 9 Factory 0 Hub");
            lines.Add("Lab/Solar/defense are in catalog");
            lines.Add("LMB: place/queue build");
        }

        DrawTextLines(target, font, lines, panelX);
    }

    private static void DrawPanel(IRenderTarget target, uint windowWidth, uint windowHeight, float panelX)
    {
        using var panel = new RectangleShape(new Vector2f(windowWidth - panelX, windowHeight))
        {
            Position = new Vector2f(panelX, 0),
            FillColor = new Color(12, 16, 22, 230),
            OutlineColor = new Color(80, 95, 120),
            OutlineThickness = 1f
        };
        target.Draw(panel);
    }

    private static void AddInventoryLines(List<string> lines, Inventory inventory)
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

    private static void AddRecipeLines(List<string> lines, IEnumerable<string> recipeLines, int recipePage)
    {
        var wrapped = WrapHudLines(recipeLines, HudWrapCharacters).ToList();
        if (wrapped.Count == 0)
        {
            return;
        }

        var pageCount = Math.Max(1, (int)Math.Ceiling(wrapped.Count / (double)RecipeLinesPerPage));
        var page = Math.Clamp(recipePage, 0, pageCount - 1);
        lines.Add($"Recipes {page + 1}/{pageCount} ([/] or PgUp/PgDn):");
        foreach (var line in wrapped.Skip(page * RecipeLinesPerPage).Take(RecipeLinesPerPage))
        {
            lines.Add(line);
        }
    }

    private static IEnumerable<string> WrapHudLines(IEnumerable<string> lines, int maxCharacters)
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

    private static bool IsBufferedBuildingForUi(EntityKind kind)
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

    private static IEnumerable<string> GetRecipeLines(WorldEntity selected, GameSimulation simulation, PlayerId localPlayer, int researchPage)
    {
        if (MvpDefinitions.FactoryKinds.Contains(selected.Kind))
        {
            yield return "Recipes:";
            foreach (var recipe in MvpDefinitions.ProductionRecipes.Values.Where(recipe => selected.Kind == EntityKind.DroneCenter ? recipe.OutputKind == EntityKind.Scout : recipe.OutputKind != EntityKind.Scout))
            {
                yield return $"  {recipe.OutputKind}: {FormatCost(recipe.Inputs)}";
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

    private static string FormatCost(IReadOnlyDictionary<ItemId, int> cost)
    {
        return string.Join(", ", cost.Select(pair => $"{pair.Value} {pair.Key}"));
    }

    private static void DrawTextLines(IRenderTarget target, Font font, IReadOnlyList<string> lines, float panelX)
    {
        for (var i = 0; i < lines.Count && i < MaxHudLines; i++)
        {
            using var text = new Text(font, lines[i], 12)
            {
                FillColor = Color.White,
                Position = new Vector2f(panelX + 8f, 10f + i * 18f)
            };
            target.Draw(text);
        }
    }

    private static Font? TryLoadFont()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "resources", "fonts", "arial.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new Font(candidate);
            }
        }

        return null;
    }

    private static Color GetEntityColor(WorldEntity entity)
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

    private static Color GetItemColor(ItemId item)
    {
        return item switch
        {
            ItemId.IronOre or ItemId.IronPlate => new Color(180, 190, 200),
            ItemId.CopperOre or ItemId.CopperPlate => new Color(210, 120, 50),
            ItemId.Coal => new Color(20, 20, 24),
            ItemId.CrudeOil or ItemId.Fuel => new Color(90, 60, 130),
            ItemId.Steel => new Color(140, 150, 160),
            ItemId.IronGear => new Color(170, 170, 150),
            ItemId.CopperWire => new Color(230, 135, 65),
            ItemId.Circuit => new Color(80, 190, 100),
            ItemId.SciencePackT1 => new Color(80, 180, 255),
            ItemId.SciencePackT2 => new Color(255, 180, 80),
            _ => Color.White
        };
    }

    private static bool TryGetRecipeShortcut(string key, out ItemRecipeId recipeId)
    {
        return key switch
        {
            "Num1" => SetRecipe(ItemRecipeId.IronGear, out recipeId),
            "Num2" => SetRecipe(ItemRecipeId.CopperWire, out recipeId),
            "Num3" => SetRecipe(ItemRecipeId.Circuit, out recipeId),
            "Num4" => SetRecipe(ItemRecipeId.SciencePackT1, out recipeId),
            "Num5" => SetRecipe(ItemRecipeId.SciencePackT2, out recipeId),
            _ => SetRecipe(default, out recipeId, success: false)
        };
    }

    private static bool TryGetNumberShortcut(string key, out int index)
    {
        return key switch
        {
            "Num1" => SetIndex(0, out index),
            "Num2" => SetIndex(1, out index),
            "Num3" => SetIndex(2, out index),
            "Num4" => SetIndex(3, out index),
            "Num5" => SetIndex(4, out index),
            "Num6" => SetIndex(5, out index),
            "Num7" => SetIndex(6, out index),
            "Num8" => SetIndex(7, out index),
            "Num9" => SetIndex(8, out index),
            "Num0" => SetIndex(9, out index),
            _ => SetIndex(0, out index, success: false)
        };
    }

    private static bool SetRecipe(ItemRecipeId value, out ItemRecipeId recipeId, bool success = true)
    {
        recipeId = value;
        return success;
    }

    private static bool SetIndex(int value, out int index, bool success = true)
    {
        index = value;
        return success;
    }
}
