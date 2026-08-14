using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// M08: composes world + HUD overlays for one frame. Owns no window lifetime.
/// </summary>
internal static class PresentationComposer
{
    public static void DrawFrame(
        RenderWindow window,
        GameSimulation simulation,
        PlayerId localPlayer,
        SessionState session,
        InputCommandMapper inputMapper,
        IReadOnlyList<EntityKind> buildMenu,
        Font? font,
        GameCamera camera,
        float playfieldWidth,
        float playfieldHeight,
        float panelX,
        float panelTop,
        uint windowWidth,
        uint windowHeight,
        Vector2i mousePosition,
        IReadOnlyList<(CombatShotEvent Shot, CombatShotRevealMode Reveal)> combatShots,
        IReadOnlyList<TilePosition> frameFogDirtyTiles,
        Func<View> createWorldView)
    {
        window.Clear(new Color(18, 22, 18));
        using (var worldView = createWorldView())
        {
            window.SetView(worldView);
            TilePosition? hoverTile = null;
            var world = window.MapPixelToCoords(mousePosition, worldView);
            var candidate = new TilePosition((int)(world.X / SfmlUiLayout.TileSize), (int)(world.Y / SfmlUiLayout.TileSize));
            if (simulation.World.IsInside(candidate)
                && mousePosition.X >= 0
                && mousePosition.X < playfieldWidth
                && mousePosition.Y >= SfmlUiLayout.TopBarHeight
                && mousePosition.Y < windowHeight)
            {
                hoverTile = candidate;
            }

            if (session.IsBuildMenuOpen
                && BuildBarOverlay.GetBuildBarBounds(windowWidth, windowHeight, panelX, buildMenu, out _, out _)
                    .Contains(new Vector2f(mousePosition.X, mousePosition.Y)))
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
                session.PendingInserterLongReach,
                hoverTile,
                session.PatrolWaypoints,
                combatShots,
                camera.X,
                camera.Y,
                playfieldWidth,
                playfieldHeight);
        }

        window.SetView(window.DefaultView);
        HudOverlay.DrawTopBar(window, simulation, localPlayer, font, playfieldWidth);
        HudOverlay.DrawMinimap(
            window,
            simulation,
            localPlayer,
            windowWidth,
            camera.X,
            camera.Y,
            playfieldWidth,
            playfieldHeight,
            frameFogDirtyTiles);
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
            var clamped = ResearchTreePanelModel.ClampScroll(
                session.ResearchScrollY,
                tree.ContentHeight,
                tree.ContentViewport.Height);
            session.SetResearchScrollY(clamped);
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
                mousePosition,
                buildMenu);
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
    }
}
