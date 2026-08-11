# R05 — Убрать cast-mutable root-коллекции

**Severity:** High · **Домен:** encapsulation / determinism · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду
**Статус реализации:** ✅ Реализовано 2026-08-11 (`GameWorld.cs`, `GameSimulation.cs`, `GameSimulation.Commands.cs`, `MvpDefinitions.cs`, `BuildCostContentLoader.cs`; тесты в `EncapsulationTests.cs`). ⚠️ Требуется прогон `dotnet build -c Release` + `dotnet test`.

### Что сделано
- Root-коллекции завёрнуты в `ReadOnlyCollection` через `.AsReadOnly()`: `GameWorld.Entities`, `GameSimulation.Players`, `GameSimulation.PendingCommands` — downcast к `List<>` + add/remove теперь невозможен (защищает индексы `_byId`/`_occupancy` и буфер команд).
- `MvpDefinitions`: `UnitKinds`/`FactoryKinds` переведены с публичного `HashSet<EntityKind>` на `FrozenSet<EntityKind>`; словари `PowerDemand`, `PowerProduction`, `ItemStackSizes`, `ProductionRecipes`, `ItemRecipes`, `TechSignatureIntensity` завёрнуты `.AsReadOnly()`.
- `BuildCostContentLoader`: top-level `Costs`/`BuildTicks`/`Requirements` отдаются как `ReadOnlyDictionary`.
- Тесты: `RootCollections_AreNotCastMutable`, `ContentTables_AreNotCastMutable` (downcast + мутация → `NotSupportedException`, тип не `List`/`HashSet`).

### Отклонения от исходного плана
- Глубокая иммутабельность вложенных payload-ов (например, `Costs[kind]` — внутренний `IReadOnlyDictionary<ItemId,int>`) относится к R12 (deep-immutable) и здесь не покрывается; закрыт корневой уровень каждой публичной коллекции/таблицы.
- `FrozenDictionary` применён только к сетам; словари оставлены как `ReadOnlyDictionary` (достаточно против cast-мутации, без изменения объявленных типов свойств).

## Проблема
Публичные свойства объявлены как `IReadOnlyList`/`IReadOnlyDictionary`, но за ними стоят реальные `List<>`/`Dictionary<>`. Внешний код может сделать downcast и мутировать коллекцию. Особенно опасен `GameWorld.Entities`: прямой add/remove обойдёт индексы `_byId` и `_occupancy` и рассинхронизирует мир.

## Где в коде
- `src/SteelConveyorWar.Core/World/GameWorld.cs:6,22` — `private readonly List<WorldEntity> _entities`; `public IReadOnlyList<WorldEntity> Entities => _entities;` (downcast к `List<>` возможен; add/remove минует `_byId`/`_occupancy`).
- `src/SteelConveyorWar.Core/GameSimulation.cs:83-85` — `Players => _players`.
- `src/SteelConveyorWar.Core/GameSimulation.Commands.cs:15` — `PendingCommands => _commandBuffer`.
- `src/SteelConveyorWar.Core/Content/BuildCostCatalog.cs` + `BuildCostContentLoader.cs:29-63` — `IReadOnlyDictionary` над реальными словарями.
- `src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs:22-37,52-105,199-233` — публичные изменяемые `HashSet`/`Dictionary`.
- `Inventory.Items` уже защищён `_items.AsReadOnly()` (`Logistics/Inventory.cs:7`) — образец правильного подхода.
- Текущий `EncapsulationTests.cs:41-50` покрывает subcollections инвентаря/сущностей, но не эти root-коллекции.

## План фикса
1. Для каждого свойства возвращать защищённую обёртку: `ReadOnlyCollection<T>` (`list.AsReadOnly()`) или `_dict.AsReadOnly()`, либо immutable-коллекции.
2. Для `MvpDefinitions` заменить публичные mutable-таблицы на `FrozenDictionary`/`FrozenSet` (или `IReadOnlyDictionary` над frozen).
3. `PendingCommands` — возвращать snapshot/ReadOnly, чтобы буфер нельзя было менять извне.
4. Добавить cast-mutation тесты: попытка `((List<T>)prop).Add(...)` должна падать/не влиять.

## Тесты
- Тест на каждую root-коллекцию: downcast + мутация невозможна (бросает или не меняет источник).
- Регрессия: индексы `GameWorld` остаются согласованными.

## Definition of Done
- Ни одну публичную коллекцию нельзя мутировать через downcast.
- `EncapsulationTests` расширены на root collections/tables.

## Связанные замечания
R12 (deep-immutable payloads), R14 (public mutation hooks), R18 (frozen content).
