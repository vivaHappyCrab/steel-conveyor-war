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
