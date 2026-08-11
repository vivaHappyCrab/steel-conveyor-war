# H01 — Сделать загруженные gameplay tables authoritative

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § H1  
**Severity:** High · **Домен:** content authority / determinism / extensibility · **Roadmap:** P0  
**Related residual:** R18 (open), R10 (partial), R30 (partial)  
**Статус валидации:** ✅ Подтверждено по коду на `develop` после PR #111/#112

---

## Проблема

Match загружает `config/gameplay-tables.json`, кладёт каталог в `GameSimulation.GameplayTables` и хеширует его в content manifest. Но runtime rules читают singleton `GameplayTablesCatalog.Embedded` через `MvpDefinitions`.

Следствия:

1. Правка JSON меняет manifest/handshake, но не меняет combat/power/recipes/footprints/collision/stacks.
2. Два binary с разным Embedded и одинаковым JSON могут пройти `EnsureMatch` и разойтись в симуляции.
3. Data-driven extensibility формально заявлена, фактически отсутствует.

Это корневой content-authority дефект третьего ревью.

Contrast: **build costs** уже match-scoped (`BuildCostCatalog` для place/cost). Gap specifically **gameplay tables** (recipes/stats/footprints/power/combat via `MvpDefinitions` → Embedded).

---

## Доказательства (file:line)

| Участок | Что происходит |
|---|---|
| `src/SteelConveyorWar.Core/Hosting/ContentBootstrap.cs:64-82` | JSON загружается и передаётся в `GameCreationOptions` |
| `src/SteelConveyorWar.Core/GameSimulation.cs:61-73,101,189` | Match хранит `GameplayTables` |
| `src/SteelConveyorWar.Core/Content/SimulationContentManifest.cs:27-33,121-209` | Manifest хеширует **stored** catalog |
| `src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs:55-85,128-145` | Facade всегда делегирует в `GameplayTablesCatalog.Embedded` |
| `src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs:71-100` | Recipes через `MvpDefinitions.ProductionRecipes` |
| `src/SteelConveyorWar.Core/Systems/PowerSystem.cs` | Demand/production через `MvpDefinitions` |
| `src/SteelConveyorWar.Core/Systems/CombatSystem.cs` / `MovementSystem.cs` | Stats/collision через `MvpDefinitions` |
| `src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs:183-193` | Footprint / unit classification через Embedded |
| `src/SteelConveyorWar.Sfml/Ui/HudOverlay.cs` | Footprints/stats через `MvpDefinitions` (~13 call sites) |
| `tests/.../ConfigContentLoaderTests.cs:282-296` | Override виден на `simulation.GameplayTables`, но `MvpDefinitions.GetStats` остаётся Embedded (уже есть partial proof) |

### Полный inventory call sites `MvpDefinitions` gameplay accessors

По состоянию дерева после PR #111 (counts = число совпадений на файл):

| Файл | ~hits | Домен |
|---|---:|---|
| `Systems/PowerSystem.cs` | 5 | demand/production/buffer |
| `Systems/FactoryBastionSystem.cs` | 5 | recipes |
| `Systems/CombatSystem.cs` | 4 | stats |
| `Systems/MovementSystem.cs` | 3 | collision/stats |
| `Systems/FogOfWarSystem.cs` | 3 | vision/signature |
| `Systems/ProductionSystem.cs` | 2 | recipes |
| `Systems/CommanderOrdersSystem.cs` | 1 | footprint/placement |
| `GameSimulation.cs` | 11 | placement/queries/stats |
| `World/GameWorld.cs` | 2 | occupancy helpers |
| `State/WorldEntity.cs` | 2 | ctor defaults |
| `Logistics/Inventory.cs` | 2 | stack sizes |
| `Combat/CombatDamage.cs` | 1 | resistances |
| `Energy/EnergyStatsHistory.cs` | 2 | labels/series |
| `Sfml/Rendering/WorldRenderer.cs` | 4 | footprint/draw |
| `Sfml/Ui/HudOverlay.cs` | 13 | HUD details |

Любой пропущенный hit после миграции = mixed Embedded/match behavior (хуже, чем полный Embedded).

---

## Почему предыдущий «R18 closed» неполный

R18 добавил loader, embedded parity, manifest v2 и делегирование `MvpDefinitions → Embedded`. Это закрыло **инфраструктуру каталога**, но не **runtime wiring**: systems не получили match-scoped catalog. STATUS.md пометил R18 ✅, хотя DoD «gameplay tables authoritative» не выполнен.

---

## Корневая причина

Статический facade `MvpDefinitions` удобен для тестов без simulation context, но скрывает dependency на match content. Пока systems/UI вызывают static API, `GameSimulation.GameplayTables` остаётся мёртвым полем с живым hash.

---

## Целевой дизайн

```text
GameCreationOptions.GameplayTables
  -> GameSimulation.GameplayTables (authoritative)
       -> ISimulationSystemContext.GameplayTables
            -> Power/Combat/Movement/Factory/Logistics/...
       -> observation/presentation snapshots (footprints/stats)
MvpDefinitions.*  -> либо удалить gameplay accessors,
                     либо принимать explicit catalog (no Embedded default in production paths)
GameplayTablesCatalog.Embedded -> только default composition / unit-test fallback
```

---

## План фикса (поэтапно)

### Этап A — wiring Core (обязательный, P0)

1. Добавить `GameplayTablesCatalog Tables { get; }` в `ISimulationSystemContext` и все system contexts.
2. Заменить call sites `MvpDefinitions.GetStats/GetFootprint/GetCollisionSize/GetPowerDemand/ProductionRecipes/ItemRecipes/TechSignatureIntensity/GetResistanceBasisPoints` на `context.GameplayTables` / `simulation.GameplayTables`.
3. Для кода вне system context (helpers на `GameSimulation`) использовать instance property `GameplayTables`, не Embedded.
4. Оставить `MvpDefinitions.UnitKinds` / `FactoryKinds` / structural predicates (`IsWallKind`, `BlocksGroundMovement`) до отдельного content-id этапа (не блокируют H01).
5. Добавить fail-fast invariant: при создании match `GameplayTables` не null; в production hosts всегда из bootstrap.

### Этап B — presentation

6. Передавать match catalog / footprint snapshot в SFML (`WorldRenderer`, `HudOverlay`, build ghost preview) вместо `MvpDefinitions.GetFootprint/GetStats`.
7. Не читать Embedded в Client/SFML production path.

### Этап C — API cleanup

8. Пометить gameplay accessors в `MvpDefinitions` как obsolete или удалить; оставить только pure predicates / constants.
9. Обновить docs (`MVP_IMPLEMENTATION_DECISIONS.md`): match-scoped tables — authoritative; Embedded — fallback only.
10. Manifest уже хеширует stored catalog — после wiring hash начнёт отражать executed content (исправляет latent R10 mismatch).

### Рекомендуемый PR slice

- PR1: Core systems + tests (behavior change under custom catalog).
- PR2: SFML/presentation wiring.
- PR3: remove/obsolete Embedded defaults from production facades.

---

## Тесты

| Тест | Сценарий |
|---|---|
| `GameplayTablesAuthorityTests.CustomMaxHealth_IsAppliedAtSpawn` | Catalog override `EntityStats.MaxHealth` → entity health/max = override |
| `...CustomPowerDemand_AffectsFill` | Override demand → power distribution differs from Embedded |
| `...CustomRecipe_UsedByFactory` | Override recipe inputs/ticks → factory consumes override |
| `...CustomFootprint_AffectsPlacement` | Override footprint → `CanPlaceBuilding` / collision |
| `...ManifestMatchesExecutedCatalog` | Mutated JSON tables → manifest differs **and** behavior differs |
| Dual-run | Same custom catalog on two sims → identical hash |
| Regression | Default bootstrap content behavior unchanged vs current Embedded parity |

Файлы: `tests/SteelConveyorWar.Core.Tests/GameplayTablesAuthorityTests.cs`, обновить `ConfigContentLoaderTests` (storage-only недостаточно), SFML footprint tests если есть preview.

---

## Риски / non-goals

**Риски**

- Широкий diff по systems; легко пропустить call site → mixed Embedded/match behavior ещё опаснее.
- Тесты, которые мутируют Embedded или полагаются на static defaults, потребуют явного catalog inject.
- Presentation без PR2 временно может рисовать неправильный footprint при custom content (default content OK).

**Non-goals**

- Замена `EntityKind` enum на string ids (остаток R18 stage 2).
- Полный modding без rebuild.
- Изменение balance numbers в JSON.

---

## Definition of Done

- [ ] Ни один production Core path не читает `GameplayTablesCatalog.Embedded` для match rules.
- [ ] Custom `GameplayTablesCatalog` в `GameCreationOptions` меняет health/power/recipes/footprints/collision/stacks/combat stats.
- [ ] Manifest hash соответствует executed catalog.
- [ ] SFML production path использует match footprints/stats (или snapshot из Core).
- [ ] Есть negative/positive authority tests + `eng/verify.ps1` green.
- [ ] `docs/review/STATUS.md` / plan status обновлены; R18 отмечается закрытым **только** по authority wiring (enum→string — отдельный follow-up).

---

## Верификация

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
dotnet test tests/SteelConveyorWar.Core.Tests -c Release --filter GameplayTablesAuthority
```

## Специалисты

`modding-extensibility`, `core-simulation`, `multiplayer-correctness`, `sfml-platform`, `test-ci`.
