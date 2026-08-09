using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

public sealed class SfmlGameRunner
{
    private const float TileSize = 24f;
    private const float SidePanelWidth = 240f;
    private const float TopBarHeight = 36f;
    private const float MinimapSize = 140f;
    private const float MinimapMargin = 8f;
    private const float EdgeScrollBand = 20f;
    private const float CameraPanSpeed = 420f;
    private const float BuildBarSlotSize = 48f;
    private const float BuildBarBottomMargin = 10f;
    private const int MaxHudLines = 34;
    private const int RecipeLinesPerPage = 8;
    private const int HudWrapCharacters = 32;
    private const float HudLineHeight = 18f;
    private const float HudTextStartY = 10f;
    private const float ResearchDoubleClickSeconds = 0.35f;

    private enum SidebarStorageKind
    {
        Input,
        Output
    }

    private readonly record struct SidebarStorageHit(FloatRect Bounds, ItemId Item, SidebarStorageKind Kind);

    public void Run(GameSimulation simulation, int? maxFrames = null, SfmlDisplayOptions? display = null)
    {
        display ??= SfmlDisplayOptions.Default;
        var windowWidth = display.Width;
        var windowHeight = display.Height;
        var panelX = Math.Max(0f, windowWidth - SidePanelWidth);
        var panelTop = TopBarHeight + MinimapSize + (2f * MinimapMargin);
        var playfieldWidth = Math.Max(1f, panelX);
        var playfieldHeight = Math.Max(1f, windowHeight - TopBarHeight);
        var worldWidthPx = simulation.World.Size.Width * TileSize;
        var worldHeightPx = simulation.World.Size.Height * TileSize;
        var cameraX = 0f;
        var cameraY = Math.Max(0f, (simulation.World.Size.Height / 2f) * TileSize - playfieldHeight / 2f);
        var isMiddleDragging = false;
        var lastDragMouse = new Vector2i();

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
        var pendingDirection = Direction.East;
        ItemRecipeId? pendingRecipe = null;
        var recipePage = 0;
        var templateUnitIndex = 0;
        var bastionPendingMode = BastionPendingInputMode.None;
        var patrolWaypoints = new List<TilePosition>();
        var sidebarStorageHits = new List<SidebarStorageHit>();
        var isResearchOverlayOpen = false;
        TechnologyId? researchSelectedId = null;
        TechnologyId? researchLastClickId = null;
        var researchLastClickSeconds = -1f;
        var researchClickClock = new Clock();
        var font = TryLoadFont();

        FloatRect GetMinimapBounds() =>
            new(
                new Vector2f(windowWidth - MinimapSize - MinimapMargin, TopBarHeight + MinimapMargin),
                new Vector2f(MinimapSize, MinimapSize));

        bool IsOverMinimap(Vector2i screen)
        {
            var bounds = GetMinimapBounds();
            return screen.X >= bounds.Left
                && screen.Y >= bounds.Top
                && screen.X < bounds.Left + bounds.Width
                && screen.Y < bounds.Top + bounds.Height;
        }

        bool IsOverSidePanel(Vector2i screen) =>
            screen.X >= panelX && screen.Y >= panelTop && !IsOverMinimap(screen);

        void ClearBastionPending()
        {
            bastionPendingMode = BastionPendingInputMode.None;
            patrolWaypoints.Clear();
        }

        void ClampCamera()
        {
            var maxX = Math.Max(0f, worldWidthPx - playfieldWidth);
            var maxY = Math.Max(0f, worldHeightPx - playfieldHeight);
            cameraX = Math.Clamp(cameraX, 0f, maxX);
            cameraY = Math.Clamp(cameraY, 0f, maxY);
        }

        ClampCamera();

        View CreateWorldView()
        {
            var view = new View(new FloatRect(new Vector2f(cameraX, cameraY), new Vector2f(playfieldWidth, playfieldHeight)));
            view.Viewport = new FloatRect(
                new Vector2f(0f, TopBarHeight / windowHeight),
                new Vector2f(playfieldWidth / windowWidth, playfieldHeight / windowHeight));
            return view;
        }

        bool IsInPlayfield(Vector2i screen)
        {
            return screen.X >= 0
                && screen.X < playfieldWidth
                && screen.Y >= TopBarHeight
                && screen.Y < windowHeight;
        }

        TilePosition? TileFromScreen(Vector2i screen)
        {
            if (!IsInPlayfield(screen))
            {
                return null;
            }

            using var worldView = CreateWorldView();
            var world = window.MapPixelToCoords(screen, worldView);
            var tile = new TilePosition((int)(world.X / TileSize), (int)(world.Y / TileSize));
            return simulation.World.IsInside(tile) ? tile : null;
        }

        window.KeyPressed += (_, args) =>
        {
            var key = args.Code.ToString();
            var selectedEntity = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);

            if (key == "Escape" && bastionPendingMode != BastionPendingInputMode.None)
            {
                ClearBastionPending();
                return;
            }

            if (key == "Escape" && isResearchOverlayOpen)
            {
                isResearchOverlayOpen = false;
                researchSelectedId = null;
                researchLastClickId = null;
                return;
            }

            if (key is "Enter" or "Return"
                && bastionPendingMode == BastionPendingInputMode.PatrolWaypoints
                && selectedEntity?.Kind == EntityKind.Bastion
                && selectedEntity.OwnerId == localPlayer
                && patrolWaypoints.Count is >= 2 and <= 4)
            {
                simulation.TryIssueBastionOrder(
                    selectedEntity.Id,
                    new BastionOrder(BastionOrderKind.Patrol, Waypoints: patrolWaypoints.ToArray()));
                ClearBastionPending();
                return;
            }

            if (key == "F1")
            {
                EnsureLocalCommanderSelected(simulation, localPlayer, ref selectedEntityId);
                selectedEntity = simulation.World.GetEntity(selectedEntityId!.Value);
                if (selectedEntity is not null)
                {
                    CenterCameraOnWorldPosition(selectedEntity.WorldPosition, playfieldWidth, playfieldHeight, ref cameraX, ref cameraY);
                    ClampCamera();
                }

                isBuildMenuOpen = false;
                pendingBuildKind = null;
                pendingDirection = Direction.East;
                pendingRecipe = null;
                ClearBastionPending();
                return;
            }

            if (key == "Q")
            {
                var mousePosition = Mouse.GetPosition(window);
                var tile = TileFromScreen(mousePosition);
                if (tile is not null)
                {
                    var hoverEntity = simulation.World.GetTopEntityAt(tile.Value);
                    if (hoverEntity is not null
                        && IsVisibleToLocalPlayer(simulation, localPlayer, hoverEntity)
                        && BuildBarModel.TryCopyFromWorldEntity(hoverEntity, out var copyKind, out var copyDirection, out var copyRecipe))
                    {
                        EnsureLocalCommanderSelected(simulation, localPlayer, ref selectedEntityId);
                        isBuildMenuOpen = true;
                        pendingBuildKind = copyKind;
                        pendingDirection = copyDirection;
                        pendingRecipe = copyRecipe;
                        recipePage = 0;
                    }
                }

                return;
            }

            if (key == "B" && selectedEntity?.Kind == EntityKind.Commander && selectedEntity.OwnerId == localPlayer)
            {
                isBuildMenuOpen = !isBuildMenuOpen;
                pendingBuildKind = isBuildMenuOpen ? BuildMenuCatalog.BuildableKinds[0] : null;
                pendingDirection = Direction.East;
                pendingRecipe = null;
                recipePage = 0;
                return;
            }

            if (key == "R")
            {
                var counterClockwise = Keyboard.IsKeyPressed(Keyboard.Key.LShift) || Keyboard.IsKeyPressed(Keyboard.Key.RShift);
                if (isBuildMenuOpen && pendingBuildKind is not null && BuildBarModel.IsDirectedKind(pendingBuildKind.Value))
                {
                    pendingDirection = RotateDirection(pendingDirection, clockwise: !counterClockwise);
                    return;
                }

                var hoverTile = TileFromScreen(Mouse.GetPosition(window));
                if (hoverTile is not null)
                {
                    var hoverEntity = simulation.World.GetTopEntityAt(hoverTile.Value);
                    if (hoverEntity is not null
                        && hoverEntity.OwnerId == localPlayer
                        && BuildBarModel.IsDirectedKind(hoverEntity.Kind)
                        && simulation.TryRotateEntity(hoverEntity.Id, localPlayer, clockwise: !counterClockwise))
                    {
                        return;
                    }
                }

                if (selectedEntity is not null && selectedEntity.OwnerId == localPlayer)
                {
                    simulation.TryRotateEntity(selectedEntity.Id, localPlayer, clockwise: !counterClockwise);
                }

                return;
            }

            if (!isBuildMenuOpen
                && selectedEntity?.Kind == EntityKind.Bastion
                && selectedEntity.OwnerId == localPlayer
                && key is "PageDown" or "RBracket" or "PageUp" or "LBracket")
            {
                var unlocked = BastionCompositionPanelModel.UnlockedUnitKinds(simulation, localPlayer);
                var count = Math.Max(1, unlocked.Length);
                var delta = key is "PageDown" or "RBracket" ? 1 : -1;
                templateUnitIndex = (templateUnitIndex + delta + count) % count;
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

            if (key == "T")
            {
                isResearchOverlayOpen = !isResearchOverlayOpen;
                if (!isResearchOverlayOpen)
                {
                    researchSelectedId = null;
                    researchLastClickId = null;
                }

                return;
            }

            if (!isBuildMenuOpen && selectedEntity?.Kind == EntityKind.Assembler && TryGetRecipeShortcut(key, out var recipeId))
            {
                simulation.TrySetAssemblerRecipe(selectedEntity.Id, recipeId);
                recipePage = 0;
                return;
            }

            if (!isResearchOverlayOpen && !isBuildMenuOpen && selectedEntity?.Kind == EntityKind.Laboratory && TryGetNumberShortcut(key, out var researchIndex))
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

            if (!isBuildMenuOpen
                && selectedEntity is not null
                && MvpDefinitions.FactoryKinds.Contains(selectedEntity.Kind)
                && selectedEntity.OwnerId == localPlayer)
            {
                if (TryGetNumberShortcut(key, out var factoryRecipeIndex))
                {
                    var recipes = GetFactoryRecipes(selectedEntity.Kind).ToList();
                    if (factoryRecipeIndex < recipes.Count)
                    {
                        simulation.TrySetFactoryProduction(selectedEntity.Id, recipes[factoryRecipeIndex].OutputKind);
                    }

                    return;
                }
            }

            if (!isBuildMenuOpen
                && selectedEntity?.Kind == EntityKind.Bastion
                && selectedEntity.OwnerId == localPlayer)
            {
                if (BastionOrderBarModel.TryGetCommandFromKey(key, out var orderCommand))
                {
                    ApplyBastionOrderCommand(simulation, selectedEntity.Id, orderCommand, ref bastionPendingMode, patrolWaypoints);
                    return;
                }

                if (TryGetNumberShortcut(key, out var bastionIndex)
                    && TrySelectOwnedBastionByIndex(simulation, localPlayer, bastionIndex, ref selectedEntityId))
                {
                    recipePage = 0;
                    templateUnitIndex = 0;
                    ClearBastionPending();
                    return;
                }

                if (TryAdjustBastionTemplate(key, simulation, selectedEntity, templateUnitIndex))
                {
                    return;
                }
            }

            if (!isBuildMenuOpen)
            {
                return;
            }

            if (TryGetNumberShortcut(key, out var buildIndex) && buildIndex < BuildMenuCatalog.BuildableKinds.Length)
            {
                pendingBuildKind = BuildMenuCatalog.BuildableKinds[buildIndex];
                if (!BuildBarModel.IsDirectedKind(pendingBuildKind.Value))
                {
                    pendingDirection = Direction.East;
                }

                if (pendingBuildKind != EntityKind.Assembler)
                {
                    pendingRecipe = null;
                }
            }
        };
        window.MouseButtonPressed += (_, args) =>
        {
            var mousePosition = Mouse.GetPosition(window);
            var button = args.Button.ToString();
            if (button == "Middle")
            {
                isMiddleDragging = true;
                lastDragMouse = mousePosition;
                return;
            }

            if (button == "Left" && isBuildMenuOpen
                && TryPickBuildBarKind(mousePosition, windowWidth, windowHeight, panelX, out var barKind))
            {
                pendingBuildKind = barKind;
                if (!BuildBarModel.IsDirectedKind(barKind))
                {
                    pendingDirection = Direction.East;
                }

                if (barKind != EntityKind.Assembler)
                {
                    pendingRecipe = null;
                }

                return;
            }

            if (isResearchOverlayOpen && button == "Left")
            {
                var bottomReserved = BuildBarSlotSize + BuildBarBottomMargin + 8f;
                var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
                var snapshot = simulation.GetResearchSnapshot(localPlayer);
                var tree = ResearchTreePanelModel.FromSnapshot(snapshot, overlayBounds, researchSelectedId);

                if (tree.HitExit(mousePosition))
                {
                    isResearchOverlayOpen = false;
                    researchSelectedId = null;
                    researchLastClickId = null;
                    return;
                }

                if (tree.HitAllocation(mousePosition))
                {
                    ToggleResearchAllocation(simulation, localPlayer);
                    return;
                }

                if (tree.HitAction(mousePosition))
                {
                    if (researchSelectedId is not null)
                    {
                        if (tree.CanCancelSelected)
                        {
                            simulation.TryCancelResearch(localPlayer, researchSelectedId.Value);
                        }
                        else if (tree.CanStartSelected)
                        {
                            var node = tree.SelectedNode;
                            simulation.TrySelectResearch(
                                localPlayer,
                                researchSelectedId.Value,
                                confirmExclusive: node?.RequiresExclusiveConfirmation == true,
                                preferredTrackId: node?.TrackId);
                        }
                    }

                    return;
                }

                if (tree.TryPickNode(mousePosition, out var techId))
                {
                    var now = researchClickClock.ElapsedTime.AsSeconds();
                    var isDouble = researchLastClickId == techId
                        && now - researchLastClickSeconds <= ResearchDoubleClickSeconds;
                    researchSelectedId = techId;
                    researchLastClickId = techId;
                    researchLastClickSeconds = now;
                    if (isDouble)
                    {
                        var node = tree.Nodes.First(n => n.Id == techId);
                        simulation.TrySelectResearch(
                            localPlayer,
                            techId,
                            confirmExclusive: node.RequiresExclusiveConfirmation,
                            preferredTrackId: node.TrackId);
                    }

                    return;
                }

                if (tree.ContainsOverlay(mousePosition))
                {
                    return;
                }
            }

            if (IsOverSidePanel(mousePosition)
                && TryHandleSidebarStorageClick(
                    simulation,
                    localPlayer,
                    selectedEntityId,
                    sidebarStorageHits,
                    mousePosition,
                    button))
            {
                return;
            }

            if (IsOverSidePanel(mousePosition) || IsOverMinimap(mousePosition))
            {
                return;
            }

            var selectedForBar = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
            if (button == "Left"
                && !isBuildMenuOpen
                && selectedForBar?.Kind == EntityKind.Bastion
                && selectedForBar.OwnerId == localPlayer)
            {
                var compositionSlots = BastionCompositionPanelModel.BuildSlots(simulation, selectedForBar);
                if (BastionCompositionPanelModel.TryPickAdjust(
                        mousePosition,
                        windowWidth,
                        windowHeight,
                        panelX,
                        compositionSlots,
                        out var slotIndex,
                        out var adjust))
                {
                    var slot = compositionSlots[slotIndex];
                    templateUnitIndex = slotIndex;
                    simulation.TrySetBastionTemplate(
                        selectedForBar.Id,
                        slot.UnitKind,
                        Math.Max(0, slot.TemplateMax + (int)adjust));
                    return;
                }

                if (BastionCompositionPanelModel.ContainsPanel(
                        mousePosition,
                        windowWidth,
                        windowHeight,
                        panelX,
                        compositionSlots.Length))
                {
                    return;
                }

                if (TryPickBastionOrderCommand(mousePosition, windowWidth, windowHeight, panelX, out var barCommand))
                {
                    ApplyBastionOrderCommand(simulation, selectedForBar.Id, barCommand, ref bastionPendingMode, patrolWaypoints);
                    return;
                }
            }

            var tile = TileFromScreen(mousePosition);
            if (tile is null)
            {
                return;
            }

            if (button == "Left")
            {
                var selectedEntity = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
                if (TryHandleBastionPendingMapClick(
                        simulation,
                        selectedEntity,
                        localPlayer,
                        bastionPendingMode,
                        patrolWaypoints,
                        tile.Value,
                        confirmPatrol: false,
                        out var consumedLeft)
                    && consumedLeft)
                {
                    if (bastionPendingMode is BastionPendingInputMode.AttackTarget or BastionPendingInputMode.ScoutTarget)
                    {
                        ClearBastionPending();
                    }

                    return;
                }

                var clickedEntity = simulation.World.GetTopEntityAt(tile.Value);
                var ctrlPressed = Keyboard.IsKeyPressed(Keyboard.Key.LControl) || Keyboard.IsKeyPressed(Keyboard.Key.RControl);
                if (ctrlPressed
                    && selectedEntity?.Kind == EntityKind.Commander
                    && selectedEntity.OwnerId == localPlayer
                    && clickedEntity is not null)
                {
                    simulation.TryWithdrawFromHubOrOutput(selectedEntity.Id, clickedEntity.Id);
                }
                else if (isBuildMenuOpen && pendingBuildKind is not null && selectedEntity?.Kind == EntityKind.Commander)
                {
                    simulation.TryQueueCommanderBuild(
                        selectedEntity.Id,
                        pendingBuildKind.Value,
                        tile.Value,
                        pendingDirection,
                        pendingRecipe);
                }
                else
                {
                    selectedEntityId = clickedEntity is not null && IsVisibleToLocalPlayer(simulation, localPlayer, clickedEntity)
                        ? clickedEntity.Id
                        : null;
                    recipePage = 0;
                    templateUnitIndex = 0;
                    isBuildMenuOpen = false;
                    pendingBuildKind = null;
                    pendingDirection = Direction.East;
                    pendingRecipe = null;
                    ClearBastionPending();
                }
            }
            else if (button == "Right")
            {
                var selectedEntity = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
                if (selectedEntity?.Kind == EntityKind.Commander && selectedEntity.OwnerId == localPlayer)
                {
                    var ctrlPressed = Keyboard.IsKeyPressed(Keyboard.Key.LControl) || Keyboard.IsKeyPressed(Keyboard.Key.RControl);
                    var clickedEntity = simulation.World.GetTopEntityAt(tile.Value);
                    if (ctrlPressed
                        && clickedEntity is not null
                        && simulation.TryDepositToHubOrInput(selectedEntity.Id, clickedEntity.Id))
                    {
                        return;
                    }

                    simulation.TryIssueMoveCommand(selectedEntity.Id, tile.Value);
                    return;
                }

                if (TryHandleBastionPendingMapClick(
                        simulation,
                        selectedEntity,
                        localPlayer,
                        bastionPendingMode,
                        patrolWaypoints,
                        tile.Value,
                        confirmPatrol: true,
                        out var consumedRight)
                    && consumedRight)
                {
                    ClearBastionPending();
                }
            }
        };
        window.MouseButtonReleased += (_, args) =>
        {
            if (args.Button.ToString() == "Middle")
            {
                isMiddleDragging = false;
            }
        };

        var clock = new Clock();
        var accumulator = 0f;
        var fixedDelta = 1f / GameSimulation.TicksPerSecond;

        var renderedFrames = 0;

        while (window.IsOpen)
        {
            window.DispatchEvents();
            var frameDt = clock.Restart().AsSeconds();
            accumulator += frameDt;
            while (accumulator >= fixedDelta)
            {
                simulation.AdvanceTick();
                accumulator -= fixedDelta;
            }

            var mousePosition = Mouse.GetPosition(window);
            if (isMiddleDragging)
            {
                cameraX -= mousePosition.X - lastDragMouse.X;
                cameraY -= mousePosition.Y - lastDragMouse.Y;
                lastDragMouse = mousePosition;
                ClampCamera();
            }
            else
            {
                var pan = CameraPanSpeed * frameDt;
                if (Keyboard.IsKeyPressed(Keyboard.Key.Left))
                {
                    cameraX -= pan;
                }

                if (Keyboard.IsKeyPressed(Keyboard.Key.Right))
                {
                    cameraX += pan;
                }

                if (Keyboard.IsKeyPressed(Keyboard.Key.Up))
                {
                    cameraY -= pan;
                }

                if (Keyboard.IsKeyPressed(Keyboard.Key.Down))
                {
                    cameraY += pan;
                }

                if (IsInPlayfield(mousePosition))
                {
                    if (mousePosition.X < EdgeScrollBand)
                    {
                        cameraX -= pan;
                    }
                    else if (mousePosition.X > playfieldWidth - EdgeScrollBand)
                    {
                        cameraX += pan;
                    }

                    if (mousePosition.Y < TopBarHeight + EdgeScrollBand)
                    {
                        cameraY -= pan;
                    }
                    else if (mousePosition.Y > windowHeight - EdgeScrollBand)
                    {
                        var overBuildBar = isBuildMenuOpen
                            && GetBuildBarBounds(windowWidth, windowHeight, panelX, out _, out _).Contains(new Vector2f(mousePosition.X, mousePosition.Y));
                        if (!overBuildBar)
                        {
                            cameraY += pan;
                        }
                    }
                }

                ClampCamera();
            }

            window.Clear(new Color(18, 22, 18));
            using (var worldView = CreateWorldView())
            {
                window.SetView(worldView);
                var hoverTile = TileFromScreen(mousePosition);
                if (isBuildMenuOpen
                    && GetBuildBarBounds(windowWidth, windowHeight, panelX, out _, out _).Contains(new Vector2f(mousePosition.X, mousePosition.Y)))
                {
                    hoverTile = null;
                }

                DrawWorld(
                    window,
                    simulation,
                    localPlayer,
                    selectedEntityId,
                    isBuildMenuOpen ? pendingBuildKind : null,
                    pendingDirection,
                    hoverTile,
                    patrolWaypoints,
                    cameraX,
                    cameraY,
                    playfieldWidth,
                    playfieldHeight);
            }

            window.SetView(window.DefaultView);
            DrawTopBar(window, simulation, localPlayer, font, playfieldWidth);
            DrawMinimap(window, simulation, localPlayer, windowWidth);
            DrawHud(
                window,
                simulation,
                localPlayer,
                selectedEntityId,
                isBuildMenuOpen,
                pendingBuildKind,
                pendingDirection,
                pendingRecipe,
                recipePage,
                templateUnitIndex,
                bastionPendingMode,
                patrolWaypoints.Count,
                font,
                windowWidth,
                windowHeight,
                panelX,
                panelTop,
                isResearchOverlayOpen,
                researchSelectedId,
                sidebarStorageHits);
            if (isResearchOverlayOpen)
            {
                var bottomReserved = BuildBarSlotSize + BuildBarBottomMargin + 8f;
                var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
                var tree = ResearchTreePanelModel.FromSnapshot(
                    simulation.GetResearchSnapshot(localPlayer),
                    overlayBounds,
                    researchSelectedId);
                DrawResearchTreeOverlay(window, tree, font);
            }

            if (isBuildMenuOpen)
            {
                DrawBuildBar(
                    window,
                    simulation,
                    localPlayer,
                    selectedEntityId,
                    pendingBuildKind,
                    pendingDirection,
                    pendingRecipe,
                    font,
                    windowWidth,
                    windowHeight,
                    panelX,
                    mousePosition);
            }
            else if (!isResearchOverlayOpen)
            {
                var selectedForOrders = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
                if (selectedForOrders?.Kind == EntityKind.Bastion && selectedForOrders.OwnerId == localPlayer)
                {
                    DrawBastionCompositionPanel(
                        window,
                        simulation,
                        selectedForOrders,
                        templateUnitIndex,
                        font,
                        windowWidth,
                        windowHeight,
                        panelX,
                        mousePosition);
                    DrawBastionOrderBar(
                        window,
                        selectedForOrders,
                        bastionPendingMode,
                        font,
                        windowWidth,
                        windowHeight,
                        panelX,
                        mousePosition);
                }
            }

            window.Display();

            renderedFrames++;
            if (maxFrames is not null && renderedFrames >= maxFrames)
            {
                window.Close();
            }
        }
    }

    private static void DrawWorld(
        IRenderTarget target,
        GameSimulation simulation,
        PlayerId localPlayer,
        int? selectedEntityId,
        EntityKind? pendingBuildKind,
        Direction pendingDirection,
        TilePosition? hoverTile,
        IReadOnlyList<TilePosition> patrolWaypoints,
        float cameraX,
        float cameraY,
        float playfieldWidth,
        float playfieldHeight)
    {
        var world = simulation.World;
        var tile = new RectangleShape(new Vector2f(TileSize - 1f, TileSize - 1f));
        var minX = Math.Max(0, (int)(cameraX / TileSize) - 1);
        var minY = Math.Max(0, (int)(cameraY / TileSize) - 1);
        var maxX = Math.Min(world.Size.Width - 1, (int)((cameraX + playfieldWidth) / TileSize) + 1);
        var maxY = Math.Min(world.Size.Height - 1, (int)((cameraY + playfieldHeight) / TileSize) + 1);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var position = new TilePosition(x, y);
                tile.Position = new Vector2f(x * TileSize, y * TileSize);
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
            DrawEntity(target, entity, selectedEntityId == entity.Id);
        }

        foreach (var entity in visibleEntities.Where(entity => IsUnitDrawKind(entity.Kind)))
        {
            DrawEntity(target, entity, selectedEntityId == entity.Id);
        }

        foreach (var waypoint in patrolWaypoints)
        {
            if (!world.IsInside(waypoint))
            {
                continue;
            }

            using var marker = new RectangleShape(new Vector2f(TileSize - 6f, TileSize - 6f))
            {
                Position = new Vector2f(waypoint.X * TileSize + 3f, waypoint.Y * TileSize + 3f),
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
        var drawKind = entity.Kind == EntityKind.GhostBuild && entity.BuildTargetKind is not null
            ? entity.BuildTargetKind.Value
            : entity.Kind;
        var footprint = MvpDefinitions.GetFootprint(drawKind);
        var isMobile = MvpDefinitions.UnitKinds.Contains(entity.Kind) || entity.Kind == EntityKind.Commander;
        var center = isMobile
            ? ToScreen(entity.WorldPosition)
            : new Vector2f(
                entity.Position.X * TileSize + footprint.Width * TileSize / 2f,
                entity.Position.Y * TileSize + footprint.Height * TileSize / 2f);
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
            Left: entity.Position.X * TileSize + 1f,
            Top: entity.Position.Y * TileSize + 1f,
            Width: TileSize * footprint.Width - 2f,
            Height: TileSize * footprint.Height - 2f);
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

    private static void DrawMobileUnit(IRenderTarget target, WorldEntity entity, Vector2f center, Color color, Color ink)
    {
        var radius = TileSize * 0.35f;
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

    private static void DrawGhostPreview(IRenderTarget target, EntityKind kind, TilePosition anchor, Direction pendingDirection)
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

        if (BuildBarModel.IsDirectedKind(kind))
        {
            DrawDirectionArrow(target, anchor, pendingDirection, new Color(255, 240, 120));
        }
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
        var radius = TileSize * 0.45f;
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

    private static void DrawTopBar(IRenderTarget target, GameSimulation simulation, PlayerId localPlayer, Font? font, float playfieldWidth)
    {
        using var bar = new RectangleShape(new Vector2f(playfieldWidth, TopBarHeight))
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

    private static void DrawMinimap(IRenderTarget target, GameSimulation simulation, PlayerId localPlayer, uint windowWidth)
    {
        var world = simulation.World;
        var mapW = world.Size.Width;
        var mapH = world.Size.Height;
        if (mapW <= 0 || mapH <= 0)
        {
            return;
        }

        var left = windowWidth - MinimapSize - MinimapMargin;
        var top = TopBarHeight + MinimapMargin;
        if (left < MinimapMargin)
        {
            left = MinimapMargin;
        }

        using var frame = new RectangleShape(new Vector2f(MinimapSize, MinimapSize))
        {
            Position = new Vector2f(left, top),
            FillColor = new Color(8, 10, 12, 220),
            OutlineColor = new Color(90, 105, 130),
            OutlineThickness = 1f
        };
        target.Draw(frame);

        var scaleX = MinimapSize / mapW;
        var scaleY = MinimapSize / mapH;
        var pixelW = Math.Max(1f, scaleX);
        var pixelH = Math.Max(1f, scaleY);

        using var pixel = new RectangleShape();
        for (var y = 0; y < mapH; y++)
        {
            for (var x = 0; x < mapW; x++)
            {
                var position = new TilePosition(x, y);
                var visibility = simulation.GetVisibility(localPlayer, position);
                if (visibility == VisibilityState.Unknown)
                {
                    continue;
                }

                var color = GetMinimapTerrainColor(world.GetTerrain(position), visibility);
                pixel.Size = new Vector2f(pixelW, pixelH);
                pixel.Position = new Vector2f(left + x * scaleX, top + y * scaleY);
                pixel.FillColor = color;
                target.Draw(pixel);
            }
        }

        var localTeam = simulation.GetPlayer(localPlayer).TeamId;
        foreach (var entity in world.Entities.Where(entity => entity.IsAlive && !entity.IsGarrisoned && entity.OwnerId is not null))
        {
            // Match main playfield FoW: explored tiles keep terrain, but live enemy/ally
            // positions only render while Visible (GDD §13).
            if (!IsVisibleToLocalPlayer(simulation, localPlayer, entity))
            {
                continue;
            }

            var owner = entity.OwnerId!.Value;
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
            pixel.Size = new Vector2f(w, h);
            pixel.Position = new Vector2f(left + entity.Position.X * scaleX, top + entity.Position.Y * scaleY);
            pixel.FillColor = buildingColor;
            target.Draw(pixel);
        }
    }

    private static Color GetMinimapTerrainColor(TerrainType terrain, VisibilityState visibility)
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

    private static string ShortItem(ItemId item)
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

    private static void DrawHud(
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
        var textStartY = panelTop + HudTextStartY;

        if (isResearchOverlayOpen)
        {
            var bottomReserved = BuildBarSlotSize + BuildBarBottomMargin + 8f;
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

            var displayResearchLines = WrapHudLines(lines, HudWrapCharacters).ToList();
            DrawTextLines(target, font, displayResearchLines, panelX, textStartY);
            return;
        }

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
                lines.Add($"World: {selected.WorldPosition.X:0.00},{selected.WorldPosition.Y:0.00}");
                lines.Add($"Move target: {(selected.MoveTarget is null ? "-" : $"{selected.MoveTarget.Value.X},{selected.MoveTarget.Value.Y}")}");
                lines.Add($"Queued: {(selected.QueuedBuildOrder is null ? "-" : $"{selected.QueuedBuildOrder.TargetKind}@{selected.QueuedBuildOrder.TargetPosition.X},{selected.QueuedBuildOrder.TargetPosition.Y}")}");
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
                lines.Add("1-5: set assembler recipe");
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
        }

        var displayLines = new List<string>();
        var panelWidth = windowWidth - panelX;
        for (var rawIndex = 0; rawIndex < lines.Count; rawIndex++)
        {
            var wrappedChunk = WrapHudLines(new[] { lines[rawIndex] }, HudWrapCharacters).ToList();
            if (lineItemTags.TryGetValue(rawIndex, out var tag) && displayLines.Count < MaxHudLines)
            {
                var bounds = new FloatRect(
                    new Vector2f(panelX + 4f, textStartY + displayLines.Count * HudLineHeight),
                    new Vector2f(panelWidth - 8f, HudLineHeight));
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

    private static void AddInventoryLinesWithHits(
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

    private static void AddHubInventoryLinesWithHits(
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

    private static void AddRecipeInputLinesWithHits(
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

    private static bool TryGetRecipeInputNeeds(
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

    private static bool TryHandleSidebarStorageClick(
        GameSimulation simulation,
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
                return simulation.TryDepositItemTypeToHubOrInput(commander.Id, selected.Id, hit.Item);
            }

            if (button == "Left" && hit.Kind == SidebarStorageKind.Output)
            {
                return simulation.TryWithdrawItemTypeFromHubOrOutput(commander.Id, selected.Id, hit.Item);
            }

            if (button == "Left"
                && hit.Kind == SidebarStorageKind.Input
                && selected.Kind == EntityKind.Hub)
            {
                return simulation.TryWithdrawItemTypeFromHubOrOutput(commander.Id, selected.Id, hit.Item);
            }
        }

        return false;
    }

    private static void DrawBuildingProgressBars(
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

    private static void DrawProgressBar(
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

    private static void DrawPanel(IRenderTarget target, uint windowWidth, uint windowHeight, float panelX, float panelTop)
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

    private static string FormatCost(IReadOnlyDictionary<ItemId, int> cost)
    {
        return string.Join(", ", cost.Select(pair => $"{pair.Value} {pair.Key}"));
    }

    private static void DrawTextLines(IRenderTarget target, Font font, IReadOnlyList<string> lines, float panelX, float startY)
    {
        for (var i = 0; i < lines.Count && i < MaxHudLines; i++)
        {
            using var text = new Text(font, lines[i], 12)
            {
                FillColor = Color.White,
                Position = new Vector2f(panelX + 8f, startY + i * HudLineHeight)
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

    private static void EnsureLocalCommanderSelected(GameSimulation simulation, PlayerId localPlayer, ref int? selectedEntityId)
    {
        var selected = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
        if (selected?.Kind == EntityKind.Commander && selected.OwnerId == localPlayer)
        {
            return;
        }

        selectedEntityId = simulation.World.Entities
            .First(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Commander && entity.IsAlive)
            .Id;
    }

    private static void CenterCameraOnWorldPosition(
        WorldPosition worldPosition,
        float playfieldWidth,
        float playfieldHeight,
        ref float cameraX,
        ref float cameraY)
    {
        cameraX = (float)(worldPosition.X * TileSize) - playfieldWidth / 2f;
        cameraY = (float)(worldPosition.Y * TileSize) - playfieldHeight / 2f;
    }

    private static bool TrySelectOwnedBastionByIndex(
        GameSimulation simulation,
        PlayerId localPlayer,
        int index,
        ref int? selectedEntityId)
    {
        var bastions = simulation.World.Entities
            .Where(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Bastion && entity.IsAlive)
            .OrderBy(entity => entity.Id)
            .ToList();
        if (index < 0 || index >= bastions.Count)
        {
            return false;
        }

        selectedEntityId = bastions[index].Id;
        return true;
    }

    private static bool IsCombatHudKind(EntityKind kind)
    {
        return kind == EntityKind.Commander
            || MvpDefinitions.UnitKinds.Contains(kind)
            || kind is EntityKind.MachineGunTurret or EntityKind.CannonTurret or EntityKind.AntiAirTurret;
    }

    private static Direction RotateDirection(Direction direction, bool clockwise)
    {
        return clockwise
            ? direction switch
            {
                Direction.North => Direction.East,
                Direction.East => Direction.South,
                Direction.South => Direction.West,
                Direction.West => Direction.North,
                _ => direction
            }
            : direction switch
            {
                Direction.North => Direction.West,
                Direction.West => Direction.South,
                Direction.South => Direction.East,
                Direction.East => Direction.North,
                _ => direction
            };
    }

    private static IEnumerable<ProductionRecipe> GetFactoryRecipes(EntityKind factoryKind)
    {
        return MvpDefinitions.ProductionRecipes.Values
            .Where(recipe => factoryKind == EntityKind.DroneCenter
                ? recipe.OutputKind == EntityKind.Scout
                : recipe.OutputKind != EntityKind.Scout)
            .OrderBy(recipe => (int)recipe.OutputKind);
    }

    private static bool IsUnitDrawKind(EntityKind kind) =>
        MvpDefinitions.UnitKinds.Contains(kind) || kind == EntityKind.Commander;

    private static void ToggleResearchAllocation(GameSimulation simulation, PlayerId playerId)
    {
        var snapshot = simulation.GetResearchSnapshot(playerId);
        if (snapshot.Tracks.Count < 2 || !snapshot.Tracks.Any(track => track.PlayerAdjustableAllocation))
        {
            return;
        }

        var cycle = snapshot.Tracks.FirstOrDefault(track => track.Id.Contains("cycle", StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Tracks[0];
        var tactical = snapshot.Tracks.FirstOrDefault(track => track.Id != cycle.Id) ?? snapshot.Tracks[^1];
        var fullCycle = cycle.AllocationBasisPoints >= 10_000;
        simulation.TrySetTrackAllocation(playerId, new Dictionary<string, int>
        {
            [cycle.Id] = fullCycle ? 7_000 : 10_000,
            [tactical.Id] = fullCycle ? 3_000 : 0
        });
    }

    private static void DrawResearchTreeOverlay(IRenderTarget target, ResearchTreePanelModel tree, Font? font)
    {
        using var backdrop = new RectangleShape(new Vector2f(tree.OverlayBounds.Width, tree.OverlayBounds.Height))
        {
            Position = new Vector2f(tree.OverlayBounds.Left, tree.OverlayBounds.Top),
            FillColor = new Color(8, 12, 18, 230),
            OutlineColor = new Color(100, 120, 150),
            OutlineThickness = 1f
        };
        target.Draw(backdrop);

        if (font is not null)
        {
            using var title = new Text(font, $"Research [{tree.ProfileId}] tier={tree.CurrentTierId}", 14)
            {
                FillColor = new Color(220, 230, 240),
                Position = new Vector2f(tree.OverlayBounds.Left + 10f, tree.OverlayBounds.Top + 6f)
            };
            target.Draw(title);
        }

        foreach (var node in tree.Nodes)
        {
            var fill = node.Status switch
            {
                ResearchTreeNodeStatus.Completed => new Color(50, 110, 210),
                ResearchTreeNodeStatus.Available => new Color(50, 170, 80),
                ResearchTreeNodeStatus.Active => new Color(70, 190, 210),
                _ => new Color(180, 55, 55)
            };
            var outline = tree.SelectedId == node.Id ? Color.White : new Color(20, 20, 24);
            using var icon = new RectangleShape(new Vector2f(node.Bounds.Width, node.Bounds.Height))
            {
                Position = new Vector2f(node.Bounds.Left, node.Bounds.Top),
                FillColor = fill,
                OutlineColor = outline,
                OutlineThickness = tree.SelectedId == node.Id ? 2f : 1f
            };
            target.Draw(icon);

            if (font is not null)
            {
                using var glyph = new Text(font, node.Symbol, 16)
                {
                    FillColor = Color.White,
                    Position = new Vector2f(node.Bounds.Left + 11f, node.Bounds.Top + 6f)
                };
                target.Draw(glyph);
            }
        }

        DrawUiButton(target, font, tree.ExitButtonBounds, "Exit", new Color(70, 80, 100));
        var actionEnabled = tree.CanStartSelected || tree.CanCancelSelected;
        DrawUiButton(
            target,
            font,
            tree.ActionButtonBounds,
            tree.ActionButtonLabel,
            actionEnabled ? new Color(60, 120, 80) : new Color(50, 55, 65));
        if (tree.SupportsAllocationToggle)
        {
            DrawUiButton(target, font, tree.AllocationButtonBounds, "Alloc cycle/tact", new Color(70, 90, 130));
        }
    }

    private static void DrawUiButton(IRenderTarget target, Font? font, FloatRect bounds, string label, Color fill)
    {
        using var button = new RectangleShape(new Vector2f(bounds.Width, bounds.Height))
        {
            Position = new Vector2f(bounds.Left, bounds.Top),
            FillColor = fill,
            OutlineColor = new Color(140, 160, 190),
            OutlineThickness = 1f
        };
        target.Draw(button);
        if (font is null)
        {
            return;
        }

        using var text = new Text(font, label, 12)
        {
            FillColor = Color.White,
            Position = new Vector2f(bounds.Left + 8f, bounds.Top + 5f)
        };
        target.Draw(text);
    }

    private static void ApplyBastionOrderCommand(
        GameSimulation simulation,
        int bastionId,
        BastionOrderCommand command,
        ref BastionPendingInputMode pendingMode,
        List<TilePosition> patrolWaypoints)
    {
        patrolWaypoints.Clear();
        switch (command)
        {
            case BastionOrderCommand.ActiveDefense:
                pendingMode = BastionPendingInputMode.None;
                simulation.TryIssueBastionOrder(bastionId, new BastionOrder(BastionOrderKind.Defend));
                break;
            case BastionOrderCommand.Patrol:
                pendingMode = BastionPendingInputMode.PatrolWaypoints;
                break;
            case BastionOrderCommand.Attack:
                pendingMode = BastionPendingInputMode.AttackTarget;
                break;
            case BastionOrderCommand.Scout:
                pendingMode = BastionPendingInputMode.ScoutTarget;
                break;
        }
    }

    private static bool TryAdjustBastionTemplate(string key, GameSimulation simulation, WorldEntity bastion, int templateUnitIndex)
    {
        if (bastion.OwnerId is null)
        {
            return false;
        }

        var kinds = BastionCompositionPanelModel.UnlockedUnitKinds(simulation, bastion.OwnerId.Value);
        if (kinds.Length == 0)
        {
            return false;
        }

        var unitKind = kinds[Math.Clamp(templateUnitIndex, 0, kinds.Length - 1)];
        var current = bastion.BastionTemplate.GetValueOrDefault(unitKind);
        var delta = key switch
        {
            "Equal" or "Add" => 1,
            "Hyphen" or "Subtract" => -1,
            _ => 0
        };
        if (delta == 0)
        {
            return false;
        }

        simulation.TrySetBastionTemplate(bastion.Id, unitKind, Math.Max(0, current + delta));
        return true;
    }

    private static void DrawBastionCompositionPanel(
        IRenderTarget target,
        GameSimulation simulation,
        WorldEntity bastion,
        int templateUnitIndex,
        Font? font,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        Vector2i mousePosition)
    {
        var slots = BastionCompositionPanelModel.BuildSlots(simulation, bastion);
        if (slots.Length == 0)
        {
            return;
        }

        var bounds = BastionCompositionPanelModel.GetPanelBounds(
            windowWidth,
            windowHeight,
            panelX,
            slots.Length,
            out var contentX,
            out var contentY);
        using var backdrop = new RectangleShape(new Vector2f(bounds.Width, bounds.Height))
        {
            Position = new Vector2f(bounds.Left, bounds.Top),
            FillColor = new Color(10, 14, 20, 220),
            OutlineColor = new Color(110, 140, 180),
            OutlineThickness = 1f
        };
        target.Draw(backdrop);

        if (font is not null)
        {
            using var title = new Text(font, "Bastion composition")
            {
                CharacterSize = 14,
                FillColor = new Color(230, 230, 210),
                Position = new Vector2f(bounds.Left + BastionCompositionPanelModel.PanelPadding, bounds.Top + 4f)
            };
            target.Draw(title);
        }

        string? tooltip = null;
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            var slotBounds = BastionCompositionPanelModel.GetSlotBounds(contentX, contentY, i);
            var selected = i == Math.Clamp(templateUnitIndex, 0, slots.Length - 1);
            using var frame = new RectangleShape(new Vector2f(slotBounds.Width, slotBounds.Height))
            {
                Position = new Vector2f(slotBounds.Left, slotBounds.Top),
                FillColor = selected ? new Color(60, 90, 130, 235) : new Color(40, 50, 65, 230),
                OutlineColor = selected ? new Color(255, 230, 120) : new Color(100, 120, 150),
                OutlineThickness = selected ? 2f : 1f
            };
            target.Draw(frame);

            if (font is not null)
            {
                using var glyph = new Text(font, slot.Glyph)
                {
                    CharacterSize = 20,
                    FillColor = Color.White,
                    Position = new Vector2f(slotBounds.Left + 34f, slotBounds.Top + 6f)
                };
                target.Draw(glyph);

                using var counts = new Text(font, BastionCompositionPanelModel.CountLabel(slot.LiveCount, slot.TemplateMax))
                {
                    CharacterSize = 13,
                    FillColor = new Color(220, 230, 240),
                    Position = new Vector2f(slotBounds.Left + 22f, slotBounds.Top + 30f)
                };
                target.Draw(counts);
            }

            DrawCompositionButton(
                target,
                font,
                BastionCompositionPanelModel.GetMinusButtonBounds(slotBounds),
                "-",
                mousePosition,
                ref tooltip,
                BastionCompositionPanelModel.Tooltip(slot) + " · -");
            DrawCompositionButton(
                target,
                font,
                BastionCompositionPanelModel.GetPlusButtonBounds(slotBounds),
                "+",
                mousePosition,
                ref tooltip,
                BastionCompositionPanelModel.Tooltip(slot) + " · +");
        }

        if (tooltip is not null && font is not null)
        {
            using var tip = new Text(font, tooltip)
            {
                CharacterSize = 13,
                FillColor = Color.White,
                Position = new Vector2f(bounds.Left, bounds.Top - 20f)
            };
            target.Draw(tip);
        }
    }

    private static void DrawCompositionButton(
        IRenderTarget target,
        Font? font,
        FloatRect buttonBounds,
        string label,
        Vector2i mousePosition,
        ref string? tooltip,
        string hoverTooltip)
    {
        var hovered = mousePosition.X >= buttonBounds.Left
            && mousePosition.Y >= buttonBounds.Top
            && mousePosition.X < buttonBounds.Left + buttonBounds.Width
            && mousePosition.Y < buttonBounds.Top + buttonBounds.Height;
        using var button = new RectangleShape(new Vector2f(buttonBounds.Width, buttonBounds.Height))
        {
            Position = new Vector2f(buttonBounds.Left, buttonBounds.Top),
            FillColor = hovered ? new Color(90, 120, 160, 240) : new Color(55, 65, 80, 235),
            OutlineColor = new Color(140, 160, 190),
            OutlineThickness = 1f
        };
        target.Draw(button);

        if (font is not null)
        {
            using var text = new Text(font, label)
            {
                CharacterSize = 14,
                FillColor = Color.White,
                Position = new Vector2f(buttonBounds.Left + 6f, buttonBounds.Top + 1f)
            };
            target.Draw(text);
        }

        if (hovered)
        {
            tooltip = hoverTooltip;
        }
    }

    private static bool TryHandleBastionPendingMapClick(
        GameSimulation simulation,
        WorldEntity? selectedEntity,
        PlayerId localPlayer,
        BastionPendingInputMode pendingMode,
        List<TilePosition> patrolWaypoints,
        TilePosition tile,
        bool confirmPatrol,
        out bool consumed)
    {
        consumed = false;
        if (pendingMode == BastionPendingInputMode.None
            || selectedEntity?.Kind != EntityKind.Bastion
            || selectedEntity.OwnerId != localPlayer)
        {
            return false;
        }

        if (pendingMode == BastionPendingInputMode.AttackTarget)
        {
            consumed = simulation.TryIssueBastionOrder(
                selectedEntity.Id,
                new BastionOrder(BastionOrderKind.AttackArea, tile));
            return true;
        }

        if (pendingMode == BastionPendingInputMode.ScoutTarget)
        {
            consumed = simulation.TryIssueBastionOrder(
                selectedEntity.Id,
                new BastionOrder(BastionOrderKind.Scout, tile));
            return true;
        }

        if (pendingMode == BastionPendingInputMode.PatrolWaypoints)
        {
            if (confirmPatrol)
            {
                if (patrolWaypoints.Count is >= 2 and <= 4)
                {
                    consumed = simulation.TryIssueBastionOrder(
                        selectedEntity.Id,
                        new BastionOrder(BastionOrderKind.Patrol, Waypoints: patrolWaypoints.ToArray()));
                }

                return true;
            }

            if (patrolWaypoints.Count < 4)
            {
                patrolWaypoints.Add(tile);
            }

            consumed = true;
            return true;
        }

        return false;
    }

    private static FloatRect GetOrderBarBounds(uint windowWidth, uint windowHeight, float panelX, int slotCount, out float startX, out float barY)
    {
        var totalWidth = slotCount * BuildBarSlotSize;
        var available = Math.Max(BuildBarSlotSize, panelX - 16f);
        startX = Math.Max(8f, (available - totalWidth) * 0.5f);
        barY = windowHeight - BuildBarSlotSize - BuildBarBottomMargin;
        return new FloatRect(new Vector2f(startX, barY), new Vector2f(Math.Min(totalWidth, available), BuildBarSlotSize));
    }

    private static bool TryPickBastionOrderCommand(
        Vector2i mousePosition,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        out BastionOrderCommand command)
    {
        command = default;
        var bounds = GetOrderBarBounds(
            windowWidth,
            windowHeight,
            panelX,
            BastionOrderBarModel.Commands.Length,
            out var startX,
            out var barY);
        if (mousePosition.X < bounds.Left
            || mousePosition.Y < bounds.Top
            || mousePosition.X >= bounds.Left + bounds.Width
            || mousePosition.Y >= bounds.Top + bounds.Height)
        {
            return false;
        }

        var index = (int)((mousePosition.X - startX) / BuildBarSlotSize);
        return BastionOrderBarModel.TryGetCommand(index, out command);
    }

    private static void DrawBastionOrderBar(
        IRenderTarget target,
        WorldEntity bastion,
        BastionPendingInputMode pendingMode,
        Font? font,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        Vector2i mousePosition)
    {
        var bounds = GetOrderBarBounds(
            windowWidth,
            windowHeight,
            panelX,
            BastionOrderBarModel.Commands.Length,
            out var startX,
            out var barY);
        using var backdrop = new RectangleShape(new Vector2f(bounds.Width + 8f, bounds.Height + 8f))
        {
            Position = new Vector2f(bounds.Left - 4f, bounds.Top - 4f),
            FillColor = new Color(10, 14, 20, 210),
            OutlineColor = new Color(90, 110, 140),
            OutlineThickness = 1f
        };
        target.Draw(backdrop);

        string? tooltip = null;
        for (var i = 0; i < BastionOrderBarModel.Commands.Length; i++)
        {
            var command = BastionOrderBarModel.Commands[i];
            var slotX = startX + i * BuildBarSlotSize;
            var selected = BastionOrderBarModel.IsCommandHighlighted(command, pendingMode, bastion.Order.Kind);
            var fill = selected
                ? new Color(70, 110, 160, 235)
                : new Color(45, 55, 70, 230);

            using var slot = new RectangleShape(new Vector2f(BuildBarSlotSize - 4f, BuildBarSlotSize - 4f))
            {
                Position = new Vector2f(slotX + 2f, barY + 2f),
                FillColor = fill,
                OutlineColor = selected ? new Color(255, 230, 120) : new Color(100, 120, 150),
                OutlineThickness = selected ? 2f : 1f
            };
            target.Draw(slot);

            if (font is not null)
            {
                using var glyph = new Text(font, BastionOrderBarModel.Glyph(command))
                {
                    CharacterSize = 18,
                    FillColor = Color.White,
                    Position = new Vector2f(slotX + 14f, barY + 10f)
                };
                target.Draw(glyph);

                var badge = BastionOrderBarModel.ShortcutBadge(command);
                if (badge is not null)
                {
                    using var keyBadge = new Text(font, badge)
                    {
                        CharacterSize = 11,
                        FillColor = new Color(230, 230, 200),
                        Position = new Vector2f(slotX + BuildBarSlotSize - 16f, barY + 2f)
                    };
                    target.Draw(keyBadge);
                }
            }

            if (mousePosition.X >= slotX
                && mousePosition.X < slotX + BuildBarSlotSize
                && mousePosition.Y >= barY
                && mousePosition.Y < barY + BuildBarSlotSize)
            {
                tooltip = BastionOrderBarModel.Label(command);
            }
        }

        if (tooltip is not null && font is not null)
        {
            using var tip = new Text(font, tooltip)
            {
                CharacterSize = 13,
                FillColor = Color.White,
                Position = new Vector2f(bounds.Left, barY - 22f)
            };
            target.Draw(tip);
        }
    }

    private static FloatRect GetBuildBarBounds(uint windowWidth, uint windowHeight, float panelX, out float startX, out float barY)
    {
        return GetOrderBarBounds(windowWidth, windowHeight, panelX, BuildMenuCatalog.BuildableKinds.Length, out startX, out barY);
    }

    private static bool TryPickBuildBarKind(Vector2i mousePosition, uint windowWidth, uint windowHeight, float panelX, out EntityKind kind)
    {
        kind = default;
        var bounds = GetBuildBarBounds(windowWidth, windowHeight, panelX, out var startX, out var barY);
        if (mousePosition.X < bounds.Position.X
            || mousePosition.Y < bounds.Position.Y
            || mousePosition.X >= bounds.Position.X + bounds.Size.X
            || mousePosition.Y >= bounds.Position.Y + bounds.Size.Y)
        {
            return false;
        }

        var index = (int)((mousePosition.X - startX) / BuildBarSlotSize);
        if (index < 0 || index >= BuildMenuCatalog.BuildableKinds.Length)
        {
            return false;
        }

        kind = BuildMenuCatalog.BuildableKinds[index];
        return true;
    }

    private static void DrawBuildBar(
        IRenderTarget target,
        GameSimulation simulation,
        PlayerId localPlayer,
        int? selectedEntityId,
        EntityKind? pendingBuildKind,
        Direction pendingDirection,
        ItemRecipeId? pendingRecipe,
        Font? font,
        uint windowWidth,
        uint windowHeight,
        float panelX,
        Vector2i mousePosition)
    {
        var commander = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
        if (commander?.Kind != EntityKind.Commander || commander.OwnerId != localPlayer)
        {
            commander = simulation.World.Entities.FirstOrDefault(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Commander && entity.IsAlive);
        }

        var inventory = commander?.Inventory ?? new Inventory();
        var bounds = GetBuildBarBounds(windowWidth, windowHeight, panelX, out var startX, out var barY);
        using var backdrop = new RectangleShape(new Vector2f(bounds.Size.X + 8f, bounds.Size.Y + 8f))
        {
            Position = new Vector2f(bounds.Position.X - 4f, bounds.Position.Y - 4f),
            FillColor = new Color(10, 14, 20, 210),
            OutlineColor = new Color(90, 110, 140),
            OutlineThickness = 1f
        };
        target.Draw(backdrop);

        string? tooltip = null;
        for (var i = 0; i < BuildMenuCatalog.BuildableKinds.Length; i++)
        {
            var kind = BuildMenuCatalog.BuildableKinds[i];
            var slotX = startX + i * BuildBarSlotSize;
            var affordable = BuildBarModel.AffordableBuilds(kind, inventory);
            var selected = pendingBuildKind == kind;
            var fill = affordable <= 0
                ? new Color(35, 40, 48, 220)
                : selected
                    ? new Color(70, 110, 160, 235)
                    : new Color(45, 55, 70, 230);

            using var slot = new RectangleShape(new Vector2f(BuildBarSlotSize - 4f, BuildBarSlotSize - 4f))
            {
                Position = new Vector2f(slotX + 2f, barY + 2f),
                FillColor = fill,
                OutlineColor = selected ? new Color(255, 230, 120) : new Color(100, 120, 150),
                OutlineThickness = selected ? 2f : 1f
            };
            target.Draw(slot);

            if (font is not null)
            {
                using var glyph = new Text(font, BuildBarModel.Glyph(kind))
                {
                    CharacterSize = 18,
                    FillColor = affordable <= 0 ? new Color(110, 110, 120) : Color.White,
                    Position = new Vector2f(slotX + 14f, barY + 10f)
                };
                target.Draw(glyph);

                using var afford = new Text(font, BuildBarModel.AffordLabel(affordable))
                {
                    CharacterSize = 11,
                    FillColor = affordable <= 0 ? new Color(160, 80, 80) : new Color(200, 220, 180),
                    Position = new Vector2f(slotX + 4f, barY + BuildBarSlotSize - 18f)
                };
                target.Draw(afford);

                var badge = BuildBarModel.ShortcutBadge(i);
                if (badge is not null)
                {
                    using var keyBadge = new Text(font, badge)
                    {
                        CharacterSize = 11,
                        FillColor = new Color(230, 230, 200),
                        Position = new Vector2f(slotX + BuildBarSlotSize - 16f, barY + 2f)
                    };
                    target.Draw(keyBadge);
                }
            }

            if (mousePosition.X >= slotX
                && mousePosition.X < slotX + BuildBarSlotSize
                && mousePosition.Y >= barY
                && mousePosition.Y < barY + BuildBarSlotSize)
            {
                tooltip = BuildBarModel.Tooltip(
                    kind,
                    affordable,
                    selected && BuildBarModel.IsDirectedKind(kind) ? pendingDirection : Direction.East,
                    selected && kind == EntityKind.Assembler ? pendingRecipe : null);
            }
        }

        if (tooltip is not null && font is not null)
        {
            using var tip = new Text(font, tooltip)
            {
                CharacterSize = 13,
                FillColor = Color.White,
                Position = new Vector2f(bounds.Left, barY - 22f)
            };
            target.Draw(tip);
        }
    }
}
