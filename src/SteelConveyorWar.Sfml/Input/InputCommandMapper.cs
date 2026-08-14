using SteelConveyorWar.Core;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// Modifier keys resolved by the host before mapping (avoids SFML keyboard reads inside the mapper).
/// </summary>
internal readonly record struct InputModifiers(bool Shift, bool Control);

/// <summary>
/// Host-resolved world hover under the cursor. Keeps <see cref="InputCommandMapper"/> free of
/// window / MapPixelToCoords side effects.
/// </summary>
internal readonly record struct SessionHoverContext(
    TilePosition? Tile,
    WorldEntity? Entity,
    bool EntityVisibleToLocalPlayer);

/// <summary>
/// Optional camera side-effect requested by mapping (session applies it to <see cref="GameCamera"/>).
/// </summary>
internal enum SessionCameraRequest
{
    None,
    CenterOnSelected,
    CenterOnTile
}

/// <summary>
/// Result of mapping an input intent. <see cref="Consumed"/> means the session should stop further
/// handling for that event.
/// </summary>
internal readonly record struct InputMapResult(
    bool Consumed,
    SessionCameraRequest Camera = SessionCameraRequest.None,
    TilePosition? CameraTile = null)
{
    public static InputMapResult Ignored { get; } = new(false);
    public static InputMapResult Handled { get; } = new(true);
    public static InputMapResult CenterSelected { get; } = new(true, SessionCameraRequest.CenterOnSelected);

    public static InputMapResult CenterTile(TilePosition tile) =>
        new(true, SessionCameraRequest.CenterOnTile, tile);
}

/// <summary>
/// R21/M08: maps input intents + <see cref="SessionState"/> into gateway enqueues / state transitions.
/// No SFML window APIs — hover tiles, modifiers, and overlay hit outcomes are injected by the host.
/// Pure <c>Map*</c> helpers build command DTOs (tick placeholder 0) for tests and gateway enqueue.
/// Still consults live simulation for selection/hover validation; full snapshot→intent purity is partial.
/// </summary>
internal sealed class InputCommandMapper
{
    private readonly PlayerId _localPlayer;
    private readonly SessionState _state;
    private readonly SfmlCommandGateway _commands;
    private readonly IReadOnlyList<EntityKind> _buildMenuKinds;

    /// <param name="buildMenuKinds">
    /// H05: match-scoped menu from <see cref="BuildMenuCatalog.ComposeFrom"/> —
    /// not <c>MvpBuildCostCatalog.Embedded</c>.
    /// </param>
    internal InputCommandMapper(
        PlayerId localPlayer,
        SessionState state,
        SfmlCommandGateway commands,
        IReadOnlyList<EntityKind> buildMenuKinds)
    {
        ArgumentNullException.ThrowIfNull(buildMenuKinds);
        _localPlayer = localPlayer;
        _state = state;
        _commands = commands;
        _buildMenuKinds = buildMenuKinds;
    }

    internal IReadOnlyList<EntityKind> BuildMenuKinds => _buildMenuKinds;

    internal static IssueMoveCommand MapMoveIntent(PlayerId actor, int entityId, TilePosition targetTile)
        => new(actor, 0, entityId, targetTile);

    internal static StopCommanderCommand MapStopCommander(PlayerId actor, int commanderId)
        => new(actor, 0, commanderId);

    internal static RotateEntityCommand MapRotate(PlayerId actor, int entityId, bool clockwise)
        => new(actor, 0, entityId, clockwise);

    internal static SelectResearchCommand MapSelectResearch(
        PlayerId actor,
        TechnologyId technology,
        bool confirmExclusive,
        string? preferredTrackId)
        => new(actor, 0, technology, confirmExclusive, preferredTrackId);

    internal static QueueCommanderDemolishCommand MapDemolishIntent(PlayerId actor, int commanderId, int targetEntityId)
        => new(actor, 0, commanderId, targetEntityId);

    internal SessionState State => _state;
    internal SfmlCommandGateway Commands => _commands;
    internal PlayerId LocalPlayer => _localPlayer;

    internal WorldEntity? GetSelectedEntity(GameSimulation simulation) =>
        _state.SelectedEntityId is null ? null : simulation.World.GetEntity(_state.SelectedEntityId.Value);

    internal InputMapResult HandleKeyPressed(
        GameSimulation simulation,
        string key,
        InputModifiers modifiers,
        SessionHoverContext hover)
    {
        var selectedEntity = GetSelectedEntity(simulation);

        if (key == "Escape" && _state.BastionPendingMode != BastionPendingInputMode.None)
        {
            _state.ClearBastionPending();
            return InputMapResult.Handled;
        }

        if (key == "Escape" && _state.IsEnergyOverlayOpen)
        {
            _state.CloseEnergyOverlay();
            return InputMapResult.Handled;
        }

        if (key == "Escape" && _state.IsResearchOverlayOpen)
        {
            _state.CloseResearchOverlay();
            return InputMapResult.Handled;
        }

        if (key == "Escape" && _state.IsBastionCompositionOpen)
        {
            _state.CloseBastionComposition();
            return InputMapResult.Handled;
        }

        if (key is "Enter" or "Return"
            && _state.BastionPendingMode == BastionPendingInputMode.PatrolWaypoints
            && selectedEntity?.Kind == EntityKind.Bastion
            && selectedEntity.OwnerId == _localPlayer
            && _state.PatrolWaypoints.Count is >= 2 and <= 4)
        {
            _commands.IssueBastionOrder(
                selectedEntity.Id,
                _localPlayer,
                new BastionOrder(BastionOrderKind.Patrol, Waypoints: _state.PatrolWaypoints.ToArray()));
            _state.ClearBastionPending();
            return InputMapResult.Handled;
        }

        if (key == "F1")
        {
            var selectedId = _state.SelectedEntityId;
            if (!SfmlInputHelpers.EnsureLocalCommanderSelected(simulation, _localPlayer, ref selectedId))
            {
                return InputMapResult.Handled;
            }

            _state.SetSelectedEntityId(selectedId);
            _state.ClearTransientUiKeepingSelection();
            return InputMapResult.CenterSelected;
        }

        if (key == "Q")
        {
            if (hover.Tile is not null
                && hover.Entity is not null
                && hover.EntityVisibleToLocalPlayer
                && BuildBarModel.TryCopyFromWorldEntity(
                    hover.Entity,
                    simulation.BuildCostCatalog,
                    out var copyKind,
                    out var copyDirection,
                    out var copyRecipe,
                    out var copyLongReach))
            {
                var selectedId = _state.SelectedEntityId;
                if (!SfmlInputHelpers.EnsureLocalCommanderSelected(simulation, _localPlayer, ref selectedId))
                {
                    return InputMapResult.Handled;
                }

                _state.SetSelectedEntityId(selectedId);
                _state.OpenBuildMenuWithCopy(copyKind, copyDirection, copyRecipe, copyLongReach);
            }

            return InputMapResult.Handled;
        }

        if (key == "F")
        {
            WorldEntity? inserter = null;
            if (hover.Entity is not null
                && hover.EntityVisibleToLocalPlayer
                && hover.Entity.OwnerId == _localPlayer
                && MvpDefinitions.IsInserterOrGhost(hover.Entity))
            {
                inserter = hover.Entity;
            }
            else if (selectedEntity is not null
                     && selectedEntity.OwnerId == _localPlayer
                     && MvpDefinitions.IsInserterOrGhost(selectedEntity))
            {
                inserter = selectedEntity;
            }

            if (inserter is not null)
            {
                _commands.SetInserterReach(inserter.Id, _localPlayer, !inserter.InserterLongReach);
                return InputMapResult.Handled;
            }

            if (_state.IsBuildMenuOpen && _state.PendingBuildKind == EntityKind.Inserter)
            {
                _state.TogglePendingInserterLongReach();
                return InputMapResult.Handled;
            }
        }

        if (key == "B" && selectedEntity?.Kind == EntityKind.Commander && selectedEntity.OwnerId == _localPlayer)
        {
            if (_buildMenuKinds.Count > 0)
            {
                _state.ToggleBuildMenu(_buildMenuKinds[0]);
            }

            return InputMapResult.Handled;
        }

        if (key == "S"
            && selectedEntity?.Kind == EntityKind.Commander
            && selectedEntity.OwnerId == _localPlayer)
        {
            _commands.StopCommander(selectedEntity.Id, _localPlayer);
            _state.ClearDemolishHold();
            return InputMapResult.Handled;
        }

        if (key == "R")
        {
            var clockwise = !modifiers.Shift;
            if (_state.IsBuildMenuOpen && _state.PendingBuildKind is not null && BuildBarModel.IsDirectedKind(_state.PendingBuildKind.Value))
            {
                _state.SetPendingDirection(SfmlInputHelpers.RotateDirection(_state.PendingDirection, clockwise));
                return InputMapResult.Handled;
            }

            if (hover.Tile is not null && hover.Entity is not null
                && hover.Entity.OwnerId == _localPlayer
                && BuildBarModel.IsDirectedKind(hover.Entity.Kind))
            {
                _commands.RotateEntity(hover.Entity.Id, _localPlayer, clockwise);
                return InputMapResult.Handled;
            }

            if (selectedEntity is not null && selectedEntity.OwnerId == _localPlayer)
            {
                _commands.RotateEntity(selectedEntity.Id, _localPlayer, clockwise);
            }

            return InputMapResult.Handled;
        }

        if (!_state.IsBuildMenuOpen
            && _state.IsBastionCompositionOpen
            && selectedEntity?.Kind == EntityKind.Bastion
            && selectedEntity.OwnerId == _localPlayer
            && key is "PageDown" or "RBracket" or "PageUp" or "LBracket")
        {
            var unlocked = BastionCompositionPanelModel.UnlockedUnitKinds(simulation, _localPlayer);
            var delta = key is "PageDown" or "RBracket" ? 1 : -1;
            _state.CycleTemplateUnitIndex(delta, unlocked.Length);
            return InputMapResult.Handled;
        }

        if (key is "PageDown" or "RBracket")
        {
            _state.IncrementRecipePage();
            return InputMapResult.Handled;
        }

        if (key is "PageUp" or "LBracket")
        {
            _state.DecrementRecipePage();
            return InputMapResult.Handled;
        }

        if (key == "T")
        {
            _state.PrepareOpenResearchExclusive();
            _state.ToggleResearchOverlay();
            return InputMapResult.Handled;
        }

        if (key == "P")
        {
            _state.PrepareOpenEnergyExclusive();
            _state.ToggleEnergyOverlay();
            return InputMapResult.Handled;
        }

        if (key == "E"
            && selectedEntity?.Kind == EntityKind.Bastion
            && selectedEntity.OwnerId == _localPlayer)
        {
            _state.PrepareToggleBastionCompositionExclusive();
            _state.ToggleBastionComposition();
            return InputMapResult.Handled;
        }

        if (!_state.IsBuildMenuOpen && selectedEntity?.Kind == EntityKind.Assembler && SfmlInputHelpers.TryGetRecipeShortcut(key, out var recipeId))
        {
            _commands.SetAssemblerRecipe(selectedEntity.Id, _localPlayer, recipeId);
            _state.SetRecipePage(0);
            return InputMapResult.Handled;
        }

        if (!_state.IsResearchOverlayOpen && !_state.IsEnergyOverlayOpen && !_state.IsBuildMenuOpen && selectedEntity?.Kind == EntityKind.Laboratory && SfmlInputHelpers.TryGetNumberShortcut(key, out var researchIndex))
        {
            // R26: research can only be steered on a laboratory the local player owns.
            if (selectedEntity.OwnerId != _localPlayer)
            {
                return InputMapResult.Handled;
            }

            var panel = ResearchPanelModel.FromSnapshot(simulation.GetResearchSnapshot(_localPlayer), _state.RecipePage);
            if (researchIndex < panel.PageEntries.Count)
            {
                var entry = panel.PageEntries[researchIndex];
                _commands.SelectResearch(
                    _localPlayer,
                    entry.Id,
                    entry.RequiresExclusiveConfirmation,
                    entry.TrackId);
            }

            return InputMapResult.Handled;
        }

        if (!_state.IsBuildMenuOpen
            && selectedEntity is not null
            && MvpDefinitions.FactoryKinds.Contains(selectedEntity.Kind)
            && selectedEntity.OwnerId == _localPlayer)
        {
            if (SfmlInputHelpers.TryGetNumberShortcut(key, out var factoryRecipeIndex))
            {
                var recipes = HudOverlay.GetFactoryRecipes(selectedEntity.Kind, simulation.GameplayTables).ToList();
                if (factoryRecipeIndex < recipes.Count)
                {
                    _commands.SetFactoryProduction(selectedEntity.Id, _localPlayer, recipes[factoryRecipeIndex].OutputKind);
                }

                return InputMapResult.Handled;
            }
        }

        if (!_state.IsBuildMenuOpen
            && selectedEntity?.Kind == EntityKind.Bastion
            && selectedEntity.OwnerId == _localPlayer)
        {
            if (BastionOrderBarModel.TryGetCommandFromKey(key, out var orderCommand))
            {
                ApplyBastionOrderCommand(selectedEntity.Id, orderCommand);
                return InputMapResult.Handled;
            }

            var selectedId = _state.SelectedEntityId;
            if (SfmlInputHelpers.TryGetNumberShortcut(key, out var bastionIndex)
                && SfmlInputHelpers.TrySelectOwnedBastionByIndex(simulation, _localPlayer, bastionIndex, ref selectedId))
            {
                _state.SetSelectedEntityId(selectedId);
                _state.OnBastionIndexSelected();
                return InputMapResult.Handled;
            }

            if (BastionUiOverlay.TryAdjustBastionTemplate(key, simulation, _commands, selectedEntity, _localPlayer, _state.TemplateUnitIndex))
            {
                return InputMapResult.Handled;
            }
        }

        if (!_state.IsBuildMenuOpen)
        {
            return InputMapResult.Ignored;
        }

        if (SfmlInputHelpers.TryGetNumberShortcut(key, out var buildIndex) && buildIndex < _buildMenuKinds.Count)
        {
            _state.SelectPendingBuildKind(_buildMenuKinds[buildIndex]);
        }

        return InputMapResult.Handled;
    }

    internal void ApplyBastionOrderCommand(int bastionId, BastionOrderCommand command)
    {
        switch (command)
        {
            case BastionOrderCommand.ActiveDefense:
                _state.ClearBastionPending();
                _commands.IssueBastionOrder(bastionId, _localPlayer, new BastionOrder(BastionOrderKind.Defend));
                break;
            case BastionOrderCommand.Patrol:
                _state.SetBastionPendingMode(BastionPendingInputMode.PatrolWaypoints);
                break;
            case BastionOrderCommand.Attack:
                _state.SetBastionPendingMode(BastionPendingInputMode.AttackTarget);
                break;
            case BastionOrderCommand.Scout:
                _state.SetBastionPendingMode(BastionPendingInputMode.ScoutTarget);
                break;
        }
    }

    /// <summary>Minimap or playfield world click that resolves to a tile (UI overlays already filtered by host).</summary>
    internal InputMapResult HandleWorldClick(
        GameSimulation simulation,
        string button,
        TilePosition tile,
        InputModifiers modifiers,
        bool confirmPatrolOnRightOrMinimap)
    {
        var selectedEntity = GetSelectedEntity(simulation);

        if (button == "Left")
        {
            if (BastionUiOverlay.TryHandleBastionPendingMapClick(
                    _commands,
                    selectedEntity,
                    _localPlayer,
                    _state.BastionPendingMode,
                    _state.PatrolWaypoints,
                    tile,
                    confirmPatrol: false,
                    out var consumedLeft)
                && consumedLeft)
            {
                if (_state.BastionPendingMode is BastionPendingInputMode.AttackTarget or BastionPendingInputMode.ScoutTarget)
                {
                    _state.ClearBastionPending();
                }

                return InputMapResult.Handled;
            }

            var clickedEntity = simulation.World.GetTopEntityAt(tile);
            if (modifiers.Control
                && selectedEntity?.Kind == EntityKind.Commander
                && selectedEntity.OwnerId == _localPlayer
                && clickedEntity is not null)
            {
                _commands.WithdrawFromHubOrOutput(selectedEntity.Id, _localPlayer, clickedEntity.Id);
                return InputMapResult.Handled;
            }

            if (_state.IsBuildMenuOpen && _state.PendingBuildKind is not null && selectedEntity?.Kind == EntityKind.Commander)
            {
                _commands.QueueCommanderBuild(
                    selectedEntity.Id,
                    _localPlayer,
                    _state.PendingBuildKind.Value,
                    tile,
                    _state.PendingDirection,
                    _state.PendingRecipe,
                    _state.PendingBuildKind == EntityKind.Inserter && _state.PendingInserterLongReach);
                return InputMapResult.Handled;
            }

            var newSelection = clickedEntity is not null && WorldRenderer.IsVisibleToLocalPlayer(simulation, _localPlayer, clickedEntity)
                ? clickedEntity.Id
                : (int?)null;
            _state.ApplyPlayfieldSelection(newSelection);
            return InputMapResult.Handled;
        }

        if (button == "Right")
        {
            if (selectedEntity?.Kind == EntityKind.Commander && selectedEntity.OwnerId == _localPlayer)
            {
                if (_state.IsBuildMenuOpen)
                {
                    var clickedEntity = simulation.World.GetTopEntityAt(tile);
                    if (clickedEntity is not null
                        && simulation.IsDemolishableTarget(selectedEntity.Id, clickedEntity.Id))
                    {
                        _state.BeginDemolishHold(clickedEntity.Id);
                    }
                    else
                    {
                        _state.ClearDemolishHold();
                    }

                    return InputMapResult.Handled;
                }

                var clickedEntityMove = simulation.World.GetTopEntityAt(tile);
                if (modifiers.Control && clickedEntityMove is not null)
                {
                    _commands.DepositToHubOrInput(selectedEntity.Id, _localPlayer, clickedEntityMove.Id);
                    return InputMapResult.Handled;
                }

                _commands.IssueMove(selectedEntity.Id, _localPlayer, tile);
                return InputMapResult.Handled;
            }

            if (BastionUiOverlay.TryHandleBastionPendingMapClick(
                    _commands,
                    selectedEntity,
                    _localPlayer,
                    _state.BastionPendingMode,
                    _state.PatrolWaypoints,
                    tile,
                    confirmPatrol: confirmPatrolOnRightOrMinimap,
                    out var consumedRight)
                && consumedRight)
            {
                _state.ClearBastionPending();
            }

            return InputMapResult.Handled;
        }

        return InputMapResult.Ignored;
    }

    internal InputMapResult HandleMinimapClick(
        GameSimulation simulation,
        string button,
        TilePosition tile,
        InputModifiers modifiers)
    {
        if (button == "Left")
        {
            return InputMapResult.CenterTile(tile);
        }

        if (button == "Right")
        {
            var selectedEntity = GetSelectedEntity(simulation);
            if (selectedEntity?.Kind == EntityKind.Commander && selectedEntity.OwnerId == _localPlayer)
            {
                var clickedEntity = simulation.World.GetTopEntityAt(tile);
                if (modifiers.Control && clickedEntity is not null)
                {
                    _commands.DepositToHubOrInput(selectedEntity.Id, _localPlayer, clickedEntity.Id);
                    return InputMapResult.Handled;
                }

                _commands.IssueMove(selectedEntity.Id, _localPlayer, tile);
                return InputMapResult.Handled;
            }

            if (BastionUiOverlay.TryHandleBastionPendingMapClick(
                    _commands,
                    selectedEntity,
                    _localPlayer,
                    _state.BastionPendingMode,
                    _state.PatrolWaypoints,
                    tile,
                    confirmPatrol: true,
                    out var consumedRight)
                && consumedRight)
            {
                _state.ClearBastionPending();
            }

            return InputMapResult.Handled;
        }

        return InputMapResult.Ignored;
    }

    internal void HandleResearchNodeClick(
        GameSimulation simulation,
        TechnologyId techId,
        float nowSeconds,
        ResearchTreePanelModel tree)
    {
        var isDouble = _state.IsResearchDoubleClick(techId, nowSeconds);
        _state.RecordResearchClick(techId, nowSeconds);
        if (isDouble)
        {
            var node = tree.Nodes.First(n => n.Id == techId);
            _commands.SelectResearch(
                _localPlayer,
                techId,
                node.RequiresExclusiveConfirmation,
                node.TrackId);
        }
    }

    internal void HandleResearchAction(ResearchTreePanelModel tree)
    {
        if (_state.ResearchSelectedId is null)
        {
            return;
        }

        if (tree.CanCancelSelected)
        {
            _commands.CancelResearch(_localPlayer, _state.ResearchSelectedId.Value);
        }
        else if (tree.CanStartSelected)
        {
            var node = tree.SelectedNode;
            _commands.SelectResearch(
                _localPlayer,
                _state.ResearchSelectedId.Value,
                node?.RequiresExclusiveConfirmation == true,
                node?.TrackId);
        }
    }

    internal void ToggleResearchAllocation(GameSimulation simulation) =>
        SfmlInputHelpers.ToggleResearchAllocation(simulation, _commands, _localPlayer);

    internal void SetBastionTemplateFromComposition(int bastionId, EntityKind unitKind, int templateMax) =>
        _commands.SetBastionTemplate(bastionId, _localPlayer, unitKind, Math.Max(0, templateMax));

    /// <summary>
    /// Per-frame demolish-hold progress. Host supplies whether RMB is still held and the current hover target id.
    /// Returns true when a demolish command was enqueued this frame.
    /// </summary>
    internal bool TickDemolishHold(
        GameSimulation simulation,
        float frameDt,
        bool rightButtonHeld,
        int? hoverTargetEntityId)
    {
        if (_state.IsBuildMenuOpen
            && _state.DemolishHoldEntityId is not null
            && rightButtonHeld
            && _state.SelectedEntityId is not null)
        {
            var holdCommander = simulation.World.GetEntity(_state.SelectedEntityId.Value);
            if (holdCommander?.Kind != EntityKind.Commander
                || holdCommander.OwnerId != _localPlayer
                || hoverTargetEntityId is null
                || hoverTargetEntityId.Value != _state.DemolishHoldEntityId.Value
                || !simulation.IsDemolishableTarget(holdCommander.Id, hoverTargetEntityId.Value))
            {
                _state.ClearDemolishHold();
                return false;
            }

            if (_state.DemolishHoldCommitted)
            {
                return false;
            }

            _state.AdvanceDemolishHold(frameDt);
            if (_state.DemolishHoldElapsed >= SfmlUiLayout.DemolishHoldSeconds)
            {
                _commands.QueueCommanderDemolish(holdCommander.Id, _localPlayer, _state.DemolishHoldEntityId.Value);
                _state.MarkDemolishHoldCommitted();
                return true;
            }

            return false;
        }

        if (!rightButtonHeld)
        {
            _state.ClearDemolishHold();
        }

        return false;
    }

    /// <summary>R27: drop selection when the entity leaves vision or is removed.</summary>
    internal void RefreshSelectionVisibility(GameSimulation simulation)
    {
        if (_state.SelectedEntityId is null)
        {
            return;
        }

        var stillSelectable = simulation.World.GetEntity(_state.SelectedEntityId.Value);
        if (stillSelectable is null
            || !WorldRenderer.IsVisibleToLocalPlayer(simulation, _localPlayer, stillSelectable))
        {
            _state.SetSelectedEntityId(null);
        }
    }
}
