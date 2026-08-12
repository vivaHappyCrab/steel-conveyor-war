# H05 — SFML build menu из runtime BuildCostCatalog

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § H5  
**Severity:** High (content extensibility blocker; default content currently matches) · **Домен:** SFML UI / content · **Roadmap:** P0  
**Related residual:** R30 (partial), H01/R18  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

`BuildMenuCatalog.ComposeFrom(BuildCostCatalog)` существует, но production static `BuildableKinds` инициализируется **один раз** из `MvpBuildCostCatalog.Embedded`. Hotkeys, hit-testing и overlay всегда используют этот static array.

При custom/runtime catalog (added/removed kinds) UI расходится с Core: можно enqueue kind, которого нет в match catalog, или не видеть доступный kind.

Affordability уже идёт через Core query — состав меню нет.

---

## Доказательства

| Участок | Что происходит |
|---|---|
| `BuildMenuCatalog.cs:38-45` | `BuildableKinds = ComposeFrom(Embedded)` |
| `InputCommandMapper.cs:162-165,325-327` | Hotkey B / number shortcuts на static array |
| `BuildBarOverlay.cs:9-33,66-72` | Bounds/pick/draw на static array |
| `BuildBarModel.cs:96-109` | Copy fallback `buildCosts ?? Embedded` |
| `BuildMenuCatalogTests.cs` | Сравнивает с Embedded, не с match catalog |

---

## Почему R30 «closed» неполный

R30 закрыл affordability + ComposeFrom helper + default menu completeness vs default JSON. Центральный DoD «production menu composed from runtime catalog» не выполнен: static Embedded composition осталась.

---

## Целевой дизайн

```text
SfmlPlaySession start:
  menu = BuildMenuCatalog.ComposeFrom(simulation.BuildCostCatalog)
  pass IReadOnlyList<EntityKind> into InputCommandMapper + overlays

No static BuildableKinds in production path.
PreferredOrder stays as ordering hint only.
```

Optional later (with observation): filter by `IsBuildKindAvailable(localPlayer, kind)` for greying — indices for hotkeys must stay stable for the match.

---

## План фикса

1. Удалить `public static readonly EntityKind[] BuildableKinds` production use (оставить obsolete helper или test-only).
2. `SfmlPlaySession` / composition root: `var buildMenu = BuildMenuCatalog.ComposeFrom(simulation.BuildCostCatalog)`.
3. Thread `buildMenu` through:
   - `InputCommandMapper` ctor/state;
   - `BuildBarOverlay.GetBuildBarBounds/TryPick/DrawBuildBar`;
   - any bastion UI that assumed static length.
4. `BuildBarModel.TryCopyFromWorldEntity`: require `simulation.BuildCostCatalog` (no Embedded default in production).
5. Tests:
   - custom catalog with extra kind → appears in composed menu;
   - custom catalog without a default kind → absent from menu/hotkeys;
   - session uses simulation catalog, not Embedded;
   - update `BuildMenuCatalogTests` accordingly.
6. Docs: R30 residual closed.

---

## Тесты

- `BuildMenuCatalogTests.ComposeFrom_UsesProvidedCatalogNotEmbedded`
- `InputCommandMapperTests` with injected menu kinds
- Integration: Host with mutated build-costs JSON (or in-memory catalog) shows matching menu length

---

## Риски / non-goals

**Риски:** hotkey index mapping changes if catalog order changes — PreferredOrder mitigates for known kinds.  
**Non-goals:** research-gated dynamic hide (optional follow-up); data-driven icons.

Severity note: shipped defaults match Embedded↔JSON, so default MVP play OK; High retained as extensibility/network content blocker per review rubric.

---

## Definition of Done

- [x] Production SFML path never reads `MvpBuildCostCatalog.Embedded` for menu composition.
- [x] Menu kinds == `simulation.BuildCostCatalog.Costs.Keys` (ordered).
- [x] Tests cover custom catalog divergence.
- [x] R30 marked closed.

---

## Верификация

```powershell
dotnet test tests/SteelConveyorWar.Sfml.Tests -c Release --filter BuildMenu
dotnet test tests/SteelConveyorWar.Core.Tests -c Release --filter BuildAfford
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
```

## Специалисты

`sfml-platform`, `modding-extensibility`, `test-ci`.
