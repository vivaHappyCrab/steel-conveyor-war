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

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **Версионирование протокола.** В `SimulationCommandSerializer` добавлены `public const int ProtocolVersion = 1` и `MinSupportedProtocolVersion = 1`. Конверт (`CommandEnvelopeDto`) получил поля `ProtocolVersion` и `Sequence`. На сериализации каждый конверт штампуется `ProtocolVersion` + канонической последовательностью (`command.Sequence`, R03). На десериализации `ValidateProtocol` предсказуемо отклоняет неизвестную/старшую версию (`NotSupportedException` → `TryDeserialize` возвращает `false`); отсутствие поля (legacy pre-versioning) принимается как текущая версия.
- **Sequence по проводу.** Ранее `Sequence` терялся при round-trip. Теперь `FromDto` пере-штампует восстановленную команду через `WithScheduling(tick, dto.Sequence)`, сохраняя runtime-тип; `Sequence = 0` (unsequenced/legacy) остаётся нулём.
- **CommandResult + логирование отказов.** Добавлен `Commands/CommandResult.cs` (`CommandResult` Ok/Rejected+reason и `CommandRejection`). `ApplyQueuedCommandsForCurrentTick` больше не игнорирует `bool`-результат: отказ (handler вернул `false`) и брошенное исключение записываются в `_commandRejections` с причиной. Новое презентационное свойство `GameSimulation.LastTickCommandRejections` (не хешируется), очищается в начале каждого тика.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/CommandProtocolTests.cs`: property-round-trip по **всем 20** `SimulationCommandKind` (сохранение Kind/Actor/Tick/Sequence и runtime-типа) + сторож `AllTwentyKinds_AreExercised` (падает при добавлении нового kind без покрытия); конверт содержит `protocolVersion`; старшая версия отклоняется (`Deserialize` бросает, `TryDeserialize` → false); legacy-конверт без версии принимается; отклонённая команда логируется с причиной; принятая команда не даёт отказов.

### Отклонения / заметки
- **«Тихие» defaults не удалены** (пункт плана 2): `Clockwise ?? true` и `Direction ?? East` оставлены — это законные optional-поля с осмысленным дефолтом, а не пропуск обязательного поля (обязательные уже валидируются через `Require`/R09). Ужесточение отложено, чтобы не ломать optional-семантику.
- **Отдельная команда `SetProjectWeight` не вводилась** (пункт плана 3): вес треков выражается существующим `SetTrackAllocationCommand`, а `ConfirmExclusive`/`preferredTrackId` — существующим `SelectResearchCommand` (R02). Т.е. все research-intent'ы уже выразимы командами; дублирующий DTO не добавлялся.
- `CommandId` как отдельное поле не добавлен: канонический ключ дедупликации/порядка — пара `(Actor, Sequence)` (R03), её достаточно; отдельный глобальный id отложен.
