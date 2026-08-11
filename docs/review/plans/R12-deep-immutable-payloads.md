# R12 — Сделать command payloads глубоко immutable

**Severity:** Medium · **Домен:** determinism / safety · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Payload'ы команд хранят caller-owned коллекции без глубокой копии. Вызывающий может изменить payload после валидации/enqueue и тем самым поменять будущий authoritative input.

## Где в коде
- `src/SteelConveyorWar.Core/Domain/ValueObjects.cs:71-77` — `BastionOrder.Waypoints` держит caller-owned `IReadOnlyList`.
- `src/SteelConveyorWar.Core/GameSimulation.cs:935-942` — Core сохраняет order без deep copy.
- `src/SteelConveyorWar.Core/Commands/SimulationCommands.cs:69-74` — `SetTrackAllocationCommand` сохраняет переданный dictionary.

## План фикса
1. В конструкторах команд/order'ов копировать входные коллекции в immutable-структуры (`ImmutableArray<T>` / `ImmutableDictionary<,>` или защищённые копии).
2. Убедиться, что публичные свойства возвращают immutable-представление (не ссылку на caller-коллекцию).
3. Пройтись по всем DTO с коллекциями (waypoints, allocations, weights, science packs) и применить единый паттерн.

## Тесты
- Тест: изменение исходной коллекции после создания команды не влияет на payload.
- Тест: применённая команда использует «замороженные» значения.

## Definition of Done
- Payload'ы команд/order'ов глубоко immutable.
- Есть тест «мутация источника после enqueue не влияет на команду».

## Связанные замечания
R5 (cast-mutable коллекции), R3, R11.

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **`BastionOrder` (`Domain/ValueObjects.cs`).** Позиционная запись переведена в non-positional `sealed record` с ручным конструктором (имена параметров `Kind`/`Target`/`Waypoints`/`WaypointIndex` сохранены — все позиционные и именованные call-site'ы, включая `Waypoints:` в SFML/тестах, компилируются без изменений). В конструкторе caller-owned `Waypoints` **глубоко копируются** в `ImmutableArray<TilePosition>` (`.ToImmutableArray()`, пусто при `null`). Публичное свойство `WaypointList` теперь возвращает этот снимок; тип свойства оставлен `IReadOnlyList<TilePosition>` (а не `ImmutableArray`), чтобы существующие `.Count`-обращения (`MovementSystem`, `SimulationStateHasher`, `GameSimulation`, `BastionOrderBarModel`) продолжали компилироваться — `ImmutableArray<T>.Count` доступен только через явную реализацию интерфейса. Свойства объявлены `{ get; init; }`, поэтому `unit.Order with { WaypointIndex = … }` (MovementSystem) продолжает работать.
- **`SetTrackAllocationCommand` (`Commands/SimulationCommands.cs`).** Тоже переведена в non-positional record с ручным конструктором (`Actor`/`Tick`/`Allocations` сохранены). `Allocations` **замораживаются** копией в `ImmutableDictionary<string,int>` с `StringComparer.Ordinal` (пустой словарь при `null`). Свойство осталось `IReadOnlyDictionary<string,int>`, поэтому сериализатор (`OrderBy/ToDictionary`) и `TrySetTrackAllocation` не тронуты.
- Deep-copy выполняется в момент конструирования, т.е. снимок фиксируется до любого enqueue — последующая мутация источника вызывающим не влияет на authoritative input.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/ImmutablePayloadTests.cs`: (1) мутация исходного `List<TilePosition>` после создания `BastionOrder` не меняет `WaypointList`; (2) `null`-waypoints → пустой снимок; (3) мутация источника **после `EnqueueForNextTick`** не меняет `command.Order.WaypointList`; (4) мутация исходного `Dictionary` после создания `SetTrackAllocationCommand` не меняет `Allocations`; (5) `null`-allocations → пустой снимок.

### Отклонения / заметки
- Прочие payload'ы с коллекциями отсутствуют: остальные research-/science-pack-интенты выражаются скалярами либо уже immutable value-объектами, так что «единый паттерн» (пункт плана 3) применён ровно к двум существующим коллекционным payload'ам (waypoints, allocations).
- `GameSimulation.cs` (сохранение order на юните) отдельной deep-copy не требует: `BastionOrder` теперь immutable по построению, копировать нечего.
- Тип свойств намеренно оставлен интерфейсным (`IReadOnlyList`/`IReadOnlyDictionary`), а не `ImmutableArray`/`ImmutableDictionary`, ради source-совместимости с `.Count`-обращениями и минимального диффа; backing-снимок при этом immutable.
