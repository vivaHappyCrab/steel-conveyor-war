using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

/// <summary>
/// L03: mutually exclusive SFML UI modes. Patrol waypoint entry stays orthogonal to these overlays.
/// </summary>
internal enum ExclusiveUiMode
{
    None = 0,
    Build = 1,
    Research = 2,
    Energy = 3,
    BastionCompose = 4
}

/// <summary>
/// R21: explicit SFML play-session UI state (selection, build menu, overlays, demolish hold,
/// bastion pending input). Transitions live here so <see cref="SfmlPlaySession"/> stays a thin
/// pump → map → sink → advance → render loop and unit tests can exercise modes without a window.
/// </summary>
internal sealed class SessionState
{
    public int? SelectedEntityId { get; private set; }
    public bool IsBuildMenuOpen { get; private set; }
    public EntityKind? PendingBuildKind { get; private set; }
    public Direction PendingDirection { get; private set; } = Direction.East;
    public ItemRecipeId? PendingRecipe { get; private set; }
    public bool PendingInserterLongReach { get; private set; }
    public int RecipePage { get; private set; }
    public int TemplateUnitIndex { get; private set; }
    public BastionPendingInputMode BastionPendingMode { get; private set; } = BastionPendingInputMode.None;
    public List<TilePosition> PatrolWaypoints { get; } = new();
    public List<SidebarStorageHit> SidebarStorageHits { get; } = new();
    public bool IsResearchOverlayOpen { get; private set; }
    public bool IsEnergyOverlayOpen { get; private set; }
    public bool IsBastionCompositionOpen { get; private set; }
    public EnergyStatsWindowKind EnergySelectedInterval { get; private set; } = EnergyStatsWindowKind.Seconds30;
    public TechnologyId? ResearchSelectedId { get; private set; }
    public TechnologyId? ResearchLastClickId { get; private set; }
    public float ResearchLastClickSeconds { get; private set; } = -1f;
    public float ResearchScrollY { get; private set; }
    public int? DemolishHoldEntityId { get; private set; }
    public float DemolishHoldElapsed { get; private set; }
    public bool DemolishHoldCommitted { get; private set; }

    /// <summary>At most one exclusive overlay/mode is active (L03).</summary>
    public ExclusiveUiMode ActiveExclusiveMode
    {
        get
        {
            if (IsBuildMenuOpen)
            {
                return ExclusiveUiMode.Build;
            }

            if (IsResearchOverlayOpen)
            {
                return ExclusiveUiMode.Research;
            }

            if (IsEnergyOverlayOpen)
            {
                return ExclusiveUiMode.Energy;
            }

            if (IsBastionCompositionOpen)
            {
                return ExclusiveUiMode.BastionCompose;
            }

            return ExclusiveUiMode.None;
        }
    }

    public SessionState(int? initialSelectedEntityId = null)
    {
        SelectedEntityId = initialSelectedEntityId;
    }

    public void SetSelectedEntityId(int? entityId) => SelectedEntityId = entityId;

    public void ClearDemolishHold()
    {
        DemolishHoldEntityId = null;
        DemolishHoldElapsed = 0f;
        DemolishHoldCommitted = false;
    }

    public void BeginDemolishHold(int targetEntityId)
    {
        DemolishHoldEntityId = targetEntityId;
        DemolishHoldElapsed = 0f;
        DemolishHoldCommitted = false;
    }

    public void AdvanceDemolishHold(float frameDt) => DemolishHoldElapsed += frameDt;

    public void MarkDemolishHoldCommitted() => DemolishHoldCommitted = true;

    public void CloseBuildMenu()
    {
        IsBuildMenuOpen = false;
        PendingBuildKind = null;
        PendingDirection = Direction.East;
        PendingRecipe = null;
        PendingInserterLongReach = false;
    }

    public void CloseResearchOverlay()
    {
        IsResearchOverlayOpen = false;
        ResearchSelectedId = null;
        ResearchLastClickId = null;
        ResearchScrollY = 0f;
    }

    public void OpenResearchOverlay()
    {
        IsResearchOverlayOpen = true;
        ResearchScrollY = 0f;
    }

    public void ToggleResearchOverlay()
    {
        if (IsResearchOverlayOpen)
        {
            CloseResearchOverlay();
        }
        else
        {
            OpenResearchOverlay();
        }
    }

    public void CloseEnergyOverlay() => IsEnergyOverlayOpen = false;

    public void OpenEnergyOverlay() => IsEnergyOverlayOpen = true;

    public void ToggleEnergyOverlay()
    {
        if (IsEnergyOverlayOpen)
        {
            CloseEnergyOverlay();
        }
        else
        {
            OpenEnergyOverlay();
        }
    }

    public void SetEnergySelectedInterval(EnergyStatsWindowKind interval) => EnergySelectedInterval = interval;

    public void CloseBastionComposition() => IsBastionCompositionOpen = false;

    public void ToggleBastionComposition() => IsBastionCompositionOpen = !IsBastionCompositionOpen;

    public void ClearBastionPending()
    {
        BastionPendingMode = BastionPendingInputMode.None;
        PatrolWaypoints.Clear();
    }

    public void SetBastionPendingMode(BastionPendingInputMode mode)
    {
        PatrolWaypoints.Clear();
        BastionPendingMode = mode;
    }

    public void SetResearchSelectedId(TechnologyId? id) => ResearchSelectedId = id;

    public void RecordResearchClick(TechnologyId techId, float nowSeconds)
    {
        ResearchSelectedId = techId;
        ResearchLastClickId = techId;
        ResearchLastClickSeconds = nowSeconds;
    }

    public bool IsResearchDoubleClick(TechnologyId techId, float nowSeconds) =>
        ResearchLastClickId == techId
        && nowSeconds - ResearchLastClickSeconds <= SfmlUiLayout.ResearchDoubleClickSeconds;

    public void SetResearchScrollY(float scrollY) => ResearchScrollY = scrollY;

    public void SetRecipePage(int page) => RecipePage = Math.Max(0, page);

    public void IncrementRecipePage() => RecipePage++;

    public void DecrementRecipePage() => RecipePage = Math.Max(0, RecipePage - 1);

    public void SetTemplateUnitIndex(int index) => TemplateUnitIndex = index;

    public void CycleTemplateUnitIndex(int delta, int unlockedCount)
    {
        var count = Math.Max(1, unlockedCount);
        TemplateUnitIndex = (TemplateUnitIndex + delta + count) % count;
    }

    public void SetPendingRecipe(ItemRecipeId? recipe) => PendingRecipe = recipe;

    public void SetPendingDirection(Direction direction) => PendingDirection = direction;

    public void SetPendingInserterLongReach(bool longReach) => PendingInserterLongReach = longReach;

    public void TogglePendingInserterLongReach() => PendingInserterLongReach = !PendingInserterLongReach;

    /// <summary>F1 / focus-commander: keep selection, clear build/demolish/bastion UI.</summary>
    public void ClearTransientUiKeepingSelection()
    {
        CloseBuildMenu();
        ClearDemolishHold();
        CloseBastionComposition();
        ClearBastionPending();
    }

    public void ToggleBuildMenu(EntityKind defaultBuildKind)
    {
        if (!IsBuildMenuOpen)
        {
            CloseExclusiveModesExcept(ExclusiveUiMode.Build);
        }

        IsBuildMenuOpen = !IsBuildMenuOpen;
        PendingBuildKind = IsBuildMenuOpen ? defaultBuildKind : null;
        PendingDirection = Direction.East;
        PendingRecipe = null;
        PendingInserterLongReach = false;
        RecipePage = 0;
        ClearDemolishHold();
    }

    public void OpenBuildMenuWithCopy(EntityKind kind, Direction direction, ItemRecipeId? recipe, bool inserterLongReach = false)
    {
        CloseExclusiveModesExcept(ExclusiveUiMode.Build);
        IsBuildMenuOpen = true;
        PendingBuildKind = kind;
        PendingDirection = direction;
        PendingRecipe = recipe;
        PendingInserterLongReach = kind == EntityKind.Inserter && inserterLongReach;
        RecipePage = 0;
    }

    public void SelectPendingBuildKind(EntityKind kind)
    {
        PendingBuildKind = kind;
        if (!BuildBarModel.IsDirectedKind(kind))
        {
            PendingDirection = Direction.East;
        }

        if (kind != EntityKind.Assembler)
        {
            PendingRecipe = null;
        }

        if (kind != EntityKind.Inserter)
        {
            PendingInserterLongReach = false;
        }
    }

    /// <summary>Left-click empty/visible entity on the playfield (not build-place).</summary>
    public void ApplyPlayfieldSelection(int? newSelectedEntityId)
    {
        var previousSelectedId = SelectedEntityId;
        SelectedEntityId = newSelectedEntityId;
        if (SelectedEntityId != previousSelectedId)
        {
            CloseBastionComposition();
        }

        RecipePage = 0;
        TemplateUnitIndex = 0;
        CloseBuildMenu();
        ClearDemolishHold();
        ClearBastionPending();
    }

    public void OnBastionIndexSelected()
    {
        RecipePage = 0;
        TemplateUnitIndex = 0;
        ClearBastionPending();
    }

    /// <summary>Mutual-exclusion when opening research (closes build/energy/bastion composition).</summary>
    public void PrepareOpenResearchExclusive() => CloseExclusiveModesExcept(ExclusiveUiMode.Research);

    /// <summary>Mutual-exclusion when opening energy (closes build/research/bastion composition).</summary>
    public void PrepareOpenEnergyExclusive() => CloseExclusiveModesExcept(ExclusiveUiMode.Energy);

    /// <summary>Mutual-exclusion when toggling bastion composition (closes build/research/energy).</summary>
    public void PrepareToggleBastionCompositionExclusive() =>
        CloseExclusiveModesExcept(ExclusiveUiMode.BastionCompose);

    private void CloseExclusiveModesExcept(ExclusiveUiMode keep)
    {
        if (keep != ExclusiveUiMode.Build)
        {
            CloseBuildMenu();
        }

        if (keep != ExclusiveUiMode.Research)
        {
            CloseResearchOverlay();
        }

        if (keep != ExclusiveUiMode.Energy)
        {
            CloseEnergyOverlay();
        }

        if (keep != ExclusiveUiMode.BastionCompose)
        {
            CloseBastionComposition();
        }
    }
}
