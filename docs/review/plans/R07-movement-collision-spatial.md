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

---

## Статус реализации: ⏸️ Отложено 2026-08-11 (по решению пользователя)

Крупный, детерминизм-критичный performance-рефактор горячего пути. Отложено осознанно: DoD прямо требует бенчмарка (R22) для подтверждения severity/выигрыша **и** побитовой эквивалентности state-hash (behavior-preserving пути). Изменения затрагивают порядок обхода коллизий/threat-скана и A* (аллокации/пул) — любое расхождение в порядке или тай-брейках **тихо** сдвинет детерминированный хеш и сломает lockstep. VM недоступна: ни собрать, ни прогнать эквивалентность, ни отбенчить нельзя, поэтому реализация без верификации недопустимо рискованна.

**Что нужно для возврата:** доступный build/test + бенч-харнесс (R22). Порядок: (1) зафиксировать baseline state-hash и alloc/tick на 100/500/1000 юнитов; (2) ввести общий `ISpatialQueryService` (расширить combat spatial index) для collision+threat; (3) reusable A* workspace (пул `PriorityQueue`/словарей) + path-invalidation cache; (4) `GetEntitiesAt` без промежуточной аллокации; (5) на каждом шаге сверять идентичность state-hash с baseline и мерить p50/p95. Реализовывать только когда эквивалентность можно доказать прогоном.
