# R07 — Убрать O(units × entities) в movement/collision

**Severity:** Medium (performance) · **Домен:** performance · **Roadmap:** P1
**Статус валидации:** ✅ Подтверждено по коду (severity — после benchmark)

## Проблема
Каждый moving unit проходит все entities в collision-check; defend-юниты дополнительно выполняют full-scan threat search; pathfinding на каждый новый маршрут создаёт `PriorityQueue` и два словаря; `GetEntitiesAt` создаёт и сортирует новый список. Combat уже использует spatial index, а movement/AI — нет. Это главный кандидат на следующий CPU/GC-потолок при большой армии.

## Где в коде
- `src/SteelConveyorWar.Core/Systems/MovementSystem.cs:324-385` — collision-scan по всем entities.
- `src/SteelConveyorWar.Core/Systems/MovementSystem.cs:60-76` + `FactoryBastionSystem.cs:485-517` — defend threat full scan.
- `src/SteelConveyorWar.Core/Systems/MovementSystem.cs:167-205` — A*: аллокация `PriorityQueue` + 2 словаря на маршрут.
- `src/SteelConveyorWar.Core/World/GameWorld.cs:68-98` — `GetEntitiesAt` создаёт и сортирует новый list.

## План фикса
1. Ввести общий `ISpatialQueryService` (радиусные/тайловые запросы соседей), переиспользуемый collision- и threat-поиском (расширить существующий combat spatial index до общего).
2. A*: reusable workspace (пул `PriorityQueue`/словарей/буферов), очищаемый между запросами вместо переаллокации.
3. Path invalidation/cache: не перестраивать маршрут, если цель/препятствия не изменились.
4. `GetEntitiesAt` — по возможности отдавать без промежуточной аллокации (буфер/`struct`-enumerator) на горячем пути.
5. Обязательно: сначала benchmark (см. R22), чтобы подтвердить severity и измерить эффект.

## Тесты
- Behavior-preserving: тот же state hash до/после (детерминизм путей сохраняется).
- Benchmark 100/500/1000 units: движение/путь/коллизии — фиксировать p50/p95 и alloc/tick.

## Definition of Done
- Общий spatial-query сервис используется movement и threat-поиском.
- A* не аллоцирует на каждый маршрут.
- Benchmark показывает улучшение без изменения детерминизма.

## Связанные замечания
R6 (spatial как часть context), R17 (factory/bastion scans), R22 (benchmark gate).
