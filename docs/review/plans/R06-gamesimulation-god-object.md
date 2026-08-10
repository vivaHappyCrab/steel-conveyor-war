# R06 — Выделить системы из god-object GameSimulation

**Severity:** Medium · **Домен:** maintainability / architecture · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
`GameSimulation` разнесён по partial-файлам (суммарно ~4297 строк), но каждый «system» — это приватная вложенная обёртка, вызывающая приватный метод того же класса. Системы не имеют самостоятельных контрактов/зависимостей и не тестируются изолированно. Файловая навигация улучшилась, архитектурная связанность — нет.

## Где в коде
- `src/SteelConveyorWar.Core/Systems/PowerSystem.cs:3-18`, `MovementSystem.cs:3-13`, `CombatSystem.cs:3-15` — вложенные обёртки.
- Образец паттерна: `src/SteelConveyorWar.Core/Systems/VictorySystem.cs:8-14` — `private static class VictorySystem { Tick(sim) => sim.CheckVictory(); }`.
- Все `GameSimulation*.cs` — partial-реализация одного класса.

## План фикса
1. Определить state/context-интерфейсы: `IWorldState`, `ISpatialQueries`, доступ к игрокам/каталогам — то, что нужно системам, без полного `GameSimulation`.
2. Выносить системы по одной в самостоятельные типы с явными входами/выходами, начиная с Power и Combat (наиболее замкнутые).
3. Перенести приватные методы-реализации в соответствующий system-тип; `GameSimulation` оставить composition/orchestration root, который создаёт системы и вызывает их `Tick(context)`.
4. Добавить изолированные unit-тесты на выделенные системы (без создания всей симуляции).
5. Повторять итеративно для Movement/Logistics/FoW/Research.

## Тесты
- Юнит-тесты каждой выделенной системы на in-memory context.
- Регрессия: полный state hash тика не меняется после рефакторинга (behavior-preserving).

## Definition of Done
- Минимум Power и Combat вынесены в самостоятельные типы с тестами.
- `GameSimulation` не содержит реализацию этих систем, только оркестрацию.

## Связанные замечания
R7 (общий spatial-query service как часть context), R21 (аналогичная декомпозиция в SFML).
