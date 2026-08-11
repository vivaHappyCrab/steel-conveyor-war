# R02 — Перевести production input на command queue

**Severity:** High · **Домен:** command pipeline · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Command queue/DTO/serializer существуют, но реальные hosts их обходят: SFML и Headless вызывают immediate `Try*`/`TrySet*` прямо из обработчиков событий. Следствия: локальный ввод не попадает в replayable command log; результат зависит от того, между какими тиками пришло оконное событие; queue и serializer не проверяются production-путями; бот и человек используют разные timing-пути.

## Где в коде
- Разрешение на legacy immediate APIs зафиксировано в doc-комментарии: `src/SteelConveyorWar.Core/GameSimulation.Commands.cs:5-8`.
- SFML вызывает симуляцию напрямую, напр.: `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs:379` (`TrySetAssemblerRecipe`), `:391` (`TrySelectResearch`), а также блоки обработчиков ввода в диапазонах ~200-212, ~744-816, ~889-918.
- Headless stub: `src/SteelConveyorWar.Headless/HeadlessHostRunner.cs:41-64` — immediate move.

## План фикса
1. Ввести интерфейс `IPlayerCommandSink` с единственной операцией `Enqueue(ISimulationCommand)` (никаких immediate-мутаций).
2. Реализация по умолчанию складывает команды в `GameSimulation.EnqueueCommand` на согласованный tick (`Tick + N`, где N — input delay).
3. Переписать SFML-обработчики: вместо `simulation.Try*` создавать DTO-команду и класть в sink. Тот же путь — для Headless stub.
4. Пометить immediate `Try*` как `internal`/`[Obsolete]` и оставить только для юнит-тестов ядра (или спрятать за `TestOnly`-фасадом).
5. Зафиксировать input-delay/tick-mapping политику в одном месте (см. R32 про TPS и назначение тиков).

## Тесты
- Тест: последовательность SFML-подобных вводов даёт идентичный state hash при двух прогонах (детерминизм через очередь).
- Тест: любой ввод порождает запись в command log; лог реплеится в тот же итоговый hash.
- Headless сценарий проходит только через sink (нет прямых `Try*`).

## Definition of Done
- Ни один host не вызывает immediate `Try*` в gameplay-пути.
- Есть replayable command log, покрытый тестом реплея.

## Связанные замечания
R1 (authorization в том же слое), R3 (canonical order), R11 (протокол), R13 (ledger в hash).

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, поэтому код не компилировался и тесты не запускались. Ниже перечислены изменения; их нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **Core — funnel ввода.** Добавлены `Commands/IPlayerCommandSink.cs` (единая операция `Enqueue`, экспонирует `InputDelayTicks` и `CommandLog`) и реализация по умолчанию `Commands/DeferredCommandSink.cs`. Sink стампует каждую команду тиком `simulation.Tick + InputDelayTicks` (по умолчанию 1) и монотонной пер-actor последовательностью (начиная с 1, т.к. Sequence 0 зарезервирован под legacy/unsequenced по R03), кладёт через `GameSimulation.EnqueueCommand` и пишет копию в replayable command log.
- **`WithScheduling`.** В `SimulationCommandBase` добавлен `WithScheduling(tick, sequence)` (через `with`), чтобы sink мог обобщённо перештамповать любую команду, сохранив runtime-тип записи.
- **SelectResearch как команда.** `TrySelectResearch` ранее не имел DTO. Добавлены `SelectResearchCommand` (+ `SimulationCommandKind.SelectResearch = 20`), ветка в `GameSimulation.Commands.cs` (`ApplyCommand`), сериализация в обе стороны и поля `ConfirmExclusive`/`PreferredTrackId` в `CommandPayloadDto`. `StartResearchCommand` теперь пробрасывает actor.
- **Headless host.** `HeadlessHostRunner` создаёт `DeferredCommandSink`, stub-AI кладёт `IssueMoveCommand` в sink вместо `simulation.TryIssueMoveCommand`; результат отдаёт `CommandLog` (новое поле в `HeadlessHostResult`).
- **SFML host.** Добавлен `Input/SfmlCommandGateway.cs` — тонкая обёртка над `IPlayerCommandSink`, строящая нужный DTO для каждого действия. Все обработчики ввода в `SfmlPlaySession.cs`, `SfmlInputHelpers.ToggleResearchAllocation`, `HudOverlay.TryHandleSidebarStorageClick`, `BastionUiOverlay` (ApplyBastionOrderCommand / TryAdjustBastionTemplate / TryHandleBastionPendingMapClick) переведены с `simulation.Try*` на `commands.*`.
- **Проверка:** grep по `simulation.Try*`-геймплей-мутациям в `src/` → совпадений нет (DoD «Ни один host не вызывает immediate `Try*` в gameplay-пути»).

### Тесты
- `tests/SteelConveyorWar.Core.Tests/DeferredCommandSinkTests.cs`: планирование на будущий тик + монотонность последовательности; независимые последовательности пер-actor; отказ при `inputDelayTicks < 1`; детерминизм через sink (одинаковый hash в двух прогонах); **реплей command log в идентичный hash** (DoD «Есть replayable command log, покрытый тестом реплея»).
- `CommandQueueTests.cs`: round-trip сериализации `SelectResearchCommand` (полный и с дефолтами).

### Отклонения / заметки
- Immediate `Try*` в ядре **не** помечены `[Obsolete]`/`internal` (пункт плана 4): они всё ещё нужны юнит-тестам ядра (напр. `RunImmediateMove`) и как реализация под `ApplyCommand`. Gameplay-путь хостов их не использует — DoD выполнен; полное сокрытие отложено.
- Gateway-методы возвращают `true` оптимистически («ввод принят в очередь»): при отложенном применении синхронного success/fail нет. Сайты, ранее ветвившиеся по результату `Try*` (ctrl+клик депозит, rotate-in-if), переведены на семантику «действие поставлено в очередь → return», т.е. ctrl+клик по сущности больше не проваливается в move.
- Input-delay/tick-mapping зафиксированы в `DeferredCommandSink` (`DefaultInputDelayTicks = 1`); окончательная политика TPS/назначения тиков — за R32.
