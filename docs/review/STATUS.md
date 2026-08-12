# Сводный статус код-ревью R01–R34

**Дата:** 2026-08-11 (доработка отложенных)
**Сборка/тесты:** ✅ `dotnet build -c Release`; Core 382; SFML 62; bench quick + CI job `benchmark-quick`.

## Итоги

- **Всего пунктов:** 34
- **✅ Реализовано полностью:** 34
- **🟡 Частично:** 0
- **⏸️ Отложено:** 0

## Сводная таблица

| ID  | Тема                                              | Статус |
|-----|---------------------------------------------------|--------|
| R01 | Авторизация actor в командах                       | ✅ Реализовано |
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
| R34 | Кросс-каталожная валидация схем контента            | ✅ Реализовано |

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
| M10–M11, M04–M09, L01–L03 | остаток индекса | ⏳ тот же PR |

Порядок оставшихся: M10 → M11 → M07 → M04 → M05/M06 → M08 → M09 → L01–L03.
