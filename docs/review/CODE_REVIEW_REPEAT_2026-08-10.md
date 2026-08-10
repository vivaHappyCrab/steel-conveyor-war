# Повторное ревью кода — Steel Conveyor War

**Дата:** 2026-08-10  
**Проверенный commit:** `912f541` (`develop`)  
**Связанный issue:** [#61 — Полный аудит архитектуры, производительности и расширяемости](https://github.com/vivaHappyCrab/steel-conveyor-war/issues/61)  
**Контекст:** повторная проверка после закрытия sub-issues #62–#83  
**Метод:** статический анализ Core/SFML/Client/Headless/config/tests + полная локальная верификация

---

## 1. Итог

Кодовая база стала заметно сильнее первоначального MVP:

- появился tick-stamped command model и JSON-сериализация;
- добавлены `SteelConveyorWar.Headless` и FoW-aware `IPlayerView`;
- появились id/tile и combat spatial indexes;
- исправлены самые дорогие варианты energy fill, FoW decay и combat target scans;
- SFML runner физически разделён на session/render/UI/input;
- build costs и условие поражения частично перенесены в конфигурацию;
- Core сохраняет правильное направление зависимостей и не зависит от SFML.

Но закрытие всех #62–#83 **не означает готовность к сетевому мультиплееру**. Главные остаточные проблемы:

1. production hosts продолжают вызывать immediate `Try*`, обходя command queue;
2. часть command DTO содержит `Actor`, но dispatcher его не проверяет;
3. FIFO command order зависит от порядка enqueue и не имеет canonical sequence;
4. fair bot view возвращает живые `WorldEntity`, поэтому сохранённая ссылка продолжает раскрывать скрывшуюся сущность;
5. `GameSimulation` остаётся одним partial god-object объёмом около **4297 строк**;
6. movement/collision/defend AI всё ещё содержит O(units × entities) проходы;
7. state hash не фиксирует queued commander orders и identity всех gameplay-каталогов;
8. malformed command может исключением оборвать tick;
9. command payload collections можно изменить после enqueue.

В проверенных production-host сценариях текущего локального MVP критических runtime-дефектов не обнаружено. High findings в command/hash/observation path являются блокерами lockstep/network и честного бота; maintainability/performance findings требуют benchmark перед окончательной оценкой severity.

### Оценка

| Критерий | Первая оценка | Повторная оценка | Комментарий |
|---|---:|---:|---|
| Архитектурные паттерны | 7.0 | **7.0** | Добавлены Command/Host/Observation patterns, но Core systems пока не отделены от `GameSimulation` |
| Производительность | 4.5 | **6.5** | Combat/FoW/energy улучшены; movement/pathfinding и factory accounting — кандидаты на следующий bottleneck |
| Расширяемость для сети | 5.0 | **5.5** | Появился prerequisite command layer, но production pipeline ещё не command-only |
| Расширяемость для ботов | 6.5 | **7.0** | Есть headless host и view, но fair view негерметичен и неполон |
| Разделение ответственности | 7.5 | **7.5** | Межпроектные границы хорошие; внутри Core/SFML крупные orchestration-объекты |
| **Итого** | **6.0** | **6.7** | Сильный прототип; до production MP требуется ещё один архитектурный этап |

---

## 2. Верификация и масштаб проверки

Проверено:

- **107** tracked C# source files;
- **211** Core tests;
- **20** SFML tests;
- Core-only headless smoke;
- SFML client smoke;
- Release build и format check.

Команда `pwsh eng/verify.ps1` не стартовала, потому что `pwsh` отсутствует в локальном `PATH`. Эквивалентный запуск Windows PowerShell прошёл:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
```

Результат:

- restore: pass;
- format: pass;
- Release build: pass, 0 warnings / 0 errors;
- Core tests: **211/211 pass**;
- SFML tests: **20/20 pass**;
- Headless smoke: pass;
- Client smoke: pass.

Дополнительный лёгкий smoke-throughput:

```text
10 000 ticks / 1.10 s ≈ 9 087 ticks/s
```

Это **не benchmark**: сценарий содержит только стартовый мир и stub AI, поэтому не доказывает производительность большой фабрики/армии.

---

## 3. Что исправлено после первого ревью

| ID | Статус повторного ревью | Доказательство / остаток |
|---|---|---|
| C1 Energy fill | **Частично исправлено** | Min-heap вместо re-sort: [`PowerSystem.cs:125–162`](../../src/SteelConveyorWar.Core/Systems/PowerSystem.cs#L125-L162). На распределённую единицу выполняется dequeue и обычно enqueue |
| C2 Command queue | **Частично исправлено** | DTO/queue/serializer существуют, но SFML и Headless работают через immediate `Try*` |
| H1 `GameSimulation` god-class | **Косметически/частично** | Код разнесён по partial files, но systems остаются nested wrappers над методами того же класса |
| H2 SFML god-runner | **В основном исправлено** | Runner стал тонким, но `SfmlPlaySession` = 1108 строк, `HudOverlay` = 901 строк |
| H3 Spatial index | **Частично исправлено** | id/tile index добавлен; continuous collision и defend threat всё ещё full scan |
| H4 Combat scans | **В основном исправлено** | `CombatSpatialIndex` ограничивает target/splash/LoS neighborhood |
| H5 Authoritative doubles | **Риск принят, не устранён** | ADR 0001 ограничивает lockstep одним runtime/ABI; movement `Sqrt` остаётся |
| H6 Ownership checks | **Частично исправлено** | Проверки есть на основных immediate APIs, но не на всех queued command handlers |
| H7 Hardcoded P1 | **Исправлено** | `SfmlDisplayOptions.LocalPlayerId` + Client CLI binding |
| M1 FoW repaint | **В основном исправлено** | Dirty-list decay; vision discs всё ещё рисуются каждый tick |
| M2 LINQ allocations | **Частично исправлено** | Scratch buffers добавлены; path/factory/research/world queries всё ещё аллоцируют |
| M3 Balance to config | **Частично по scope** | Build costs мигрированы; recipes/combat/stacks/power остаются в code |
| M4 Config TPS | **Исправлено** | Client передаёт TPS в `SfmlDisplayOptions` |
| M5 Cheat APIs | **Частично исправлено** | Основные helpers internal; `TryAddOutputItemToEntity` остался публичным |
| M6 Inventory mutators | **Исправлено** | Mutators стали assembly-internal |
| M7 Headless host | **Частично исправлено** | Host есть и проверяется в CI, но stub AI читает `World` и вызывает immediate API |
| M8 Fair observation | **Частично исправлено** | FoW gate есть, но возвращаются live entity references |
| L1 Presentation state | **Исправлено/задокументировано** | Sink и hash-exclusion tests |
| L2 Golden hash | **Осознанно отложено** | Решение документировано; dual-run остаётся gate |
| L3 Dead commanders | **Исправлено** | Удаляются после victory evaluation |
| L4 JSON behavior | **Частичный vertical slice** | `lossCondition` влияет на victory; остальное registry/metadata |
| L5 WindowSettings in Core | **Исправлено** | Host-only parsing в Client |

---

## 4. Каталог актуальных проблем

### High — command/observation/encapsulation

#### R1. `Actor` игнорируется частью queued commands

[`GameSimulation.Commands.cs:49–77`](../../src/SteelConveyorWar.Core/GameSimulation.Commands.cs#L49-L77) передаёт `Actor` для move/stop/rotate/research/factory/bastion/assembler, но игнорирует его для:

- queue/place build;
- queue demolish;
- collect/withdraw/deposit;
- typed deposit/withdraw;
- obsolete factory-bastion assignment.

Например, `QueueCommanderBuildCommand(Actor = P2, CommanderId = P1)` диспетчеризуется в `TryQueueCommanderBuild`, где actor отсутствует: [`GameSimulation.cs:244–271`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L244-L271).

Это реальный trust-boundary bypass для будущего сетевого host: знание foreign commander id достаточно, чтобы отправить команду от другого `Actor`. `AssignFactoryBastionCommand` является исключением без security impact: legacy handler всегда возвращает `false` ([`GameSimulation.cs:647–655`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L647-L655)).

**Рекомендация:** все entity-targeted handlers принимают actor; единый authorization layer перед dispatch; negative tests для **каждого** command kind.

#### R2. Production input path обходит command queue

Документация на queue прямо допускает legacy immediate APIs: [`GameSimulation.Commands.cs:5–8`](../../src/SteelConveyorWar.Core/GameSimulation.Commands.cs#L5-L8).

SFML вызывает `TryIssue*`/`TrySet*` непосредственно из event handlers, например:

- [`SfmlPlaySession.cs:202–212`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L202-L212);
- [`SfmlPlaySession.cs:744–816`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L744-L816);
- [`SfmlPlaySession.cs:889–918`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L889-L918).

Headless stub тоже вызывает immediate move: [`HeadlessHostRunner.cs:41–64`](../../src/SteelConveyorWar.Headless/HeadlessHostRunner.cs#L41-L64).

Следствия:

- local input не попадает в replayable command log;
- результат зависит от того, между какими ticks пришло оконное событие;
- queue и serializer не проверяются production host’ами;
- bot и human используют разные потенциальные timing paths.

**Рекомендация:** один `IPlayerCommandSink`, который для всех hosts только enqueue’ит commands на согласованный tick.

#### R3. FIFO queue не задаёт canonical cross-peer order

Команды одного tick применяются в порядке `_commandBuffer`: [`GameSimulation.Commands.cs:82–110`](../../src/SteelConveyorWar.Core/GameSimulation.Commands.cs#L82-L110). DTO содержит `Actor` и `Tick`, но не sequence/nonce: [`ISimulationCommand.cs:7–15`](../../src/SteelConveyorWar.Core/Commands/ISimulationCommand.cs#L7-L15).

Если два peer enqueue’ят один набор команд в разном network-arrival order, итог может различаться (например, конкурирующие inventory/build intents). Dual-run tests проверяют одинаковый порядок, а не перестановки.

**Рекомендация:** authoritative sequence либо deterministic sort key `(tick, actor, actorSequence)`; зафиксировать duplicate/idempotency policy.

#### R4. Fair `IPlayerView` можно обойти сохранённой live-ссылкой

`PlayerView` возвращает реальные `WorldEntity`: [`PlayerView.cs:55–73`](../../src/SteelConveyorWar.Core/Observation/PlayerView.cs#L55-L73).

Бот может:

1. получить enemy entity, пока tile visible;
2. сохранить ссылку;
3. после ухода enemy в FoW продолжать читать актуальные `Position`, `Health`, order/buffers из того же объекта.

То есть API фильтрует момент получения, но не обеспечивает честное наблюдение после этого. Также видимая enemy entity раскрывает весь внутренний state, а не ограниченный game-design snapshot.

**Рекомендация:** immutable `PlayerObservationSnapshot` на конкретный tick с DTO (`VisibleEntitySnapshot`, own economy/research snapshot), без выдачи `WorldEntity`.

#### R5. Публичные read-only коллекции фактически cast-mutable

- `GameWorld.Entities => _entities`: [`GameWorld.cs:5–22`](../../src/SteelConveyorWar.Core/World/GameWorld.cs#L5-L22);
- `GameSimulation.Players => _players`: [`GameSimulation.cs:83–85`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L83-L85);
- `PendingCommands => _commandBuffer`: [`GameSimulation.Commands.cs:12–15`](../../src/SteelConveyorWar.Core/GameSimulation.Commands.cs#L12-L15);
- `BuildCostCatalog` exposes `IReadOnlyDictionary`, за которым остаются реальные dictionaries: [`BuildCostCatalog.cs:6–10`](../../src/SteelConveyorWar.Core/Content/BuildCostCatalog.cs#L6-L10), [`BuildCostContentLoader.cs:29–63`](../../src/SteelConveyorWar.Core/Content/BuildCostContentLoader.cs#L29-L63);
- глобальные `HashSet`/dictionaries в `MvpDefinitions` публичны и изменяемы: [`MvpDefinitions.cs:22–37`](../../src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs#L22-L37), [`MvpDefinitions.cs:52–105`](../../src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs#L52-L105), [`MvpDefinitions.cs:199–233`](../../src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs#L199-L233).

Сигнатура `IReadOnlyList`/`IReadOnlyDictionary` не мешает внешнему коду сделать downcast к `List<>`/`Dictionary<>`. Особенно опасен `World.Entities`: прямой add/remove обойдёт `_byId` и `_occupancy`, рассинхронизировав индексы.

Текущий encapsulation test покрывает inventory/entity subcollections, но не эти root collections: [`EncapsulationTests.cs:41–50`](../../tests/SteelConveyorWar.Core.Tests/EncapsulationTests.cs#L41-L50).

**Рекомендация:** хранить и возвращать `ReadOnlyCollection`, immutable collections или snapshots; добавить cast-mutation tests.

### Medium — maintainability/performance

#### R6. `GameSimulation` остаётся god-object, разнесённым по partial files

Суммарный объём partial implementation — около **4297 строк**. Каждый system — private nested wrapper:

- [`PowerSystem.cs:3–18`](../../src/SteelConveyorWar.Core/Systems/PowerSystem.cs#L3-L18);
- [`MovementSystem.cs:3–13`](../../src/SteelConveyorWar.Core/Systems/MovementSystem.cs#L3-L13);
- [`CombatSystem.cs:3–15`](../../src/SteelConveyorWar.Core/Systems/CombatSystem.cs#L3-L15).

Wrapper вызывает приватный метод того же `GameSimulation`; системы не имеют самостоятельных contracts/dependencies и не тестируются изолированно. Файловая навигация улучшилась, архитектурная связанность почти не изменилась.

**Рекомендация:** выделять state/context interfaces и реальные system types по одному, начиная с Power/Combat; `GameSimulation` оставить composition/orchestration root.

#### R7. Movement/collision остаётся O(units × entities)

Каждый moving unit проходит все entities в collision check: [`MovementSystem.cs:324–385`](../../src/SteelConveyorWar.Core/Systems/MovementSystem.cs#L324-L385).

Defend units дополнительно вызывают full-scan threat search: [`MovementSystem.cs:60–76`](../../src/SteelConveyorWar.Core/Systems/MovementSystem.cs#L60-L76), [`FactoryBastionSystem.cs:485–517`](../../src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs#L485-L517).

Pathfinding на каждый новый маршрут создаёт `PriorityQueue` и два dictionaries: [`MovementSystem.cs:167–205`](../../src/SteelConveyorWar.Core/Systems/MovementSystem.cs#L167-L205). `GetEntitiesAt` создаёт и сортирует новый list: [`GameWorld.cs:68–98`](../../src/SteelConveyorWar.Core/World/GameWorld.cs#L68-L98).

Combat spatial index решает combat scan, но не movement/AI. Movement является главным **кандидатом** на следующий CPU/GC потолок; подтвердить это должен large-army benchmark.

**Рекомендация:** общий spatial-query service для nearby collision/threat; reusable A* workspace; path invalidation/cache; benchmark с 100/500/1000 units.

### High — multiplayer correctness

#### R8. State hash пропускает authoritative commander orders

`QueuedBuildOrder` и `QueuedDemolishOrder` определяют поведение будущих ticks: [`WorldEntity.cs:91–93`](../../src/SteelConveyorWar.Core/State/WorldEntity.cs#L91-L93), [`CommanderOrdersSystem.cs:19–65`](../../src/SteelConveyorWar.Core/Systems/CommanderOrdersSystem.cs#L19-L65).

Но `SimulationStateHasher.WriteEntity` пишет movement target/path и bastion order, не записывая обе commander queues: [`SimulationStateHasher.cs:93–174`](../../src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs#L93-L174).

Два состояния с одинаковым hash могут выполнить разные build/demolish действия на следующих ticks. Это прямой дефект desync detector.

**Рекомендация:** добавить оба order payload в versioned hash surface и regression test «разные queued orders → разные hashes».

#### R9. Malformed/untrusted command может уронить simulation tick

Deserializer принимает произвольный положительный/отрицательный actor id без roster validation: [`SimulationCommandSerializer.cs:164–168`](../../src/SteelConveyorWar.Core/Commands/SimulationCommandSerializer.cs#L164-L168). Research dispatch вызывает `GetPlayer(...).Single(...)`: [`GameSimulation.cs:171–174`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L171-L174), [`GameSimulation.Commands.cs:62–64`](../../src/SteelConveyorWar.Core/GameSimulation.Commands.cs#L62-L64).

Кроме того, `TrySetTrackAllocation` проверяет наличие обязательных tracks, но затем проходит и по лишним keys, обращаясь к `research.Tracks[pair.Key]`: [`ResearchSystem.cs:138–169`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L138-L169). Extra key с корректной общей суммой может дать `KeyNotFoundException`.

`ApplyQueuedCommandsForCurrentTick` не изолирует command exceptions: [`GameSimulation.Commands.cs:89–102`](../../src/SteelConveyorWar.Core/GameSimulation.Commands.cs#L89-L102). Для untrusted network input это denial-of-service path.

**Рекомендация:** envelope/schema validation до enqueue; roster check; no-throw command result; reject unknown allocation keys; fuzz/property tests.

#### R10. Нет content/session manifest hash

Hasher включает `ResearchCatalog.ContentHash`, но не identity `BuildCostCatalog` и `EntityCatalog`: [`SimulationStateHasher.cs:33–41`](../../src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs#L33-L41).

При этом:

- build costs влияют на команды/build rules;
- entity `lossCondition` влияет на victory;
- host/session settings (включая TPS) влияют на pacing и назначение input ticks, хотя не являются state самого `GameSimulation`.

Два peer с разными authoritative catalogs могут иметь одинаковый state hash до первого затронутого события, затем разойтись. `TileCatalog` пока почти не влияет на tick rules, но должен входить в общий versioned session manifest при расширении content behavior.

**Рекомендация:** versioned `SimulationContentManifestHash` в handshake/replay header и state fingerprint; fail-fast до tick 0.

### Medium — protocol/extensibility/testing

#### R11. Command wire shape ещё не protocol-grade

[`SimulationCommandSerializer.cs`](../../src/SteelConveyorWar.Core/Commands/SimulationCommandSerializer.cs):

- нет schema/protocol version;
- нет command id/sequence;
- для части пропущенных полей используются defaults (`Clockwise ?? true`, `Direction ?? East`);
- serializer round-trip tests покрывают только move и bastion order: [`CommandQueueTests.cs:48–71`](../../tests/SteelConveyorWar.Core.Tests/CommandQueueTests.cs#L48-L71);
- result/rejection command не логируется (`ApplyQueuedCommandsForCurrentTick` игнорирует `bool`).

JSON stub достаточен для research/prototype, но не для untrusted network input.

#### R12. Command payloads не являются глубоко immutable

`BastionOrder.Waypoints` хранит caller-owned `IReadOnlyList`: [`ValueObjects.cs:71–77`](../../src/SteelConveyorWar.Core/Domain/ValueObjects.cs#L71-L77), а Core сохраняет order без deep copy: [`GameSimulation.cs:935–942`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L935-L942). Аналогично `SetTrackAllocationCommand` сохраняет переданный dictionary: [`SimulationCommands.cs:69–74`](../../src/SteelConveyorWar.Core/Commands/SimulationCommands.cs#L69-L74).

Caller может изменить payload после validation/enqueue, изменив будущий authoritative input.

**Рекомендация:** копировать collections в immutable arrays/dictionaries при создании command/order.

#### R13. Pending command state не входит в hash

Hasher не пишет `_commandBuffer`. Два simulation snapshot с одинаковым state и разными будущими commands имеют одинаковый hash. Это допустимо только если command ledger гарантированно хранится/сравнивается отдельно; такого production ledger пока нет.

#### R14. Остался публичный item-injection API

`TryAddOutputItemToEntity` публично добавляет item без игрового источника или actor: [`GameSimulation.cs:1028–1039`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L1028-L1039).

Это противоречит задокументированной цели gating cheat helpers и опасно для plugin/bot host.

#### R15. Energy algorithm всё ещё зависит от количества произведённых единиц

Min-heap снизил стоимость с повторной сортировки примерно до `O(C log C + min(produced, deficit) log C)`, но цикл всё ещё распределяет **по одной единице**; каждая распределённая единица делает dequeue и, пока буфер не заполнен, enqueue: [`PowerSystem.cs:138–160`](../../src/SteelConveyorWar.Core/Systems/PowerSystem.cs#L138-L160).

Для текущих значений это нормально; при data-driven росте production понадобится batched water-filling.

#### R16. FoW и tech signatures остаются full-tick derived systems

Dirty-list decay исправлен, но каждый vision source каждый tick заново красит весь круг: [`FogOfWarSystem.cs:17–81`](../../src/SteelConveyorWar.Core/Systems/FogOfWarSystem.cs#L17-L81). Tech signatures каждый tick создают LINQ/GroupBy pipeline для каждого игрока: [`FogOfWarSystem.cs:84–105`](../../src/SteelConveyorWar.Core/Systems/FogOfWarSystem.cs#L84-L105).

Следующий шаг — dirty sources/regions и update only on entity move/build/death/research change.

#### R17. Factory/Bastion accounting многократно сканирует `World.Entities`

Deficit/supply/in-flight calculations выполняют вложенные `Count/Where/OrderBy` на idle tick: [`FactoryBastionSystem.cs:86–245`](../../src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs#L86-L245).

Дополнительно bastion processing повторно собирает units/orders: [`FactoryBastionSystem.cs:364–470`](../../src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs#L364-L470), а repair pass выполняет вложенный поиск units для каждого bastion: [`CombatSystem.cs:166–197`](../../src/SteelConveyorWar.Core/Systems/CombatSystem.cs#L166-L197).

При MVP limits это приемлемо, но плохо масштабируется с количеством bastions/factories.

#### R18. Data-driven extensibility остаётся ограниченной enum/switch моделью

`MvpDefinitions` продолжает владеть:

- unit/factory sets;
- power;
- stack sizes;
- footprints/collision;
- resistances;
- production recipes;
- combat stats.

См. [`MvpDefinitions.cs:22–267`](../../src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs#L22-L267).

`entities.json` всё ещё требует точного `EntityKind` enum: [`EntityContentLoader.cs:42–50`](../../src/SteelConveyorWar.Core/Content/EntityContentLoader.cs#L42-L50). Добавление нового entity требует code + config + renderer/tests; plugin/mod content без rebuild невозможен.

#### R19. Bot observation contract неполон

`IPlayerView` даёт terrain/visibility/entities, но не:

- observation tick;
- own inventory/power/research;
- available commands/capabilities;
- tech signatures;
- immutable event stream.

См. [`IPlayerView.cs:7–38`](../../src/SteelConveyorWar.Core/Observation/IPlayerView.cs#L7-L38). Product bot вынужден выходить в `GameSimulation.GetPlayer`/`World`, размывая fair boundary.

#### R20. Numeric policy ограничивает lockstep одним runtime/ABI

`WorldPosition` и movement normalization остаются `double` + `Sqrt`: [`ValueObjects.cs:39–66`](../../src/SteelConveyorWar.Core/Domain/ValueObjects.cs#L39-L66), [`MovementSystem.cs:121–149`](../../src/SteelConveyorWar.Core/Systems/MovementSystem.cs#L121-L149).

ADR 0001 корректно фиксирует ограничение, но документирование не устраняет cross-platform desync risk.

#### R21. SFML decomposition улучшена, но session/UI всё ещё крупные

- `SfmlGameRunner`: 15 строк — хороший composition facade;
- `SfmlPlaySession`: **1108** строк, один большой `Run` с event handlers/state/loop;
- `HudOverlay`: **901** строк.

Это лучше исходного 3592-line runner, но input command mapping и state transitions трудно unit-test’ить без окна.

#### R22. Нет performance regression gate

CI собирает coverage и запускает multi-OS build/tests/smoke, что является сильной стороной: [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml).

Но отсутствуют:

- benchmark project;
- threshold для tick time/allocations;
- large-world/large-army scenario;
- coverage threshold;
- command serializer/property-based tests для всех kinds.

### Low

#### R23. SFML fixed-step loop не ограничивает catch-up

`while (accumulator >= fixedDelta)` не имеет max ticks/frame: [`SfmlPlaySession.cs:872–925`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L872-L925). После длинной паузы возможна spiral-of-death. Нужен documented catch-up/drop policy, особенно перед network host.

#### R24. Invalid local player завершается через `First`

CLI проверяет положительный id, но session предполагает наличие commander и вызывает `First`: [`SfmlPlaySession.cs:38–40`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L38-L40). `--local-player 3` для default map завершится исключением без domain-friendly ошибки.

#### R25. Client и Headless дублируют config composition

Оба `Program.cs` повторяют path resolution, file I/O и catalog loading. При добавлении нового обязательного каталога легко обновить только один host. Стоит вынести общий host bootstrap без зависимости от SFML.

---

## 5. Архитектурные паттерны

### Сильные стороны

| Паттерн | Реализация | Оценка |
|---|---|---|
| Ports/adapters | Core не зависит от SFML; Client/Headless — composition roots | Хорошо |
| Fixed timestep | `AdvanceTick` + host pacing | Хорошо |
| Command pattern | DTO + tick + serializer + queue | Хорошая основа, не end-to-end |
| Deterministic simulation | ordered iteration, integer combat/path costs, state hash | Хорошая основа, но hash surface неполон (R8/R10) |
| Read model | Research snapshots, начало `IPlayerView` | Частично |
| Data-driven content | Research + bootstrap + build costs | Частично |
| Spatial indexing | world tile/id + transient combat index | Хорошо, неполно |
| CI quality gates | format/build/test/coverage/multi-OS/smoke | Сильно |

### Главная архитектурная проблема

Текущая форма:

```text
GameSimulation partial (state + commands + rules + system internals)
  ├─ private nested PowerSystem      -> sim.UpdatePower()
  ├─ private nested MovementSystem   -> sim.ProcessMovement()
  ├─ private nested CombatSystem     -> sim.ProcessCombat()
  └─ ...
```

Целевая форма:

```text
GameSimulation / SimulationKernel
  ├─ CommandScheduler
  ├─ IWorldState + SpatialQueries
  ├─ PowerSystem
  ├─ LogisticsSystem
  ├─ MovementSystem
  ├─ CombatSystem
  ├─ FogOfWarSystem
  └─ ObservationSnapshotBuilder
```

Системам не обязательны публичные interfaces «ради interfaces», но у них должны быть явные входы/выходы и возможность изолированной проверки.

---

## 6. Производительность

### Что улучшилось

1. `GameWorld.GetEntity` — dictionary lookup.
2. Tile occupancy — dictionary buckets.
3. Combat target/splash — neighborhood index.
4. Energy ratio — integer comparer + heap.
5. FoW decay — только previously visible tiles.
6. Hot logistics loops используют reusable scratch buffers.
7. Headless 10k-tick smoke показывает большой запас на стартовом мире.

### Кандидаты на следующий потолок

При большом числе units/buildings ожидаемый порядок:

1. continuous collision full scan;
2. per-unit defend threat scans;
3. repeated A* allocations/repath;
4. Factory/Bastion aggregate recounts;
5. FoW repaint for all vision sources;
6. per-unit energy heap loop при росте production.

### Нужный benchmark matrix

| Scenario | Entities | Что измерять |
|---|---:|---|
| Idle factory | 100 / 500 / 2000 buildings | tick p50/p95, alloc/tick |
| Belts | 100 / 1000 / 5000 belts | logistics time, alloc |
| Army move | 100 / 500 / 1000 units | movement/path/collision |
| Battle | 100v100 / 500v500 | target/index/combat |
| FoW | 100 / 1000 vision sources | FoW time |
| Power | 100 / 1000 consumers, high production | energy distribution |

До появления такого gate performance conclusions остаются статическими.

---

## 7. Сетевой мультиплеер

### Готово как foundation

- deterministic tick;
- command DTO/serialization;
- actor field;
- queue at tick boundary;
- state hash foundation (authoritative surface пока неполон);
- ownership checks на части commands;
- Core-only host;
- multi-OS CI.

### Блокеры

1. нет command-only production path;
2. actor bypass у части command handlers;
3. нет canonical command order / idempotency;
4. queued commander orders отсутствуют в state hash;
5. malformed command может исключением оборвать tick;
6. command collections не deep-immutable после enqueue;
7. нет content manifest hash;
8. нет transport/session/authority/handshake;
9. нет save/replay ledger;
10. pending queue не входит в hash и не сравнивается отдельно;
11. authoritative FP ограничивает mixed-platform lockstep;
12. public/castable state surfaces слишком широкие для untrusted adapters.

**Вывод:** Core готов к следующему этапу исследования lockstep, но не к заявлению «network-ready».

---

## 8. Боты

### Что уже можно

- запускать симуляцию без SFML;
- выбирать PlayerId;
- получать FoW-filtered entities/terrain;
- отправлять существующие gameplay intents;
- проверять deterministic hash.

### Чего не хватает

1. immutable per-tick observation snapshot;
2. own economy/research/capability view;
3. queue-only command controller;
4. deterministic bot cadence/seed contract;
5. scenario tests «бот достигает цели за tick budget»;
6. защита от retained live references;
7. отдельный AI module (сейчас Headless содержит только stub).

**Вывод:** инфраструктура бота появилась, но честный продуктовый bot API ещё не сформирован.

---

## 9. Разделение ответственности

### Хорошо

- Core rules/headless determinism отделены от SFML.
- Client владеет display bootstrap.
- Headless не ссылается на SFML.
- Core парсит/валидирует simulation content; hosts выполняют I/O.
- Test/debug helpers в основном internal.
- Presentation side channels явно исключены из hash.

### Требует доработки

- `GameSimulation` одновременно state owner, command dispatcher и implementation container всех systems.
- `WorldEntity` остаётся fat state bag для logistics/combat/movement/build/bastion.
- SFML читает live `GameWorld`/`PlayerState`, а не UI snapshot.
- `IPlayerView` выдаёт domain objects вместо observation DTO.
- два hosts дублируют config bootstrap.
- content behavior одновременно живёт в catalogs, enums и `MvpDefinitions`.

---

## 10. Приоритетный roadmap

### P0 — correctness / trust

1. Actor authorization для **всех** command kinds.
2. Перевести SFML и Headless на queue-only command sink.
3. Canonical `(tick, actor, actorSequence)` order + duplicate policy.
4. Сделать command payloads глубоко immutable.
5. Валидировать untrusted envelopes без исключений из tick loop.
6. Добавить queued commander orders в state hash.
7. Убрать cast-mutable root collections.
8. Закрыть `TryAddOutputItemToEntity`.
9. Добавить content/session manifest hash.

### P1 — bots / scale

1. Immutable `PlayerObservationSnapshot`.
2. Own player economy/research view.
3. Shared spatial query service для movement/threat/collision.
4. Reusable A* workspace/path cache.
5. Benchmark project и regression scenarios.

### P2 — maintainability / extensibility

1. Реально выделять systems из partial `GameSimulation`.
2. Разделить `SfmlPlaySession` input/session state.
3. Общий host bootstrap для Client/Headless.
4. Мигрировать recipes/combat/stacks/power/footprints в versioned content catalogs.
5. Fixed-point world coordinates перед heterogeneous lockstep.

---

## 11. Финальный вывод

Повторный аудит подтверждает, что remediation wave после #61 была полезной: наиболее очевидные perf bottlenecks и layer violations исправлены или существенно смягчены. Репозиторий теперь имеет реальную основу для headless simulation, command logging и AI experiments.

При этом несколько закрытых issue реализовали **минимальный vertical slice**, а не конечную архитектуру. Самая важная корректировка ожиданий:

> Наличие command queue, state hash и headless host ещё не делает игру готовой к сети; production hosts, authorization, ordering, content identity и immutable observations должны образовать один непрерывный authoritative pipeline.

Текущий статус: **хороший deterministic MVP prototype, готовый к следующему архитектурному этапу; не production-ready для сетевого multiplayer**.
