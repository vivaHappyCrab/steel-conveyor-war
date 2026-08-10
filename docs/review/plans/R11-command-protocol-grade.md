# R11 — Довести command wire-формат до protocol-grade

**Severity:** Medium · **Домен:** protocol / network · **Roadmap:** P0/P1
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
JSON-сериализация команд достаточна для research/prototype, но не для untrusted network input: нет версии протокола/схемы, нет command id/sequence, часть пропущенных полей заполняется defaults, модель команд не выражает ряд research-intent'ов, round-trip тесты покрывают только move и bastion order, результат/отказ команды не логируется.

## Где в коде
- `src/SteelConveyorWar.Core/Commands/SimulationCommandSerializer.cs` — нет schema/protocol version; нет command id/sequence; defaults для пропущенных полей (`Clockwise ?? true`, `Direction ?? East`).
- Модель команд не выражает `TryConfirmExclusive`, `TrySetProjectWeight`, `preferredTrackId`, `confirmExclusive`.
- `tests/SteelConveyorWar.Core.Tests/CommandQueueTests.cs:48-71` — round-trip только для move и bastion order.
- `src/SteelConveyorWar.Core/GameSimulation.Commands.cs:82-111` — `ApplyQueuedCommandsForCurrentTick` игнорирует `bool`-результат (нет логирования rejection).

## План фикса
1. Добавить в конверт команды `SchemaVersion`/`ProtocolVersion` и `CommandId`/`Sequence` (согласовать с R3).
2. Убрать «тихие» defaults: пропущенные обязательные поля → ошибка валидации (R9), а не подстановка.
3. Расширить command-модель на все research-intent'ы (`ConfirmExclusive`, `ProjectWeight`, `preferredTrackId`).
4. Ввести `CommandResult` (Ok/Rejected+reason); `ApplyQueuedCommandsForCurrentTick` логирует отказы.
5. Round-trip/property тесты на **все** `SimulationCommandKind`.

## Тесты
- Property-тест: сериализация→десериализация для каждого command kind даёт эквивалентный объект.
- Тест: неизвестная/старшая `SchemaVersion` отклоняется предсказуемо.
- Тест: rejection логируется с причиной.

## Definition of Done
- Wire-формат версионирован, покрыт round-trip тестами по всем kinds.
- Все research-intent'ы выразимы через команды.

## Связанные замечания
R2, R3, R9, R34 (schema evolution policy).
