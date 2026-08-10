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
9. command payload collections можно изменить после enqueue;
10. SFML позволяет управлять research выбранной enemy laboratory;
11. selection и combat tracers раскрывают live state через FoW;
12. research content validation пропускает отрицательные science costs и неизвестный target tier;
13. research pack consumption теряет project identity;
14. integer rounding может фактически игнорировать research allocations/weights.

В smoke-путях критического падения не обнаружено, но статический аудит нашёл High gameplay correctness defects, достижимые в текущем Client, и блокеры lockstep/network/честного бота. Maintainability/performance findings требуют benchmark перед окончательной оценкой severity.

### Оценка

| Критерий | Первая оценка | Повторная оценка | Комментарий |
|---|---:|---:|---|
| Архитектурные паттерны | 7.0 | **6.0** | Границы проектов хорошие; systems/command/observation contracts не образуют герметичный pipeline |
| Производительность | 4.5 | **6.0** | Combat/FoW/energy улучшены; movement/factory/FoW allocations не защищены benchmark gate |
| Расширяемость для сети | 5.0 | **4.0** | Queue — только infrastructure; authorization/hash/order/host path неполны |
| Расширяемость для ботов | 6.5 | **6.0** | Headless/view есть, но production bot обходит fair view, а live references нарушают fairness |
| Разделение ответственности | 7.5 | **6.0** | Межпроектные границы хорошие; Core/SFML orchestration и authority surfaces слишком широки |
| **Итого** | **6.0** | **5.6** | Глубокая повторная проверка выявила correctness gaps, не покрытые закрытыми issue |

Отдельный subscore data-driven/content extensibility: **4.0/10**.

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
| H2 SFML god-runner | **Частично исправлено** | Runner стал тонким, но `SfmlPlaySession` = 1108 строк; authority/FoW/input defects остались в session/HUD |
| H3 Spatial index | **Частично исправлено** | id/tile index добавлен; continuous collision и defend threat всё ещё full scan |
| H4 Combat scans | **В основном исправлено** | `CombatSpatialIndex` ограничивает target/splash/LoS neighborhood |
| H5 Authoritative doubles | **Риск принят, не устранён** | ADR 0001 ограничивает lockstep одним runtime/ABI; movement `Sqrt` остаётся |
| H6 Ownership checks | **Не исправлено end-to-end** | Immediate APIs проверяют часть actors; queued commander paths и laboratory shortcut обходят boundary |
| H7 Hardcoded P1 | **В основном исправлено** | Binding инъецируется, но renderer всё ещё сравнивает owners с `PlayerId(1)` |
| M1 FoW repaint | **В основном исправлено** | Dirty-list decay; vision discs всё ещё рисуются каждый tick |
| M2 LINQ allocations | **Частично исправлено** | Scratch buffers добавлены; path/factory/research/world queries всё ещё аллоцируют |
| M3 Balance to config | **Частично по scope** | Core build costs мигрированы; SFML affordability/menu и остальные balance domains остаются code-owned |
| M4 Config TPS | **Host wiring исправлен** | TPS передаётся в SFML, но Core duration/history constants остаются 30 TPS |
| M5 Cheat APIs | **Частично исправлено** | `TryAddOutputItemToEntity`, public energy drain и public `ResearchSystem` остаются mutation hooks |
| M6 Inventory mutators | **Исправлено** | Mutators стали assembly-internal |
| M7 Headless host | **Частично исправлено** | Host есть и проверяется в CI, но stub AI читает `World` и вызывает immediate API |
| M8 Fair observation | **Частично исправлено** | FoW gate есть, но возвращаются live entity references |
| L1 Presentation state | **Исправлено/задокументировано** | Sink и hash-exclusion tests |
| L2 Golden hash | **Осознанно отложено** | Решение документировано; dual-run остаётся gate |
| L3 Dead commanders | **Исправлено** | Удаляются после victory evaluation |
| L4 JSON behavior | **Частичный vertical slice** | `lossCondition` влияет на victory; cross-catalog/research validation и content fingerprints неполны |
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

Hasher включает `ResearchCatalog.ContentHash`, но не identity `BuildCostCatalog` и `EntityCatalog`: [`SimulationStateHasher.cs:33–41`](../../src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs#L33-L41). Для production JSON research hash канонизирует полный DTO: [`ResearchContentLoader.cs:108–125`](../../src/SteelConveyorWar.Core/Research/ResearchContentLoader.cs#L108-L125). Более слабый ID/count-only hash используется embedded test/default fallback: [`MvpResearchCatalog.cs:5–17`](../../src/SteelConveyorWar.Core/Research/MvpResearchCatalog.cs#L5-L17), [`MvpResearchCatalog.cs:612–620`](../../src/SteelConveyorWar.Core/Research/MvpResearchCatalog.cs#L612-L620).

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
- command model не выражает `TryConfirmExclusive`, `TrySetProjectWeight`, `preferredTrackId` и `confirmExclusive`;
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

`TryAddOutputItemToEntity` публично добавляет item без игрового источника или actor: [`GameSimulation.cs:1028–1039`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L1028-L1039). `TryConsumeBuildingEnergy` публично меняет energy/stat accounting через переданный live entity: [`PowerSystem.cs:26–53`](../../src/SteelConveyorWar.Core/Systems/PowerSystem.cs#L26-L53). Публичный `ResearchSystem` принимает live authoritative research/simulation и может выполнять mutation вне `AdvanceTick`: [`ResearchSystem.cs:3–21`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L3-L21), [`ResearchSystem.cs:222–228`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L222-L228).

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

### High — gameplay/content correctness

#### R26. SFML позволяет управлять research противника

Laboratory number shortcut не проверяет `selectedEntity.OwnerId == localPlayer`. Вместо этого он берёт owner выбранной лаборатории и вызывает mutation от его имени: [`SfmlPlaySession.cs:384–395`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L384-L395).

Игрок может выбрать видимую enemy laboratory и переключить research противника. Это текущий gameplay authority bug, а не только будущий network risk.

**Рекомендация:** единый local command controller с обязательным actor; запретить enemy entity actions на Core boundary и покрыть P1/P2 adapter tests.

#### R27. Selection и combat tracers нарушают FoW

Visibility проверяется только в момент click/selection: [`SfmlPlaySession.cs:744–767`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L744-L767). После ухода enemy из vision `selectedEntityId` сохраняется, а HUD продолжает читать live HP, energy, production, world position и queued orders: [`HudOverlay.cs:237–286`](../../src/SteelConveyorWar.Sfml/Ui/HudOverlay.cs#L237-L286).

Все global `CombatShotsThisTick` сохраняются без local-player filtering: [`SfmlPlaySession.cs:915–922`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L915-L922), а renderer рисует их без visibility gate: [`WorldRenderer.cs:44–58`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L44-L58), [`WorldRenderer.cs:83–101`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L83-L101).

Следствие — hidden movement/combat можно отслеживать через выбранный object и tracer endpoints.

**Рекомендация:** каждый frame инвалидировать selection через observation snapshot; фильтровать presentation events по observer visibility.

#### R28. Research content validation пропускает опасные значения

Validator требует наличие science packs, но не проверяет `Amount > 0` и duplicate item entries: [`ResearchContentValidator.cs:29–38`](../../src/SteelConveyorWar.Core/Research/ResearchContentValidator.cs#L29-L38). При отрицательной стоимости `Inventory.TryRemove(item, negative)` увеличивает количество item: [`Inventory.cs:69–84`](../../src/SteelConveyorWar.Core/Logistics/Inventory.cs#L69-L84). Duplicate packs проходят `CanAfford` по отдельности, но могут частично списаться и затем завершиться `false`: [`ResearchSystem.cs:429–447`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L429-L447).

Profile budget validation проверяет только сумму и известность keys, но допускает отрицательные default allocations: [`ResearchContentValidator.cs:53–65`](../../src/SteelConveyorWar.Core/Research/ResearchContentValidator.cs#L53-L65).

Ветка unknown `gate.TargetTierId` также не бросает исключение: [`ResearchContentValidator.cs:68–79`](../../src/SteelConveyorWar.Core/Research/ResearchContentValidator.cs#L68-L79). После completion неизвестный tier записывается в authoritative state: [`ResearchSystem.cs:647–660`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L647-L660).

**Рекомендация:** fail-fast для non-positive pack amounts, duplicate pack item entries, negative allocations и unknown target tiers; negative configuration tests.

#### R29. Research work теряет источник pack и искажает allocations/weights

`TryConsumePackForAnyActiveProject` возвращает только `bool`, после чего Core увеличивает общий `packConsumptions`; identity проекта, под который был списан pack, теряется: [`ResearchSystem.cs:413–455`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L413-L455). Затем generic work распределяется между всеми active projects. При одновременных previous-tier optionals и current-tier projects pack одного типа может продвинуть другой project.

Дополнительно при `packConsumptions = 1` доли всех tracks, кроме последнего, округляются вниз до нуля, а весь remainder получает последний track: [`ResearchSystem.cs:452–480`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L452-L480). Тот же last-entry bias повторяется для weighted projects: [`ResearchSystem.cs:482–523`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L482-L523).

Например, allocation 70/30 при одном work unit превращается в 0/100 на каждом cycle. UI хранит настройку, но фактический progress может её игнорировать.

**Рекомендация:** сохранять consumed-project identity; deterministic remainder accumulator / largest-remainder rotation между cycles; tests на mixed pack types, долгосрочное 70/30 и weighted distribution.

#### R30. SFML build UI расходится с runtime build catalog

Build bar вызывает affordability без `simulation.BuildCostCatalog`, поэтому model использует embedded fallback: [`BuildBarOverlay.cs:50–73`](../../src/SteelConveyorWar.Sfml/Ui/BuildBarOverlay.cs#L50-L73), [`BuildBarModel.cs:50–58`](../../src/SteelConveyorWar.Sfml/Ui/BuildBarModel.cs#L50-L58). UI также учитывает только commander inventory, тогда как Core может оплатить build из nearby hubs.

Список buildable entities hardcoded: [`BuildMenuCatalog.cs:5–26`](../../src/SteelConveyorWar.Sfml/Ui/BuildMenuCatalog.cs#L5-L26) и уже не включает настроенные `UndergroundConveyor`, `SteelWall`, `CannonTurret`, `AntiAirTurret`: [`build-costs.json:53–57`](../../config/build-costs.json#L53-L57), [`build-costs.json:85–105`](../../config/build-costs.json#L85-L105).

**Рекомендация:** build menu/view model строится из authoritative `BuildCostCatalog` + capability snapshot; один affordability query в Core.

### Medium — additional integration/scaling gaps

#### R31. Multiplayer roster и victory остаются 1v1-specific

Players загружаются из map config, но starting entities создаются для фиксированных `PlayerId(1)`/`PlayerId(2)`: [`GameSimulation.cs:1393–1416`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L1393-L1416). Victory объявляется только когда остаётся один active **player**, а не одна active team: [`VictorySystem.cs:16–35`](../../src/SteelConveyorWar.Core/Systems/VictorySystem.cs#L16-L35).

Renderer также использует `PlayerId(1)` для outline/color, несмотря на injected local seat: [`WorldRenderer.cs:217–250`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L217-L250), [`WorldRenderer.cs:417–422`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L417-L422).

#### R32. Configurable TPS меняет real-time semantics не полностью

SFML pacing принимает любой положительный configured TPS, но Core durations и `EnergyStatsHistory` используют константу 30 TPS: [`GameSimulation.cs:5–12`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L5-L12), [`EnergyStatsHistory.cs:11–12`](../../src/SteelConveyorWar.Core/Energy/EnergyStatsHistory.cs#L11-L12), [`EnergyStatsHistory.cs:97–100`](../../src/SteelConveyorWar.Core/Energy/EnergyStatsHistory.cs#L97-L100).

Документация требует держать configured TPS согласованным с Core default, но это не enforced: [`MVP_IMPLEMENTATION_DECISIONS.md:35`](../MVP_IMPLEMENTATION_DECISIONS.md#L35). Значение, отличное от 30, меняет реальную длительность gameplay и графиков.

#### R33. SFML rendering имеет отдельные scaling hotspots

Minimap каждый frame сканирует все 192×112 tiles; `Unknown` пропускаются, поэтому **до** 21 504 explored/visible tile draw calls выполняются в худшем случае: [`HudOverlay.cs:85–103`](../../src/SteelConveyorWar.Sfml/Ui/HudOverlay.cs#L85-L103). World renderer каждый frame создаёт visible-entity list и перечисляет его дважды: [`WorldRenderer.cs:44–55`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L44-L55).

Нужны cached minimap texture/dirty regions и frame-allocation benchmark.

#### R34. Cross-catalog validation и schema evolution неполны

Research unlock effects принимают произвольные `contentKind/contentId`: [`ResearchContentLoader.cs:130–149`](../../src/SteelConveyorWar.Core/Research/ResearchContentLoader.cs#L130-L149), а неизвестные kinds затем молча игнорируются при применении: [`ResearchSystem.cs:700–720`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L700-L720). Build requirement принимает любой technology string без проверки against `ResearchCatalog`: [`BuildCostContentLoader.cs:50–63`](../../src/SteelConveyorWar.Core/Content/BuildCostContentLoader.cs#L50-L63).

Content schemas также не имеют строгой evolution policy: build/entity/tile/game/map loaders принимают любую `schemaVersion >= 1` ([`BuildCostContentLoader.cs:14–22`](../../src/SteelConveyorWar.Core/Content/BuildCostContentLoader.cs#L14-L22), [`GameSettingsLoader.cs:14–22`](../../src/SteelConveyorWar.Core/Content/GameSettingsLoader.cs#L14-L22), [`MapSettingsLoader.cs:14–22`](../../src/SteelConveyorWar.Core/Content/MapSettingsLoader.cs#L14-L22)), а research DTO вообще не имеет schema version: [`ResearchContentLoader.cs:285–291`](../../src/SteelConveyorWar.Core/Research/ResearchContentLoader.cs#L285-L291).

Следствие — typo может молча оставить content навсегда locked, а future schema быть принятой старым loader без понимания новых semantics.

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
6. per-unit energy heap loop при росте production;
7. per-tick combat/power dictionaries и read-only wrappers;
8. uncached minimap draw calls.

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
12. public/castable state surfaces слишком широкие для untrusted adapters;
13. command DTO не выражают все research intents;
14. roster/start/victory/render assumptions остаются 1v1/P1-specific.

**Вывод:** Core готов к следующему этапу исследования lockstep, но не к заявлению «network-ready».

---

## 8. Боты

### Что уже можно

- запускать симуляцию без SFML;
- выбирать PlayerId;
- получать FoW-filtered entities/terrain;
- отправлять существующие gameplay intents;
- проверять deterministic hash.

Эти возможности являются opt-in: текущий Headless stub читает `simulation.World.Entities` и вызывает immediate mutation, не используя fair view/queue.

### Чего не хватает

1. immutable per-tick observation snapshot;
2. own economy/research/capability view;
3. queue-only command controller;
4. deterministic bot cadence/seed contract;
5. scenario tests «бот достигает цели за tick budget»;
6. защита от retained live references;
7. filtered presentation/event stream;
8. отдельный AI module (сейчас Headless содержит только stub).

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
- SFML дублирует FoW policy, читает live selected entity и содержит command-producing code в session/overlays.
- Public `ResearchSystem` и energy mutation helper позволяют обходить tick orchestration.
- два hosts дублируют config bootstrap.
- content behavior одновременно живёт в catalogs, enums, SFML catalogs и `MvpDefinitions`.

---

## 10. Приоритетный roadmap

### P0 — correctness / trust

1. Закрыть enemy-laboratory research control.
2. Инвалидировать hidden selection и фильтровать combat tracers по observer FoW.
3. Запретить non-positive/duplicate science pack entries, negative default allocations и unknown target tiers.
4. Сохранить consumed-project identity и исправить deterministic research allocation/weight remainder.
5. Actor authorization для **всех** command kinds.
6. Перевести SFML и Headless на queue-only command sink.
7. Canonical `(tick, actor, actorSequence)` order + duplicate policy.
8. Сделать command payloads глубоко immutable.
9. Валидировать untrusted envelopes без исключений из tick loop.
10. Добавить queued commander orders в state hash.
11. Убрать cast-mutable root collections/tables.
12. Закрыть public item/energy/research mutation hooks.
13. Добавить полный content/session manifest hash.

### P1 — bots / scale

1. Immutable `PlayerObservationSnapshot`.
2. Own player economy/research view.
3. Строить build menu/affordability из runtime catalog.
4. Shared spatial query service для movement/threat/collision.
5. Reusable A* workspace/path cache.
6. Cached/dirty minimap rendering.
7. Benchmark project и regression scenarios.

### P2 — maintainability / extensibility

1. Реально выделять systems из partial `GameSimulation`.
2. Разделить `SfmlPlaySession` input/session state.
3. Общий host bootstrap для Client/Headless.
4. Мигрировать recipes/combat/stacks/power/footprints в versioned content catalogs.
5. Добавить cross-catalog validation и strict schema-version policy.
6. Enforce или удалить configurable TPS, согласовав все tick-duration/history semantics.
7. Сделать starts/victory/rendering roster/team-driven.
8. Fixed-point world coordinates перед heterogeneous lockstep.

---

## 11. Финальный вывод

Повторный аудит подтверждает, что remediation wave после #61 была полезной: наиболее очевидные perf bottlenecks и layer violations исправлены или существенно смягчены. Репозиторий теперь имеет реальную основу для headless simulation, command logging и AI experiments. Однако более глубокая cross-domain проверка нашла текущие gameplay correctness defects в research/FoW/config paths, поэтому итоговая оценка снижена до **5.6/10**.

При этом несколько закрытых issue реализовали **минимальный vertical slice**, а не конечную архитектуру. Самая важная корректировка ожиданий:

> Наличие command queue, state hash и headless host ещё не делает игру готовой к сети; production hosts, authorization, ordering, content identity и immutable observations должны образовать один непрерывный authoritative pipeline. До этого необходимо закрыть текущие enemy-research, FoW и research-validation defects.

Текущий статус: **перспективный deterministic MVP prototype с сильными project boundaries, но с High correctness gaps; не production-ready для сетевого multiplayer или честного competitive client**.
