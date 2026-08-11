using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// Owns SFML session state, input bindings, camera updates, and frame presentation.
/// Gameplay mutations go through <see cref="GameSimulation"/> APIs only.
/// R21: UI mode state lives in <see cref="SessionState"/>; intents route through
/// <see cref="InputCommandMapper"/> into <see cref="SfmlCommandGateway"/>.
/// </summary>
internal sealed class SfmlPlaySession
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
        var localPlayer = display.LocalPlayerId;
        // R24: fail fast with a domain-friendly message if the requested seat is not on this map
        // (e.g. --local-player 3 on a 2-player map) instead of a raw First() exception below.
        LocalPlayerBinding.EnsureSeatControllable(simulation, localPlayer);
        // R02: all gameplay mutations flow through the deferred command sink so local input takes the
        // same tick-scheduled, replayable path as remote input.
        var commandSink = new DeferredCommandSink(simulation);
        var commands = new SfmlCommandGateway(commandSink);
        var initialSelectedId = simulation.World.Entities
            .First(entity => entity.OwnerId == localPlayer && entity.Kind == EntityKind.Commander)
            .Id;
        var session = new SessionState(initialSelectedId);
        var inputMapper = new InputCommandMapper(localPlayer, session, commands);
        var researchClickClock = new Clock();
        var font = SfmlFontLoader.TryLoadFont();

        float ClampResearchScroll(ResearchTreePanelModel tree)
        {
            var clamped = ResearchTreePanelModel.ClampScroll(
                session.ResearchScrollY,
                tree.ContentHeight,
                tree.ContentViewport.Height);
            session.SetResearchScrollY(clamped);
            return clamped;
        }

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

        InputModifiers CurrentModifiers() =>
            new(
                Keyboard.IsKeyPressed(Keyboard.Key.LShift) || Keyboard.IsKeyPressed(Keyboard.Key.RShift),
                Keyboard.IsKeyPressed(Keyboard.Key.LControl) || Keyboard.IsKeyPressed(Keyboard.Key.RControl));

        SessionHoverContext BuildHoverContext()
        {
            var tile = TileFromScreen(Mouse.GetPosition(window));
            if (tile is null)
            {
                return new SessionHoverContext(null, null, false);
            }

            var entity = simulation.World.GetTopEntityAt(tile.Value);
            var visible = entity is not null
                && WorldRenderer.IsVisibleToLocalPlayer(simulation, localPlayer, entity);
            return new SessionHoverContext(tile, entity, visible);
        }

        void ApplyCameraRequest(InputMapResult result)
        {
            if (result.Camera == SessionCameraRequest.CenterOnSelected)
            {
                var selected = inputMapper.GetSelectedEntity(simulation);
                if (selected is not null)
                {
                    camera.CenterOnWorldPosition(selected.WorldPosition, playfieldWidth, playfieldHeight);
                    ClampCamera();
                }
            }
            else if (result.Camera == SessionCameraRequest.CenterOnTile && result.CameraTile is not null)
            {
                CenterCameraOnTile(result.CameraTile.Value);
            }
        }

        window.KeyPressed += (_, args) =>
        {
            ApplyCameraRequest(
                inputMapper.HandleKeyPressed(
                    simulation,
                    args.Code.ToString(),
                    CurrentModifiers(),
                    BuildHoverContext()));
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

            if (button == "Left" && session.IsBuildMenuOpen
                && BuildBarOverlay.TryPickBuildBarKind(mousePosition, windowWidth, windowHeight, panelX, out var barKind))
            {
                session.SelectPendingBuildKind(barKind);
                return;
            }

            if (session.IsEnergyOverlayOpen && button == "Left")
            {
                var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                var stats = simulation.GetPlayer(localPlayer).EnergyStats.Query((int)session.EnergySelectedInterval);
                var panel = EnergyStatsPanelModel.Build(
                    stats,
                    session.EnergySelectedInterval,
                    windowWidth,
                    windowHeight,
                    panelX,
                    bottomReserved);
                if (panel.HitExit(mousePosition))
                {
                    session.CloseEnergyOverlay();
                    return;
                }

                if (panel.TryHitInterval(mousePosition, out var interval))
                {
                    session.SetEnergySelectedInterval(interval);
                    return;
                }

                if (panel.ContainsOverlay(mousePosition))
                {
                    return;
                }
            }

            if (session.IsResearchOverlayOpen && button == "Left")
            {
                session.CloseEnergyOverlay();
                var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
                var snapshot = simulation.GetResearchSnapshot(localPlayer);
                var tree = ResearchTreePanelModel.FromSnapshot(snapshot, overlayBounds, session.ResearchSelectedId);
                ClampResearchScroll(tree);

                if (tree.HitExit(mousePosition))
                {
                    session.CloseResearchOverlay();
                    return;
                }

                if (tree.HitAllocation(mousePosition))
                {
                    inputMapper.ToggleResearchAllocation(simulation);
                    return;
                }

                if (tree.HitAction(mousePosition))
                {
                    inputMapper.HandleResearchAction(tree);
                    return;
                }

                if (tree.TryPickNode(mousePosition, session.ResearchScrollY, out var techId))
                {
                    inputMapper.HandleResearchNodeClick(
                        simulation,
                        techId,
                        researchClickClock.ElapsedTime.AsSeconds(),
                        tree);
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
                    commands,
                    localPlayer,
                    session.SelectedEntityId,
                    session.SidebarStorageHits,
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

                ApplyCameraRequest(
                    inputMapper.HandleMinimapClick(
                        simulation,
                        button,
                        minimapTile.Value,
                        CurrentModifiers()));
                return;
            }

            if (IsOverSidePanel(mousePosition))
            {
                return;
            }

            var selectedForBar = inputMapper.GetSelectedEntity(simulation);
            if (button == "Left"
                && !session.IsBuildMenuOpen
                && !session.IsResearchOverlayOpen
                && !session.IsEnergyOverlayOpen
                && selectedForBar?.Kind == EntityKind.Bastion
                && selectedForBar.OwnerId == localPlayer)
            {
                if (session.IsBastionCompositionOpen)
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
                        session.CloseBastionComposition();
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
                        session.SetTemplateUnitIndex(slotIndex);
                        inputMapper.SetBastionTemplateFromComposition(
                            selectedForBar.Id,
                            slot.UnitKind,
                            slot.TemplateMax + (int)adjust);
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
                    inputMapper.ApplyBastionOrderCommand(selectedForBar.Id, barCommand);
                    return;
                }
            }

            var tile = TileFromScreen(mousePosition);
            if (tile is null)
            {
                return;
            }

            ApplyCameraRequest(
                inputMapper.HandleWorldClick(
                    simulation,
                    button,
                    tile.Value,
                    CurrentModifiers(),
                    confirmPatrolOnRightOrMinimap: button == "Right"));
        };
        window.MouseWheelScrolled += (_, args) =>
        {
            if (!session.IsResearchOverlayOpen)
            {
                return;
            }

            var mousePosition = Mouse.GetPosition(window);
            var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
            var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
            var tree = ResearchTreePanelModel.FromSnapshot(
                simulation.GetResearchSnapshot(localPlayer),
                overlayBounds,
                session.ResearchSelectedId);
            if (!tree.ContainsContentViewport(mousePosition) && !tree.ContainsOverlay(mousePosition))
            {
                return;
            }

            session.SetResearchScrollY(
                ResearchTreePanelModel.ClampScroll(
                    session.ResearchScrollY - args.Delta * ResearchTreePanelModel.ScrollStep,
                    tree.ContentHeight,
                    tree.ContentViewport.Height));
        };
        window.MouseButtonReleased += (_, args) =>
        {
            if (args.Button.ToString() == "Middle")
            {
                isMiddleDragging = false;
            }

            if (args.Button.ToString() == "Right")
            {
                session.ClearDemolishHold();
            }
        };

        var clock = new Clock();
        var accumulator = 0f;
        var fixedDelta = 1f / display.TicksPerSecond;
        var lingeringShots = new List<(CombatShotEvent Shot, float Remaining)>();

        var renderedFrames = 0;

        while (window.IsOpen)
        {
            window.DispatchEvents();
            var frameDt = clock.Restart().AsSeconds();

            var mouseTile = TileFromScreen(Mouse.GetPosition(window));
            var hoverTarget = mouseTile is null ? null : simulation.World.GetTopEntityAt(mouseTile.Value);
            inputMapper.TickDemolishHold(
                simulation,
                frameDt,
                Mouse.IsButtonPressed(Mouse.Button.Right),
                hoverTarget?.Id);

            accumulator += frameDt;
            // R23: cap ticks per frame and drop excess backlog (see FixedStepPacer) so a long pause
            // (minimized window, breakpoint) can't trigger a spiral-of-death catch-up. Dropping is
            // acceptable for this local host; a future lockstep network host must stall/resync.
            var (ticksThisFrame, pacedAccumulator) = FixedStepPacer.Plan(accumulator, fixedDelta);
            accumulator = pacedAccumulator;
            for (var tickIndex = 0; tickIndex < ticksThisFrame; tickIndex++)
            {
                simulation.AdvanceTick();
                foreach (var shot in simulation.CombatShotsThisTick)
                {
                    // R27: only buffer tracers the local player can actually see, so hidden combat
                    // cannot be inferred from tracer endpoints.
                    if (WorldRenderer.IsShotVisibleToLocalPlayer(simulation, localPlayer, shot))
                    {
                        lingeringShots.Add((shot, SfmlUiLayout.CombatShotLingerSeconds));
                    }
                }

                // R27: re-validate the current selection every tick. If a non-owned entity has left
                // the local player's vision (or was removed), drop the selection so the HUD stops
                // leaking its live HP/position/orders.
                inputMapper.RefreshSelectionVisibility(simulation);
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
                        var overBuildBar = session.IsBuildMenuOpen
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
                if (session.IsBuildMenuOpen
                    && BuildBarOverlay.GetBuildBarBounds(windowWidth, windowHeight, panelX, out _, out _).Contains(new Vector2f(mousePosition.X, mousePosition.Y)))
                {
                    hoverTile = null;
                }

                WorldRenderer.DrawWorld(
                    window,
                    simulation,
                    localPlayer,
                    session.SelectedEntityId,
                    session.IsBuildMenuOpen ? session.PendingBuildKind : null,
                    session.PendingDirection,
                    hoverTile,
                    session.PatrolWaypoints,
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
                session.SelectedEntityId,
                session.IsBuildMenuOpen,
                session.PendingBuildKind,
                session.PendingDirection,
                session.PendingRecipe,
                session.RecipePage,
                session.TemplateUnitIndex,
                session.BastionPendingMode,
                session.PatrolWaypoints.Count,
                font,
                windowWidth,
                windowHeight,
                panelX,
                panelTop,
                session.IsResearchOverlayOpen,
                session.ResearchSelectedId,
                session.SidebarStorageHits);
            if (session.IsEnergyOverlayOpen)
            {
                var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                var stats = simulation.GetPlayer(localPlayer).EnergyStats.Query((int)session.EnergySelectedInterval);
                var energyPanel = EnergyStatsPanelModel.Build(
                    stats,
                    session.EnergySelectedInterval,
                    windowWidth,
                    windowHeight,
                    panelX,
                    bottomReserved);
                EnergyStatsOverlay.DrawEnergyStatsOverlay(window, energyPanel, font, mousePosition);
            }
            else if (session.IsResearchOverlayOpen)
            {
                var bottomReserved = SfmlUiLayout.BuildBarSlotSize + SfmlUiLayout.BuildBarBottomMargin + 8f;
                var overlayBounds = ResearchTreePanelModel.ComputeOverlayBounds(windowWidth, windowHeight, panelX, bottomReserved);
                var tree = ResearchTreePanelModel.FromSnapshot(
                    simulation.GetResearchSnapshot(localPlayer),
                    overlayBounds,
                    session.ResearchSelectedId);
                ClampResearchScroll(tree);
                ResearchTreeOverlay.DrawResearchTreeOverlay(window, tree, font, session.ResearchScrollY, windowWidth, windowHeight);
            }

            if (session.IsBuildMenuOpen)
            {
                BuildBarOverlay.DrawBuildBar(
                    window,
                    simulation,
                    localPlayer,
                    session.SelectedEntityId,
                    session.PendingBuildKind,
                    session.PendingDirection,
                    session.PendingRecipe,
                    font,
                    windowWidth,
                    windowHeight,
                    panelX,
                    mousePosition);
            }
            else if (!session.IsResearchOverlayOpen && !session.IsEnergyOverlayOpen)
            {
                var selectedForOrders = inputMapper.GetSelectedEntity(simulation);
                if (selectedForOrders?.Kind == EntityKind.Bastion && selectedForOrders.OwnerId == localPlayer)
                {
                    if (session.IsBastionCompositionOpen)
                    {
                        BastionUiOverlay.DrawBastionCompositionPanel(
                            window,
                            simulation,
                            selectedForOrders,
                            session.TemplateUnitIndex,
                            font,
                            windowWidth,
                            windowHeight,
                            panelX,
                            mousePosition);
                    }

                    BastionUiOverlay.DrawBastionOrderBar(
                        window,
                        selectedForOrders,
                        session.BastionPendingMode,
                        font,
                        windowWidth,
                        windowHeight,
                        panelX,
                        mousePosition);
                }
                else
                {
                    session.CloseBastionComposition();
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
