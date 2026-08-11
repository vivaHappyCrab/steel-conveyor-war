using SteelConveyorWar.Core;

namespace SteelConveyorWar.Sfml;

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

    /// <summary>F1 / focus-commander: keep selection, clear build/demolish/bastion UI.</summary>
    public void ClearTransientUiKeepingSelection()
    {
        IsBuildMenuOpen = false;
        PendingBuildKind = null;
        PendingDirection = Direction.East;
        PendingRecipe = null;
        ClearDemolishHold();
        CloseBastionComposition();
        ClearBastionPending();
    }

    public void ToggleBuildMenu(EntityKind defaultBuildKind)
    {
        IsBuildMenuOpen = !IsBuildMenuOpen;
        PendingBuildKind = IsBuildMenuOpen ? defaultBuildKind : null;
        PendingDirection = Direction.East;
        PendingRecipe = null;
        RecipePage = 0;
        ClearDemolishHold();
    }

    public void OpenBuildMenuWithCopy(EntityKind kind, Direction direction, ItemRecipeId? recipe)
    {
        IsBuildMenuOpen = true;
        PendingBuildKind = kind;
        PendingDirection = direction;
        PendingRecipe = recipe;
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
        IsBuildMenuOpen = false;
        PendingBuildKind = null;
        PendingDirection = Direction.East;
        PendingRecipe = null;
        ClearDemolishHold();
        ClearBastionPending();
    }

    public void OnBastionIndexSelected()
    {
        RecipePage = 0;
        TemplateUnitIndex = 0;
        ClearBastionPending();
    }

    /// <summary>Mutual-exclusion when opening research (closes energy + bastion composition).</summary>
    public void PrepareOpenResearchExclusive()
    {
        CloseEnergyOverlay();
        CloseBastionComposition();
    }

    /// <summary>Mutual-exclusion when opening energy (closes research + bastion composition).</summary>
    public void PrepareOpenEnergyExclusive()
    {
        CloseResearchOverlay();
        CloseBastionComposition();
    }

    /// <summary>Mutual-exclusion when toggling bastion composition (closes research + energy).</summary>
    public void PrepareToggleBastionCompositionExclusive()
    {
        CloseResearchOverlay();
        CloseEnergyOverlay();
    }
}
