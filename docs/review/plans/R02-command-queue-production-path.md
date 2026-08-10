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
