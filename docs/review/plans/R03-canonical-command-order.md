# R03 — Canonical cross-peer порядок команд

**Severity:** High · **Домен:** determinism / lockstep · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду
**Статус реализации:** ✅ Реализовано 2026-08-11 (`ISimulationCommand.cs`, `SimulationCommands.cs`, `GameSimulation.Commands.cs`; тесты в `CommandQueueTests.cs`). ⚠️ Требуется прогон `dotnet build -c Release` + `dotnet test`.

### Что сделано
- В `ISimulationCommand` добавлен `long Sequence`; в `SimulationCommandBase` он реализован как **init-only** авто-свойство (не входит в позиционный конструктор → все существующие call-sites остаются исходно-совместимыми; источник ставит через `command with { Sequence = n }`).
- `ApplyQueuedCommandsForCurrentTick` теперь собирает команды текущего тика в список, сортирует детерминированным ключом `(Actor.Value, Sequence)` (helper `CompareCanonical`; `List.Sort` + стабильный fallback для unsequenced) и применяет по каноническому порядку — не зависит от порядка enqueue/сетевого прихода.
- Duplicate suppression: `(Actor, Sequence)` применяется не более одного раза в рамках тика; `Sequence == 0` считается «unsequenced» (legacy/local) и освобождается от дедупликации.
- Тесты: `SameTickCommands_DifferentEnqueueOrder_ProduceIdenticalHash` (перестановка enqueue → одинаковый hash), `DuplicateActorSequence_IsAppliedAtMostOnce` (дубликат не применяется дважды). Существующие dual-run тесты не затронуты.

### Отклонения от исходного плана
- Сериализация `Sequence` в wire-формат (п.4) отнесена к R11 (протокол/schema/version); текущие round-trip тесты не ломаются, т.к. `Sequence` по умолчанию 0 у обеих сторон.
- Cross-tick ledger дубликатов (полная идемпотентность между тиками) — область R13; здесь дедупликация ограничена одним тиком (что покрывает сценарий переупорядочивания сетевого приёма в рамках тика).

## Проблема
Команды одного тика применяются в порядке добавления в `_commandBuffer` (FIFO по enqueue). DTO содержит `Actor` и `Tick`, но не имеет sequence/nonce. Если два peer получат один и тот же набор команд в разном сетевом порядке, итоговое состояние может разойтись (конкурирующие inventory/build intents). Существующие dual-run тесты проверяют одинаковый порядок, а не перестановки.

## Где в коде
- `src/SteelConveyorWar.Core/GameSimulation.Commands.cs:82-111` — `ApplyQueuedCommandsForCurrentTick`: `foreach (var command in _commandBuffer)` без сортировки.
- `src/SteelConveyorWar.Core/Commands/ISimulationCommand.cs:7-16` — интерфейс: только `Kind`, `Actor`, `Tick`.

## План фикса
1. Добавить в `ISimulationCommand` монотонный `ActorSequence` (или глобальный `Sequence`/`Nonce`), присваиваемый источником при enqueue.
2. Перед применением тика сортировать команды детерминированным ключом `(Tick, Actor.Value, ActorSequence)`.
3. Зафиксировать duplicate/idempotency-политику: одинаковый `(Actor, ActorSequence)` игнорируется (idempotent) либо отклоняется с логом.
4. Обновить serializer (R11), чтобы sequence сериализовался.

## Тесты
- Тест перестановок: один набор команд, поданный в разном порядке enqueue → идентичный state hash.
- Тест дубликатов: повторная команда с тем же `(Actor, ActorSequence)` не применяется дважды.
- Регрессия детерминизма dual-run сохраняется.

## Definition of Done
- Порядок применения не зависит от порядка сетевого прихода.
- Есть тест на перестановки и на дубликаты.

## Связанные замечания
R2 (sink присваивает sequence), R11 (wire-формат), R13 (ledger).
