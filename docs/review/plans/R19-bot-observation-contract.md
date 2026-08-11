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

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **`IPlayerView` расширен** (`Observation/IPlayerView.cs`): добавлены `long ObservationTick`, `GetOwnEconomy()`, `GetOwnResearch()`, `GetTechSignatures()`, `GetAvailableCommandKinds()`, `GetEventsThisTick()`. Все возвращают immutable snapshot-DTO, `WorldEntity`/live state наружу не выдаётся.
- **Новые DTO** (`Observation/ObservationSnapshots.cs`): `OwnEconomySnapshot` (агрегатный player-inventory как defensive-copy, power produced/demand, defeated), `OwnResearchSnapshot` (tier, completed tech, progress, треки, applied capabilities / unlocked entity kinds/recipes/item recipes, milestones) + `TrackObservation`, `TechSignatureObservation` (только зона 8×8 + intensity, без точных позиций), `ObservedCombatEvent` (presentation-only, отфильтрован по видимости). `PlayerObservationSnapshot` дополнен полями own economy/research, tech signatures, command vocabulary и event stream — снапшот самодостаточен.
- **`PlayerView`** (`Observation/PlayerView.cs`): реализация всех новых членов. `GetOwnEconomy/GetOwnResearch` берут только `GetPlayer(ObserverId)` (свой игрок — чужую экономику/tech-дерево прочитать нельзя, отдельного канала нет). `GetEventsThisTick` фильтрует `CombatShotsThisTick` через тот же fair-visibility gate, что SFML использует для трейсеров (R27): endpoint-entity видима ИЛИ endpoint-тайл `Visible`; в Cheat — всё. `GetAvailableCommandKinds` возвращает полный `SimulationCommandKind`-словарь (authority по-прежнему проверяется per-command на apply).
- **Headless stub мигрирован** (`Headless/HeadlessHostRunner.cs`): `TryApplyStubAiStep` теперь принимает `IPlayerView` и читает своего командира и его idle-состояние (MoveTarget/QueuedBuild/QueuedDemolish) **исключительно** через `GetVisibleEntities()`/`OwnEntityDetail`, а границы мира — через `view.WorldSize`. Обращения к `simulation.World`/`GetPlayer` из стаба убраны (fair boundary больше не обходится). `Run` создаёт `CreatePlayerView(playerId, Fair)` один раз до цикла.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/BotObservationContractTests.cs`: own economy tick-stamped и defensive-copy; own research (tier/tracks) как immutable snapshot; command vocabulary покрывает весь enum; event stream пуст на свежей игре и fair ⊆ cheat на 20 тиках; tech signatures несут только зоны; `CaptureSnapshot` самодостаточен; fair-контракт отдаёт всё, что нужно стабу, без доступа к World; own economy не течёт между игроками.

### Отклонения / заметки
- **Все добавления read-only и presentation-уровня** — hashed authoritative state не тронут, поэтому детерминизм не меняется (state hash тот же).
- **Поведенческий тест мигрированного Headless-стаба через отдельный Headless.Tests не добавлен** — `Core.Tests` ссылается только на `Core`, не на `Headless`. Вместо этого достаточность контракта проверена на уровне `IPlayerView` (тест `FairContract_ExposesEverythingTheBotStubNeeds_WithoutWorldAccess`), что и есть DoD «контракт самодостаточен для честного бота».
- **Правило видимости выстрелов воспроизведено в Core** (в `PlayerView`), а не переиспользовано из `Sfml.WorldRenderer.IsShotVisibleToLocalPlayer` — чтобы Core не зависел от Sfml; семантика идентична (owner-always / endpoint-tile Visible).
