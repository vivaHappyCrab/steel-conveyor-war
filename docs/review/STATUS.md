# Сводный статус код-ревью R01–R34

**Дата:** 2026-08-12 (четвёртое ревью после PR #114)  
**Сборка/тесты:** ✅ `eng/verify.ps1` — Core 437; Hosting 4; SFML 76; bench quick.

> Историческая запись волны PR #111 («34/34 fully realized») **не** является строгим DoD-счётом.  
> Актуальная переоценка: [`CODE_REVIEW_FOURTH_2026-08-12.md`](CODE_REVIEW_FOURTH_2026-08-12.md) — **~32 ✅ / 2 🟡 (R01, R34)**.

## Итоги (строго после #114)

- **Всего пунктов:** 34
- **✅ Реализовано полностью (строгий DoD):** ~32
- **🟡 Частично:** 2 (`R01` null-actor `Try*`; `R34` tiles unused in cross-graph)
- **⏸️ Отложено:** 0

## Сводная таблица

| ID  | Тема                                              | Статус |
|-----|---------------------------------------------------|--------|
| R01 | Авторизация actor в командах                       | 🟡 Частично (bound sink; `Try*` null opt-out) |
| R02 | Очередь команд на боевом пути (production path)    | ✅ Реализовано |
| R03 | Канонический порядок команд                        | ✅ Реализовано |
| R04 | Неизменяемый снапшот наблюдения                     | ✅ Реализовано |
| R05 | Устранение cast к мутабельным коллекциям           | ✅ Реализовано |
| R06 | Декомпозиция god-object (Power + Combat)            | ✅ Реализовано |
| R07 | Spatial index для movement/collision               | ✅ Реализовано |
| R08 | Приказы командиров в state-hash                     | ✅ Реализовано |
| R09 | Защита от некорректных (malformed) команд           | ✅ Реализовано |
| R10 | Хеш манифеста контента                              | ✅ Реализовано |
| R11 | Версионирование протокола команд                   | ✅ Реализовано |
| R12 | Глубоко неизменяемые payload'ы                      | ✅ Реализовано |
| R13 | Pending-команды в state-hash                        | ✅ Реализовано |
| R14 | Устранение публичных mutation-хуков                | ✅ Реализовано |
| R15 | Batched water-filling в распределении энергии       | ✅ Реализовано |
| R16 | FoW/tech-signatures: dirty-регионы                 | ✅ Реализовано |
| R17 | Учёт factory/bastion (accounting)                  | ✅ Реализовано |
| R18 | Data-driven сущности / gameplay tables             | ✅ Реализовано |
| R19 | Контракт наблюдения для ботов                       | ✅ Реализовано |
| R20 | Координаты в millitiles (fixed-point)               | ✅ Реализовано |
| R21 | Декомпозиция SFML-сессии                            | ✅ Реализовано |
| R22 | Бенч-гейт производительности                        | ✅ Реализовано |
| R23 | Ограничение catch-up фиксированного шага            | ✅ Реализовано |
| R24 | Валидация некорректного local player                | ✅ Реализовано |
| R25 | Общий bootstrap хостов (Client/Headless)            | ✅ Реализовано |
| R26 | Исследование через вражескую лабораторию           | ✅ Реализовано |
| R27 | FoW для selection и трассеров                        | ✅ Реализовано |
| R28 | Валидация research-контента                          | ✅ Реализовано |
| R29 | Детерминизм распределения research                  | ✅ Реализовано |
| R30 | Build-меню из рантайм-каталога                       | ✅ Реализовано |
| R31 | Состав ростера и победа по командам                 | ✅ Реализовано |
| R32 | Настраиваемый TPS                                   | ✅ Реализовано |
| R33 | SFML rendering hotspots (minimap cache)             | ✅ Реализовано |
| R34 | Кросс-каталожная валидация схем контента            | 🟡 Частично (tiles unused) |

## Доработка отложенных (этот проход)

| ID | Что сделано |
|----|-------------|
| R22 | `tests/SteelConveyorWar.Benchmarks` (BenchmarkDotNet + `--quick` soft gate); CI job `benchmark-quick`; property round-trip serializer tests |
| R15 | Batched water-fill в `PowerSystem`; `EnergyBatchedWaterfillTests` |
| R07 | `SpatialQueryIndex` + `PathfindingWorkspace`; movement/threat на spatial; dual-run тесты |
| R16 | FoW dirty tracking + `GetFogDirtyTiles`; инкрементальные tech signatures; `FogDirtyRegionTests` |
| R17 | Scratch-индексы factory/bastion/units + idle-skip epoch; live mid-tick spawn |
| R33 | Minimap `RenderTexture` + `MinimapDirtyTracker` (dirty FoW/entities) |
| R31 | `MapPlayerDefinition` startCommander/Bastion/Hub + color; `CreateStartingEntities` из карты; цвета в `WorldRenderer` |
| R21 | `SessionState` + `InputCommandMapper` + unit-тесты без окна |
| R18 | `config/gameplay-tables.json` + `GameplayTablesCatalog`/`Loader`; `MvpDefinitions` делегирует Embedded; manifest v2 |
| R20 | `WorldPosition`/`CollisionSize` millitiles; `AlgorithmVersion=8`; ADR 0001 обновлён |

## Верификация

```powershell
dotnet build SteelConveyorWar.sln -c Release
dotnet test tests/SteelConveyorWar.Core.Tests -c Release   # 382
dotnet test tests/SteelConveyorWar.Sfml.Tests -c Release    # 62
dotnet run --project tests/SteelConveyorWar.Benchmarks -c Release -- --quick
pwsh eng/verify.ps1
```

---

# Post-remediation H/M/L (третье ревью)

**Дата:** 2026-08-12  
**Ветка:** `ai/61-post-remediation-fixes` · issue [#61](https://github.com/vivaHappyCrab/steel-conveyor-war/issues/61)  
**Индекс планов:** [`docs/review/plans/post-remediation/PLANS_INDEX.md`](plans/post-remediation/PLANS_INDEX.md)  
**Верификация P0 (+ caretaker H06/M02 hardening):** ✅ scoped verify — Core **426**, SFML **70**, headless smoke  
**Верификация M10/M11/M07:** ✅ `eng/verify.ps1` — Core **438**, SFML **69**, headless + client smoke  
**Верификация M04–M08 + M09/L01–L03:** ✅ `eng/verify.ps1` — Core **437**, Hosting **4**, SFML **76**, headless + client smoke

## Wave P0

| ID | Тема | Статус |
|----|------|--------|
| H04 | Factory idle-cache invalidation (CapabilityEpoch + unlock gate) | ✅ |
| H02 | Research remainder hash (`AlgorithmVersion` 9) | ✅ |
| H01 | Gameplay tables authoritative (match-scoped) | ✅ |
| H07 | Content catalog deep freeze | ✅ |
| H05 | Build menu from runtime `BuildCostCatalog` | ✅ |
| H06 | Combat tracer FoW Policy B (no hidden endpoint leak) | ✅ |
| H03 | Canonical command identity + `BoundPlayerCommandSink` | ✅ |

## Wave P1 / P2

| ID | Тема | Статус |
|----|------|--------|
| M01 | Payload `with` bypass | ✅ |
| M02 | Command protocol vocabulary | ✅ |
| M03 | Schema + cross-catalog validation | ✅ |
| M10 | Observation frame immutability | ✅ |
| M11 | Session manifest / roster / draw | ✅ |
| M07 | Minimap catch-up dirty union | ✅ |
| M04 | Benchmark hard gate + matrix | ✅ |
| M05 | Level energy water-fill | ✅ |
| M06 | Spatial rebuilds / FoW dirty paint | ✅ |
| M08 | SFML resources + session split | ✅ |
| M09 | Host bootstrap → `SteelConveyorWar.Hosting` | ✅ |
| L01 | Trailing `--local-player` errors | ✅ |
| L02 | Commander select after death (no throw) | ✅ |
| L03 | UI mode exclusivity (incl. build) | ✅ |

Все post-remediation планы (H/M/L) закрыты в этом PR.

### Post-remediation residual notes (honest)

| Residual | Notes |
|----------|-------|
| R22 | CI `SCW_BENCH_HARD_GATE=1`; calibrated 5ms / 2.5MB; expanded matrix (idle_factory/army100/battle/power_equal/fow_moving/hash). Local soft. |
| R15 | Level water-fill for identical `(buffer,capacity)` plateaus; equivalence tests + `power_equal` bench. |
| R07 | ≤2 shared spatial rebuilds/tick (post-commands, post-factory); combat shares index; path/candidate scratch; placement `AnyAliveAt`. |
| R16 | Moved-source dirty disk decay/paint; static sources skip full decay. |
| R21 | Tile `RectangleShape` disposed; `SimulationPump` + `PresentationComposer` extracted; exclusive UI modes include build (L03); mapper purity still partial. |
| R25 | Bootstrap I/O moved to `SteelConveyorWar.Hosting` (M09); Core parse-only restored. |

---

# Четвёртое ревью (после PR #114)

**Дата:** 2026-08-12  
**Отчёт:** [`CODE_REVIEW_FOURTH_2026-08-12.md`](CODE_REVIEW_FOURTH_2026-08-12.md)  
**Commit:** `c31ba95`  
**Верификация:** ✅ `eng/verify.ps1` — Core **437**, Hosting **4**, SFML **76**, headless + client smoke

## Итог аудита

| Метрика | Третье ревью | Четвёртое |
|---------|-------------:|----------:|
| Итоговая оценка | 5.8/10 | **7.4/10** |
| High findings | 7 | **0** |
| Post-remediation H/M/L | open catalog | **21/21 CLOSED** |
| R01–R34 (строгий DoD) | 8✅ / 24🟡 / 2🔴 | **~32✅ / 2🟡 (R01, R34)** |

## Честные остатки

| ID | Статус | Notes |
|----|--------|-------|
| R01 | 🟡 | Hosts bind via `BoundPlayerCommandSink`; public `Try*` still allow `actor = null` |
| R34 | 🟡 | Cross-catalog expanded; tiles catalog still unused in graph |
| Low polish | — | `MvpDefinitions` Embedded accessors not obsolete; `GetEntitiesAt` allocs; SFML mapper/`Run` size |

P1 focus: seal null-actor `Try*`, transport/auth prototype, cross-OS hash compare.
