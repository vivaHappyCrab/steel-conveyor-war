# R08 — Добавить queued commander orders в state hash

**Severity:** High · **Домен:** determinism / desync detector · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду
**Статус реализации:** ✅ Реализовано 2026-08-11 (`SimulationStateHasher.cs`, тесты в `DeterminismHashTests.cs`). ⚠️ Требуется прогон `dotnet build -c Release` + `dotnet test`.

### Что сделано
- В `WriteEntity` добавлена детерминированная сериализация `QueuedBuildOrder` (`TargetKind`, `TargetPosition.X/Y`, `Direction`, `SelectedItemRecipe` наличие+значение) и `QueuedDemolishOrder` (`TargetEntityId`), с флагом наличия перед каждым блоком (как для остальных nullable-полей).
- `AlgorithmVersion` поднят `5 → 6`.
- Тесты: `QueuedBuildOrder_AffectsStateHash`, `QueuedDemolishOrder_AffectsStateHash` (разные очереди → разные hashes), `SameQueuedBuildOrder_ProducesIdenticalHash` (детерминизм сохранён). Тесты используют `internal set` через существующий `InternalsVisibleTo`.
- Golden-ссылки не трогались — они отложены (#80), проверка идёт через dual-run equality.

## Проблема
`QueuedBuildOrder` и `QueuedDemolishOrder` определяют поведение будущих тиков, но не участвуют в state hash. Два состояния с одинаковым hash могут выполнить разные build/demolish на следующих тиках — прямой дефект desync-детектора.

## Где в коде
- `src/SteelConveyorWar.Core/State/WorldEntity.cs:91` — `public CommanderBuildOrder? QueuedBuildOrder`; `:93` — `QueuedDemolishOrder`.
- `src/SteelConveyorWar.Core/Systems/CommanderOrdersSystem.cs:19-65` — эти очереди управляют будущими тиками.
- `src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs:93-174` — `WriteEntity` пишет `Order` (bastion), `MovementPath`, `MoveTarget`, `BastionTemplate`, но **не** пишет `QueuedBuildOrder`/`QueuedDemolishOrder`.

## План фикса
1. В `WriteEntity` добавить сериализацию обеих очередей: наличие + все значимые поля (`TargetKind`, `Position`, `Direction`, `SelectedItemRecipe`, `TargetEntityId` и т.п.).
2. Соблюсти детерминированный порядок записи (как для остальных полей).
3. Поднять `AlgorithmVersion` (сейчас `= 5`, строка 24) на 6.
4. Обновить/освежить любые golden-ссылки после изменения hash surface.

## Тесты
- Регрессия: два состояния, различающиеся только `QueuedBuildOrder` (или demolish) → **разные** hashes.
- Регрессия: одинаковые очереди → одинаковый hash (детерминизм сохранён).

## Definition of Done
- Обе commander-очереди входят в versioned hash surface.
- Есть тест «разные queued orders → разные hashes».

## Связанные замечания
R10 (content manifest hash), R13 (pending command ledger в hash).
