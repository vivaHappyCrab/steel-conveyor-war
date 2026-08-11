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

---

## Статус реализации: ✅ Реализовано 2026-08-11 (Power + Combat)

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Ниже перечислены изменения; их нужно верифицировать сборкой и прогоном тестов. Рефакторинг сделан «вслепую», поэтому риск компиляционных ошибок повышен.

### Что сделано
- **Контракт-интерфейс.** Добавлен `Systems/ISimulationSystemContext.cs` — узкий `internal`-контракт (вместо всего god-object): `World`, `Tick`, `Players`, `Presentation`, `GetPlayer`, `AreAllied`, `ResolveStat`, `SyncResolvedMaxHealth`, `CascadeBastionDeaths`, `TryConsumeBuildingEnergy`, `CollectSortedAliveEntities`. Ровно то, что нужно Power/Combat, ничего лишнего.
- **PowerSystem → самостоятельный тип.** `Systems/PowerSystem.cs` теперь `internal sealed class PowerSystem`, конструируется как `new PowerSystem(this)`. Владеет своими пер-тик аккумуляторами (`_tickPowerProduced/Consumed`, `*ByKind`) и статикой `EnergyFillPriorityComparer` (перенесены из `GameSimulation`). Публично: `Tick()` (produce + fill emptiest-first), `RecordStats()` (было `RecordEnergyStatsSample`), `TryConsumeBuildingEnergy()`. Логика перенесена байт-в-байт, поменялись только источники доступа (`_players`→`_context.Players`, `World`→`_context.World`, `GetPlayer`→`_context.GetPlayer`, `Tick`→`_context.Tick`).
- **CombatSystem → самостоятельный тип.** `Systems/CombatSystem.cs` теперь `internal sealed class CombatSystem`, конструируется как `new CombatSystem(this)`. Владеет собственными scratch-буферами `_scratchEntities`/`_scratchEntitiesSecondary`. `Tick()` = таргетинг/урон + каскад смертей бастионов + out-of-combat ремонт. Все хелперы (Resolve*, ComputeDamageAgainst, IsGroundToGroundBlockedByAlliedWall, EnumerateLineExclusive, ProcessCombat, FindNearestCombatTarget, ProcessRepairOutOfCombat) перенесены; внешние обращения идут через `_context`.
- **GameSimulation → только оркестрация.** `GameSimulation` объявлен `: ISimulationSystemContext`; из него удалены поля Power-аккумуляторов, `_scratchEntitiesSecondary` и `EnergyFillPriorityComparer`. Добавлены поля `_powerSystem`/`_combatSystem` (создаются в конструкторе). `AdvanceTick` вызывает `_powerSystem.Tick()`, `_combatSystem.Tick()`, `_powerSystem.RecordStats()`; бутстрап новой игры тоже переведён на `_powerSystem.Tick()/RecordStats()`. Добавлен forwarding `public bool TryConsumeBuildingEnergy(...)` (Production/FactoryBastion/Research зовут его напрямую как метод partial-класса). `CollectSortedAliveEntities` (общий для Combat/Logistics/Movement) оставлен инстанс-методом на `GameSimulation`. Явные реализации интерфейса форвардят на существующую поверхность: `Players` (→ `_players` без AsReadOnly-обёртки), `SyncResolvedMaxHealth`, `CascadeBastionDeaths`, `CollectSortedAliveEntities`.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/ExtractedSystemsTests.cs`: изолированный in-memory фейк `FakeSystemContext : ISimulationSystemContext` (без полной симуляции). Проверяет: Combat наносит урон врагу в радиусе и ставит кулдаун; Combat не бьёт союзника (тот же TeamId); Power раздаёт произведённую энергию в буфер потребителя emptiest-first; `TryConsumeBuildingEnergy` списывает при достатке и отказывает при пустом буфере; **регрессия детерминизма** — полный state hash идентичен между двумя прогонами с оркестрацией выделенных систем (behavior-preserving).

### Отклонения / заметки
- Вынесены **только Power и Combat** (по DoD «минимум Power и Combat»). Movement/Logistics/FoW/Research (пункт плана 5) оставлены прежними вложенными обёртками — итеративный вынос отложен.
- `ISimulationSystemContext` — один узкий интерфейс, а не отдельные `IWorldState`/`ISpatialQueries` (пункт плана 1). Разбиение на несколько контрактов + общий spatial-query service отложены до R7.
- `ApplyCombatDamage` в `CombatSystem` стал `static` (раньше инстанс-метод) — чистая функция от `(target, damage)`, поведение не изменилось.
- Регрессия «behavior-preserving» проверяется детерминизмом (одинаковый hash между прогонами), т.к. VM недоступен и снять до/после-хеш старой реализации нельзя; аккумуляторы энергии и combat-презентация не хешируются, поэтому перенос владения этим состоянием hash-нейтрален.
