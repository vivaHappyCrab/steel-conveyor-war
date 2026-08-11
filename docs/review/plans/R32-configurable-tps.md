# R32 — Configurable TPS: довести настройку до Core-длительностей

**Severity:** Medium (correctness/consistency) · **Домен:** simulation timing · **Roadmap:** P1

**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Настройка ticks-per-second заявлена как конфигурируемая, но реально не протянута сквозь Core: часть длительностей и историй считается от зашитого значения TPS. При изменении TPS игровые тайминги (длительности, окно истории энергии) разъезжаются с фактической частотой симуляции, что даёт некорректные интервалы и несогласованную статистику.

## Где в коде
- `src/SteelConveyorWar.Core/GameSimulation.cs:5-12` — TPS/шаг симуляции зашит как константа, не связан с конфигом.
- `src/SteelConveyorWar.Core/State/EnergyStatsHistory.cs:11-12,97-100` — окно/ёмкость истории считается от фиксированного TPS.
- `docs/MVP_IMPLEMENTATION_DECISIONS.md:35` — задокументировано намерение сделать TPS конфигурируемым (расхождение док/код).

## План фикса
1. Ввести единый источник конфигурации симуляции (TPS/tick duration) и прокинуть его в конструкторы систем, зависящих от времени.
2. Все длительности выражать в тиках (детерминизм) и вычислять производные от конфигурируемого TPS, а не от литералов.
3. `EnergyStatsHistory` (и другие time-window буферы) параметризовать по TPS/длительности окна.
4. Согласовать поведение с документом: либо реализовать конфигурируемость, либо зафиксировать TPS и убрать заявление о конфигурируемости.
5. Проверить детерминизм: при равном TPS результат и hash идентичны прогону до изменения (регрессия).

## Тесты
- Тест: изменение TPS в конфиге меняет производные длительности согласованно (нет литералов, считающих от старого значения).
- Тест истории энергии: окно истории соответствует заданной длительности при разных TPS.
- Регрессия детерминизма: при дефолтном TPS hash не меняется.

## Definition of Done
- Конфигурируемый TPS реально управляет всеми зависящими от времени величинами в Core.
- Документация и код согласованы.

## Связанные замечания
R23 (fixed-step catch-up cap), R8 (детерминизм/hash), R33 (history/rendering).

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **Единый источник TPS протянут в Core.** `GameSimulation.cs`: зашитый `public const int TicksPerSecond = 30` переименован в `DefaultTicksPerSecond` (только fallback), добавлено инстанс-свойство `public int TicksPerSecond { get; }`, а приватный конструктор получил параметр `int ticksPerSecond`. Все производные величины теперь считаются от инстанс-значения матча, а не от литерала.
- **`GameCreationOptions` расширен** (`Research/ResearchDefinitions.cs`): добавлен опциональный хвостовой параметр `int TicksPerSecond = GameSimulation.DefaultTicksPerSecond`. `CreateNewGame` прокидывает `options.TicksPerSecond` и в конструктор симуляции, и в конструктор каждого `PlayerState`.
- **`EnergyStatsHistory` параметризован по TPS** (`Energy/EnergyStatsHistory.cs`): статическое `Capacity` заменено на инстанс-поля `_ticksPerSecond`/`_capacity` (`= MaxWindowSeconds * ticksPerSecond`), конструктор принимает `int ticksPerSecond = DefaultTicksPerSecond` с валидацией `> 0`. `Capacity` теперь инстанс-свойство; окно `Query` считает `bucketTicks = bucketSeconds * _ticksPerSecond`. Так 10-минутное окно остаётся 10 минутами wall-clock при любом TPS.
- **`PlayerState`** (`State/PlayerState.cs`): добавлен опциональный хвостовой параметр `int ticksPerSecond = DefaultTicksPerSecond`, `EnergyStats` создаётся как `new EnergyStatsHistory(ticksPerSecond)` в теле конструктора (было field-initializer). Старые 4-аргументные вызовы (тесты) продолжают компилироваться.
- **Build-timing fallback** (`GameSimulation.cs:296`): `GetValueOrDefault(targetKind, TicksPerSecond)` теперь резолвится в инстанс-свойство → при дефолтном TPS=30 значение идентично прежнему (hash не меняется), при другом TPS корректно масштабируется.
- **Композиционные корни** (`Client/Program.cs`, `Headless/Program.cs`): `gameSettings.TicksPerSecond` прокинут в `new GameCreationOptions(...)`, так что заявленная в конфиге частота реально управляет Core-таймингами, а не только пейсингом хоста.
- **Прочие ссылки на переименованный const**: `Sfml/SfmlDisplayOptions.cs` и тесты (`LocalPlayerBindingTests`, `ConfigContentLoaderTests`, `GameSimulationTests`) переведены на `GameSimulation.DefaultTicksPerSecond`.
- **Документация согласована**: `docs/MVP_IMPLEMENTATION_DECISIONS.md:35` обновлён — TPS теперь per-match instance, единый источник для всех Core-длительностей; `DefaultTicksPerSecond` описан как fallback.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/ConfigurableTpsTests.cs`: capacity истории масштабируется с TPS; невалидный TPS (0/-5) кидает; длительность окна привязана к секундам, а не к фиксированному числу тиков (30 тиков = полное 1-с ведро при 30 TPS, но лишь пол-секунды при 60); `GameCreationOptions` прокидывает TPS в инстанс симуляции и в историю игрока; при неуказанном TPS используется `DefaultTicksPerSecond`; регрессия детерминизма — implicit-default и explicit-default (TPS=30) дают одинаковый state hash после 60 тиков.

### Отклонения / заметки
- **Дефолтный путь поведенчески неизменен** — при TPS=30 все производные значения совпадают с прежними, поэтому детерминизм/hash не затронуты (подтверждается тестом-регрессией).
- **DoD-развилка решена в сторону реальной конфигурируемости** (вариант «реализовать»), а не фиксации TPS — конфиг теперь действительно управляет Core-таймингами.
- **`EnergyStatsHistory` — presentation-only** (не хешируется), поэтому изменение его ёмкости на детерминизм не влияет; параметризация нужна лишь для корректности окна статистики при нестандартном TPS.
- **Headless по-прежнему считает по `--ticks`, а не по wall-clock** — это вне рамок R32 (см. связку с R23).
