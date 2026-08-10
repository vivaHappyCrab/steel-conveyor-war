using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// Thin SFML host loop: window lifecycle, fixed-tick stepping, and composition of
/// input / camera / world render / UI overlay modules. No gameplay rules live here.
/// </summary>
public sealed class SfmlGameRunner
{
    public void Run(GameSimulation simulation, int? maxFrames = null, SfmlDisplayOptions? display = null)
    {
        display ??= SfmlDisplayOptions.Default;
        var windowWidth = display.Width;
        var windowHeight = display.Height;
        var panelX = Math.Max(0f, windowWidth - SfmlUiLayout.SidePanelWidth);
        var panelTop = SfmlUiLayout.MinimapSize;
        var playfieldWidth = Math.Max(1f, panelX);
        var playfieldHeight = Math.Max(1f, windowHeight - SfmlUiLayout.TopBarHeight);
        var worldWidthPx = simulation.World.Size.Width * SfmlUiLayout.TileSize;
        var worldHeightPx = simulation.World.Size.Height * SfmlUiLayout.TileSize;
        var camera = new GameCamera(
            0f,
            Math.Max(0f, (simulation.World.Size.Height / 2f) * SfmlUiLayout.TileSize - playfieldHeight / 2f));
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
        var isEnergyOverlayOpen = false;
        var isBastionCompositionOpen = false;
        var energySelectedInterval = EnergyStatsWindowKind.Seconds30;
        TechnologyId? researchSelectedId = null;
        TechnologyId? researchLastClickId = null;
        var researchLastClickSeconds = -1f;
        var researchScrollY = 0f;
        var researchClickClock = new Clock();
        var font = SfmlFontLoader.TryLoadFont();
        int? demolishHoldEntityId = null;
        var demolishHoldElapsed = 0f;
        var demolishHoldCommitted = false;

        void ClearDemolishHold()
        {
            demolishHoldEntityId = null;
            demolishHoldElapsed = 0f;
            demolishHoldCommitted = false;
        }

        void CloseResearchOverlay()
        {
            isResearchOverlayOpen = false;
            researchSelectedId = null;
            researchLastClickId = null;
            researchScrollY = 0f;
        }

        void CloseEnergyOverlay()
        {
            isEnergyOverlayOpen = false;
        }

        void CloseBastionComposition()
        {
            isBastionCompositionOpen = false;
        }

        float ClampResearchScroll(ResearchTreePanelModel tree) =>
            ResearchTreePanelModel.ClampScroll(researchScrollY, tree.ContentHeight, tree.ContentViewport.Height);

        FloatRect GetMinimapBounds() =>
            new(
                new Vector2f(windowWidth - SfmlUiLayout.MinimapSize, 0f),
                new Vector2f(SfmlUiLayout.MinimapSize, SfmlUiLayout.MinimapSize));

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

        TilePosition? TileFromMinimap(Vector2i screen)
        {
            if (!IsOverMinimap(screen))
            {
                return null;
            }

            var bounds = GetMinimapBounds();
            var mapW = simulation.World.Size.Width;
            var mapH = simulation.World.Size.Height;
            if (mapW <= 0 || mapH <= 0)
            {
                return null;
            }

            var localX = (screen.X - bounds.Left) / bounds.Width;
            var localY = (screen.Y - bounds.Top) / bounds.Height;
            var tileX = (int)Math.Clamp(localX * mapW, 0, mapW - 1);
            var tileY = (int)Math.Clamp(localY * mapH, 0, mapH - 1);
            return new TilePosition(tileX, tileY);
        }

        void CenterCameraOnTile(TilePosition tile)
        {
            camera.CenterOnWorldPosition(WorldPosition.FromTileCenter(tile), playfieldWidth, playfieldHeight);
            ClampCamera();
        }

        void ClearBastionPending()
        {
            bastionPendingMode = BastionPendingInputMode.None;
            patrolWaypoints.Clear();
        }

        void ClampCamera()
        {
            camera.Clamp(worldWidthPx, worldHeightPx, playfieldWidth, playfieldHeight);
        }

        ClampCamera();

        View CreateWorldView() =>
            camera.CreateWorldView(playfieldWidth, playfieldHeight, windowWidth, windowHeight);

        bool IsInPlayfield(Vector2i screen)
        {
            return screen.X >= 0
                && screen.X < playfieldWidth
                && screen.Y >= SfmlUiLayout.TopBarHeight
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
            var tile = new TilePosition((int)(world.X / SfmlUiLayout.TileSize), (int)(world.Y / SfmlUiLayout.TileSize));
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

            if (key == "Escape" && isEnergyOverlayOpen)
            {
                CloseEnergyOverlay();
                return;
            }

            if (key == "Escape" && isResearchOverlayOpen)
            {
                CloseResearchOverlay();
                return;
            }

            if (key == "Escape" && isBastionCompositionOpen)
            {
                CloseBastionComposition();
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
                SfmlInputHelpers.EnsureLocalCommanderSelected(simulation, localPlayer, ref selectedEntityId);
                selectedEntity = simulation.World.GetEntity(selectedEntityId!.Value);
                if (selectedEntity is not null)
                {
                    camera.CenterOnWorldPosition(selectedEntity.WorldPosition, playfieldWidth, playfieldHeight);
                    ClampCamera();
                }

                isBuildMenuOpen = false;
                pendingBuildKind = null;
                pendingDirection = Direction.East;
                pendingRecipe = null;
                ClearDemolishHold();
                CloseBastionComposition();
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
                        && WorldRenderer.IsVisibleToLocalPlayer(simulation, localPlayer, hoverEntity)
                        && BuildBarModel.TryCopyFromWorldEntity(hoverEntity, out var copyKind, out var copyDirection, out var copyRecipe))
                    {
                        SfmlInputHelpers.EnsureLocalCommanderSelected(simulation, localPlayer, ref selectedEntityId);
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
                ClearDemolishHold();
                return;
            }

            if (key == "S"
                && selectedEntity?.Kind == EntityKind.Commander
                && selectedEntity.OwnerId == localPlayer)
            {
                simulation.TryStopCommander(selectedEntity.Id);
                ClearDemolishHold();
                return;
            }

            if (key == "R")
            {
                var counterClockwise = Keyboard.IsKeyPressed(Keyboard.Key.LShift) || Keyboard.IsKeyPressed(Keyboard.Key.RShift);
                if (isBuildMenuOpen && pendingBuildKind is not null && BuildBarModel.IsDirectedKind(pendingBuildKind.Value))
                {
                    pendingDirection = SfmlInputHelpers.RotateDirection(pendingDirection, clockwise: !counterClockwise);
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
                && isBastionCompositionOpen
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
                CloseEnergyOverlay();
                CloseBastionComposition();
                if (isResearchOverlayOpen)
                {
                    CloseResearchOverlay();
                }
                else
                {
                    isResearchOverlayOpen = true;
                    researchScrollY = 0f;
                }

                return;
            }

            if (key == "P")
            {
                CloseResearchOverlay();
                CloseBastionComposition();
                if (isEnergyOverlayOpen)
                {
                    CloseEnergyOverlay();
                }
                else
                {
                    isEnergyOverlayOpen = true;
                }

                return;
            }

            if (key == "E"
                && selectedEntity?.Kind == EntityKind.Bastion
                && selectedEntity.OwnerId == localPlayer)
            {
                CloseResearchOverlay();
                CloseEnergyOverlay();
                isBastionCompositionOpen = !isBastionCompositionOpen;
                return;
            }

            if (!isBuildMenuOpen && selectedEntity?.Kind == EntityKind.Assembler && SfmlInputHelpers.TryGetRecipeShortcut(key, out var recipeId))
            {
                simulation.TrySetAssemblerRecipe(selectedEntity.Id, recipeId);
                recipePage = 0;
                return;
            }

            if (!isResearchOverlayOpen && !isEnergyOverlayOpen && !isBuildMenuOpen && selectedEntity?.Kind == EntityKind.Laboratory && SfmlInputHelpers.TryGetNumberShortcut(key, out var researchIndex))
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
                if (SfmlInputHelpers.TryGetNumberShortcut(key, out var factoryRecipeIndex))
                {
                    var recipes = HudOverlay.GetFactoryRecipes(selectedEntity.Kind).ToList();
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
                    BastionUiOverlay.ApplyBastionOrderCommand(simulation, selectedEntity.Id, orderCommand, ref bastionPendingMode, patrolWaypoints);
                    return;
                }

                if (SfmlInputHelpers.TryGetNumberShortcut(key, out var bastionIndex)
                    && SfmlInputHelpers.TrySelectOwnedBastionByIndex(simulation, localPlayer, bastionIndex, ref selectedEntityId))
                {
                    recipePage = 0;
                    templateUnitIndex = 0;
                    ClearBastionPending();
                    return;
                }

                if (BastionUiOverlay.TryAdjustBastionTemplate(key, simulation, selectedEntity, templateUnitIndex))
                {
                    return;
                }
            }

            if (!isBuildMenuOpen)
            {
                return;
            }

            if (SfmlInputHelpers.TryGetNumberShortcut(key, out var buildIndex) && buildIndex < BuildMenuCatalog.BuildableKinds.Length)
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
                && BuildBarOverlay.TryPickBuildBarKind(mousePosition, windowWidth, windowHeight, panelX, out var barKind))
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

            if (isEnergyOverlayOpen && button == "Left")
            {
                var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                var stats = simulation.GetPlayer(localPlayer).EnergyStats.Query((int)energySelectedInterval);
                var panel = EnergyStatsPanelModel.Build(
                    stats,
                    energySelectedInterval,
                    windowWidth,
                    windowHeight,
                    panelX,
                    bottomReserved);
                if (panel.HitExit(mousePosition))
                {
                    CloseEnergyOverlay();
                    return;
                }

                if (panel.TryHitInterval(mousePosition, out var interval))
                {
                    energySelectedInterval = interval;
                    return;
                }

                if (panel.ContainsOverlay(mousePosition))
                {
                    return;
                }
            }

            if (isResearchOverlayOpen && button == "Left")
            {
                CloseEnergyOverlay();
                var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
                var snapshot = simulation.GetResearchSnapshot(localPlayer);
                var tree = ResearchTreePanelModel.FromSnapshot(snapshot, overlayBounds, researchSelectedId);
                researchScrollY = ClampResearchScroll(tree);

                if (tree.HitExit(mousePosition))
                {
                    CloseResearchOverlay();
                    return;
                }

                if (tree.HitAllocation(mousePosition))
                {
                    SfmlInputHelpers.ToggleResearchAllocation(simulation, localPlayer);
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

                if (tree.TryPickNode(mousePosition, researchScrollY, out var techId))
                {
                    var now = researchClickClock.ElapsedTime.AsSeconds();
                    var isDouble = researchLastClickId == techId
                        && now - researchLastClickSeconds <= SfmlUiLayout.ResearchDoubleClickSeconds;
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
                && HudOverlay.TryHandleSidebarStorageClick(
                    simulation,
                    localPlayer,
                    selectedEntityId,
                    sidebarStorageHits,
                    mousePosition,
                    button))
            {
                return;
            }

            if (IsOverMinimap(mousePosition))
            {
                var minimapTile = TileFromMinimap(mousePosition);
                if (minimapTile is null)
                {
                    return;
                }

                if (button == "Left")
                {
                    CenterCameraOnTile(minimapTile.Value);
                    return;
                }

                if (button == "Right")
                {
                    var selectedEntity = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
                    if (selectedEntity?.Kind == EntityKind.Commander && selectedEntity.OwnerId == localPlayer)
                    {
                        var ctrlPressed = Keyboard.IsKeyPressed(Keyboard.Key.LControl) || Keyboard.IsKeyPressed(Keyboard.Key.RControl);
                        var clickedEntity = simulation.World.GetTopEntityAt(minimapTile.Value);
                        if (ctrlPressed
                            && clickedEntity is not null
                            && simulation.TryDepositToHubOrInput(selectedEntity.Id, clickedEntity.Id))
                        {
                            return;
                        }

                        simulation.TryIssueMoveCommand(selectedEntity.Id, minimapTile.Value);
                        return;
                    }

                    if (BastionUiOverlay.TryHandleBastionPendingMapClick(
                            simulation,
                            selectedEntity,
                            localPlayer,
                            bastionPendingMode,
                            patrolWaypoints,
                            minimapTile.Value,
                            confirmPatrol: true,
                            out var consumedRight)
                        && consumedRight)
                    {
                        ClearBastionPending();
                    }
                }

                return;
            }

            if (IsOverSidePanel(mousePosition))
            {
                return;
            }

            var selectedForBar = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
            if (button == "Left"
                && !isBuildMenuOpen
                && !isResearchOverlayOpen
                && !isEnergyOverlayOpen
                && selectedForBar?.Kind == EntityKind.Bastion
                && selectedForBar.OwnerId == localPlayer)
            {
                if (isBastionCompositionOpen)
                {
                    var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                    var compositionSlots = BastionCompositionPanelModel.BuildSlots(simulation, selectedForBar);
                    if (BastionCompositionPanelModel.HitExit(
                            mousePosition,
                            windowWidth,
                            windowHeight,
                            panelX,
                            bottomReserved))
                    {
                        CloseBastionComposition();
                        return;
                    }

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
                }

                if (BastionUiOverlay.TryPickBastionOrderCommand(mousePosition, windowWidth, windowHeight, panelX, out var barCommand))
                {
                    BastionUiOverlay.ApplyBastionOrderCommand(simulation, selectedForBar.Id, barCommand, ref bastionPendingMode, patrolWaypoints);
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
                if (BastionUiOverlay.TryHandleBastionPendingMapClick(
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
                    var previousSelectedId = selectedEntityId;
                    selectedEntityId = clickedEntity is not null && WorldRenderer.IsVisibleToLocalPlayer(simulation, localPlayer, clickedEntity)
                        ? clickedEntity.Id
                        : null;
                    if (selectedEntityId != previousSelectedId)
                    {
                        CloseBastionComposition();
                    }

                    recipePage = 0;
                    templateUnitIndex = 0;
                    isBuildMenuOpen = false;
                    pendingBuildKind = null;
                    pendingDirection = Direction.East;
                    pendingRecipe = null;
                    ClearDemolishHold();
                    ClearBastionPending();
                }
            }
            else if (button == "Right")
            {
                var selectedEntity = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
                if (selectedEntity?.Kind == EntityKind.Commander && selectedEntity.OwnerId == localPlayer)
                {
                    if (isBuildMenuOpen)
                    {
                        // Build mode: RMB starts demolish hold on a valid target; empty/invalid = no-op (not move).
                        var clickedEntity = simulation.World.GetTopEntityAt(tile.Value);
                        if (clickedEntity is not null
                            && simulation.IsDemolishableTarget(selectedEntity.Id, clickedEntity.Id))
                        {
                            demolishHoldEntityId = clickedEntity.Id;
                            demolishHoldElapsed = 0f;
                            demolishHoldCommitted = false;
                        }
                        else
                        {
                            ClearDemolishHold();
                        }

                        return;
                    }

                    var ctrlPressed = Keyboard.IsKeyPressed(Keyboard.Key.LControl) || Keyboard.IsKeyPressed(Keyboard.Key.RControl);
                    var clickedEntityMove = simulation.World.GetTopEntityAt(tile.Value);
                    if (ctrlPressed
                        && clickedEntityMove is not null
                        && simulation.TryDepositToHubOrInput(selectedEntity.Id, clickedEntityMove.Id))
                    {
                        return;
                    }

                    simulation.TryIssueMoveCommand(selectedEntity.Id, tile.Value);
                    return;
                }

                if (BastionUiOverlay.TryHandleBastionPendingMapClick(
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
        window.MouseWheelScrolled += (_, args) =>
        {
            if (!isResearchOverlayOpen)
            {
                return;
            }

            var mousePosition = Mouse.GetPosition(window);
            var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
            var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
            var tree = ResearchTreePanelModel.FromSnapshot(
                simulation.GetResearchSnapshot(localPlayer),
                overlayBounds,
                researchSelectedId);
            if (!tree.ContainsContentViewport(mousePosition) && !tree.ContainsOverlay(mousePosition))
            {
                return;
            }

            researchScrollY = ResearchTreePanelModel.ClampScroll(
                researchScrollY - args.Delta * ResearchTreePanelModel.ScrollStep,
                tree.ContentHeight,
                tree.ContentViewport.Height);
        };
        window.MouseButtonReleased += (_, args) =>
        {
            if (args.Button.ToString() == "Middle")
            {
                isMiddleDragging = false;
            }

            if (args.Button.ToString() == "Right")
            {
                ClearDemolishHold();
            }
        };

        var clock = new Clock();
        var accumulator = 0f;
        var fixedDelta = 1f / GameSimulation.TicksPerSecond;
        var lingeringShots = new List<(CombatShotEvent Shot, float Remaining)>();

        var renderedFrames = 0;

        while (window.IsOpen)
        {
            window.DispatchEvents();
            var frameDt = clock.Restart().AsSeconds();

            if (isBuildMenuOpen
                && demolishHoldEntityId is not null
                && Mouse.IsButtonPressed(Mouse.Button.Right)
                && selectedEntityId is not null)
            {
                var holdCommander = simulation.World.GetEntity(selectedEntityId.Value);
                var mouseTile = TileFromScreen(Mouse.GetPosition(window));
                var hoverTarget = mouseTile is null ? null : simulation.World.GetTopEntityAt(mouseTile.Value);
                if (holdCommander?.Kind != EntityKind.Commander
                    || holdCommander.OwnerId != localPlayer
                    || hoverTarget is null
                    || hoverTarget.Id != demolishHoldEntityId.Value
                    || !simulation.IsDemolishableTarget(holdCommander.Id, hoverTarget.Id))
                {
                    ClearDemolishHold();
                }
                else if (!demolishHoldCommitted)
                {
                    demolishHoldElapsed += frameDt;
                    if (demolishHoldElapsed >= SfmlUiLayout.DemolishHoldSeconds)
                    {
                        simulation.TryQueueCommanderDemolish(holdCommander.Id, demolishHoldEntityId.Value);
                        demolishHoldCommitted = true;
                    }
                }
            }
            else if (!Mouse.IsButtonPressed(Mouse.Button.Right))
            {
                ClearDemolishHold();
            }

            accumulator += frameDt;
            while (accumulator >= fixedDelta)
            {
                simulation.AdvanceTick();
                foreach (var shot in simulation.CombatShotsThisTick)
                {
                    lingeringShots.Add((shot, SfmlUiLayout.CombatShotLingerSeconds));
                }

                accumulator -= fixedDelta;
            }

            for (var i = lingeringShots.Count - 1; i >= 0; i--)
            {
                var remaining = lingeringShots[i].Remaining - frameDt;
                if (remaining <= 0f)
                {
                    lingeringShots.RemoveAt(i);
                }
                else
                {
                    lingeringShots[i] = (lingeringShots[i].Shot, remaining);
                }
            }

            var mousePosition = Mouse.GetPosition(window);
            if (isMiddleDragging)
            {
                camera.PanBy(-(mousePosition.X - lastDragMouse.X), -(mousePosition.Y - lastDragMouse.Y));
                lastDragMouse = mousePosition;
                ClampCamera();
            }
            else
            {
                var pan = SfmlUiLayout.CameraPanSpeed * frameDt;
                var dx = 0f;
                var dy = 0f;
                if (Keyboard.IsKeyPressed(Keyboard.Key.Left)) { dx -= pan; }
                if (Keyboard.IsKeyPressed(Keyboard.Key.Right)) { dx += pan; }
                if (Keyboard.IsKeyPressed(Keyboard.Key.Up)) { dy -= pan; }
                if (Keyboard.IsKeyPressed(Keyboard.Key.Down)) { dy += pan; }
                if (IsInPlayfield(mousePosition))
                {
                    if (mousePosition.X < SfmlUiLayout.EdgeScrollBand) { dx -= pan; }
                    else if (mousePosition.X > playfieldWidth - SfmlUiLayout.EdgeScrollBand) { dx += pan; }
                    if (mousePosition.Y < SfmlUiLayout.TopBarHeight + SfmlUiLayout.EdgeScrollBand) { dy -= pan; }
                    else if (mousePosition.Y > windowHeight - SfmlUiLayout.EdgeScrollBand)
                    {
                        var overBuildBar = isBuildMenuOpen
                            && BuildBarOverlay.GetBuildBarBounds(windowWidth, windowHeight, panelX, out _, out _).Contains(new Vector2f(mousePosition.X, mousePosition.Y));
                        if (!overBuildBar) { dy += pan; }
                    }
                }
                camera.PanBy(dx, dy);
                ClampCamera();
            }

            window.Clear(new Color(18, 22, 18));
            using (var worldView = CreateWorldView())
            {
                window.SetView(worldView);
                var hoverTile = TileFromScreen(mousePosition);
                if (isBuildMenuOpen
                    && BuildBarOverlay.GetBuildBarBounds(windowWidth, windowHeight, panelX, out _, out _).Contains(new Vector2f(mousePosition.X, mousePosition.Y)))
                {
                    hoverTile = null;
                }

                WorldRenderer.DrawWorld(
                    window,
                    simulation,
                    localPlayer,
                    selectedEntityId,
                    isBuildMenuOpen ? pendingBuildKind : null,
                    pendingDirection,
                    hoverTile,
                    patrolWaypoints,
                    lingeringShots.Select(entry => entry.Shot).ToList(),
                    camera.X,
                    camera.Y,
                    playfieldWidth,
                    playfieldHeight);
            }

            window.SetView(window.DefaultView);
            HudOverlay.DrawTopBar(window, simulation, localPlayer, font, playfieldWidth);
            HudOverlay.DrawMinimap(window, simulation, localPlayer, windowWidth, camera.X, camera.Y, playfieldWidth, playfieldHeight);
            HudOverlay.DrawHud(
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
            if (isEnergyOverlayOpen)
            {
                var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                var stats = simulation.GetPlayer(localPlayer).EnergyStats.Query((int)energySelectedInterval);
                var energyPanel = EnergyStatsPanelModel.Build(
                    stats,
                    energySelectedInterval,
                    windowWidth,
                    windowHeight,
                    panelX,
                    bottomReserved);
                EnergyStatsOverlay.DrawEnergyStatsOverlay(window, energyPanel, font, mousePosition);
            }
            else if (isResearchOverlayOpen)
            {
                var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
                var tree = ResearchTreePanelModel.FromSnapshot(
                    simulation.GetResearchSnapshot(localPlayer),
                    overlayBounds,
                    researchSelectedId);
                researchScrollY = ClampResearchScroll(tree);
                ResearchTreeOverlay.DrawResearchTreeOverlay(window, tree, font, researchScrollY, windowWidth, windowHeight);
            }

            if (isBuildMenuOpen)
            {
                BuildBarOverlay.DrawBuildBar(
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
            else if (!isResearchOverlayOpen && !isEnergyOverlayOpen)
            {
                var selectedForOrders = selectedEntityId is null ? null : simulation.World.GetEntity(selectedEntityId.Value);
                if (selectedForOrders?.Kind == EntityKind.Bastion && selectedForOrders.OwnerId == localPlayer)
                {
                    if (isBastionCompositionOpen)
                    {
                        BastionUiOverlay.DrawBastionCompositionPanel(
                            window,
                            simulation,
                            selectedForOrders,
                            templateUnitIndex,
                            font,
                            windowWidth,
                            windowHeight,
                            panelX,
                            mousePosition);
                    }

                    BastionUiOverlay.DrawBastionOrderBar(
                        window,
                        selectedForOrders,
                        bastionPendingMode,
                        font,
                        windowWidth,
                        windowHeight,
                        panelX,
                        mousePosition);
                }
                else
                {
                    CloseBastionComposition();
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
}
