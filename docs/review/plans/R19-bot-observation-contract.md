# R19 — Полный observation-контракт для ботов

**Severity:** Medium · **Домен:** bots / extensibility · **Roadmap:** P1
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
`IPlayerView` даёт terrain/visibility/entities, но не даёт observation tick, собственную экономику/power/research, доступные команды/capabilities, tech signatures и immutable event stream. Продуктовый бот вынужден выходить в `GameSimulation.GetPlayer`/`World`, размывая fair boundary.

## Где в коде
- `src/SteelConveyorWar.Core/Observation/IPlayerView.cs:7-38` — только `ObserverId`, `Mode`, `WorldSize`, `GetVisibility`, `TryGetTerrain`, `GetVisibleEntities`, `GetVisibleEntity`, `IsEntityVisible`.

## План фикса
1. Расширить контракт наблюдения (совместно с R04 snapshot):
   - `long ObservationTick`;
   - own inventory/power/research snapshot;
   - список доступных команд/capabilities для actor;
   - tech signatures (в допустимом объёме);
   - immutable event stream за тик (см. R27 для фильтрации presentation-событий).
2. Все данные — immutable snapshot-DTO, без выдачи `WorldEntity`/live state.
3. Перенести Headless stub на этот контракт (не читать `World`/`GetPlayer`).

## Тесты
- Тест: бот получает всю нужную информацию только из `IPlayerView`, без доступа к `GameSimulation`/`World`.
- Тест: own economy/research присутствуют и корректны; чужие — ограничены.

## Definition of Done
- Контракт наблюдения самодостаточен для честного бота.
- Headless stub не обходит fair boundary.

## Связанные замечания
R04 (snapshot), R27 (event filtering), R2 (queue-only sink для бота).
