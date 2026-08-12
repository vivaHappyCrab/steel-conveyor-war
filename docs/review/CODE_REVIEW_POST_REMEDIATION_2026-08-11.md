# Третье ревью кода — Steel Conveyor War

**Дата:** 2026-08-11  
**Проверенный commit:** `ba43004` (`develop`, merge PR #111)  
**Связанный issue:** [#61 — Полный аудит архитектуры, производительности и расширяемости](https://github.com/vivaHappyCrab/steel-conveyor-war/issues/61)  
**Предыдущее ревью:** [`CODE_REVIEW_REPEAT_2026-08-10.md`](CODE_REVIEW_REPEAT_2026-08-10.md)  
**Контекст:** проверка после заявленного закрытия R01–R34 в `docs/review/STATUS.md`  
**Метод:** статический cross-domain анализ Core/SFML/Client/Headless/config/tests, шесть независимых профильных проверок, полная локальная верификация и quick benchmark matrix

---

## 1. Итог

PR #111 существенно улучшил проект:

- SFML и Headless используют отложенный command sink;
- команды получили version/sequence/result и расширенные serializer tests;
- добавлены immutable observation DTO, content manifest и millitile-координаты;
- Power и Combat выделены в тестируемые системы;
- movement/threat получили общий spatial index и reusable A* workspace;
- появились dirty FoW, minimap cache, batched energy fill, benchmark project;
- roster/start/team victory и runtime content bootstrap стали заметно полнее;
- Release build, 444 теста и оба smoke-пути проходят.

Однако запись «34/34 реализовано полностью» в [`STATUS.md:8–11`](STATUS.md#L8-L11) не подтверждается кодом. Строгая проверка исходных DoD даёт:

- **полностью закрыто:** 8;
- **частично закрыто:** 24;
- **открыто:** 2 (`R17`, `R18`);
- **Critical:** 0;
- **High:** 7;
- **Medium:** 11;
- **Low:** 3.

Главные остаточные риски:

1. загруженный `gameplay-tables.json` входит в manifest, но симуляция продолжает выполнять embedded-таблицы;
2. state hash пропускает persistent research scheduler state;
3. одинаковые command keys не имеют deterministic tie-break, а actor не привязан к сетевой сессии;
4. idle-cache фабрики может навсегда пропустить производство после research unlock;
5. SFML build menu по-прежнему строится из embedded build catalog;
6. tracer, видимый по одному endpoint, раскрывает точную координату второго скрытого endpoint;
7. несколько authoritative content catalogs по-прежнему доступны для внешней cast-мутации.

### Оценка

| Критерий | До PR #111 | Сейчас | Комментарий |
|---|---:|---:|---|
| Архитектурные паттерны | 6.0 | **7.0** | Command/observation/system/content foundations стали реальными; authority pipeline всё ещё негерметичен |
| Производительность | 6.0 | **6.0** | Малые сценарии быстрые, но gate мягкий, matrix мала, несколько hot paths остаются линейными/аллокационными |
| Расширяемость для сети | 4.0 | **4.0** | Queue/hash/fixed-point улучшены; identity/order/hash/session blockers остаются |
| Расширяемость для ботов | 6.0 | **6.0** | Fair DTO и queued sink работают; snapshot не полностью immutable/atomic, host остаётся stub |
| Разделение ответственности | 6.0 | **6.0** | Power/Combat и SFML state выделены; Core владеет file I/O, session и simulation всё ещё широки |
| **Итого** | **5.6** | **5.8** | Remediation полезен, но часть closure claims закрывает инфраструктуру, а не исходный end-to-end DoD |

Отдельная оценка data-driven/content extensibility: **4.0/10**.

---

## 2. Верификация

Выполнено:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
dotnet run --project tests/SteelConveyorWar.Benchmarks -c Release --no-build -- --quick
```

Результат:

- restore/format: pass;
- Release build: pass, **0 warnings / 0 errors**;
- Core tests: **382/382 pass**;
- SFML tests: **62/62 pass**;
- Headless smoke: pass, 90 ticks, 6 commands;
- Client smoke: pass;
- отдельный quick tick/allocation run выполнен и записал JSON artifact; из-за soft-gate semantics его exit `0` не считается pass/fail доказательством бюджета.

Ограничения доказательств:

- coverage собирается, но threshold не enforced;
- quick benchmark не является стабильным microbenchmark и не моделирует крупную фабрику;
- CI не сравнивает state hash между Windows и Linux;
- rendering не имеет frame-time/image-equivalence benchmark;
- статический аудит не заменяет network fuzzing, длительный soak и profiler trace.

---

## 3. Статус R01–R34 после повторной проверки

| ID | Статус | Доказательство / остаток |
|---|---|---|
| R01 | 🟡 Частично | Command dispatcher передаёт actor, но immediate APIs сохраняют `actor = null` opt-out; DoD «ни один handler без проверки» не выполнен буквально |
| R02 | ✅ Закрыто | SFML и Headless используют `DeferredCommandSink` |
| R03 | 🟡 Частично | Unique non-zero sequence сортируется; одинаковые keys и sequence `0` зависят от enqueue order |
| R04 | 🟡 Частично | Live entity leak закрыт DTO; вложенные collections snapshot можно downcast/mutate |
| R05 | 🟡 Частично | World/players/queue защищены; entity/tile и вложенные content dictionaries остаются cast-mutable |
| R06 | ✅ Закрыто по минимальному DoD | Power и Combat выделены; `GameSimulation` всё ещё большой orchestration/state container |
| R07 | 🟡 Частично | Spatial index/A* workspace есть; повторные rebuilds и allocating world queries остаются |
| R08 | ✅ Закрыто | Commander build/demolish orders входят в hash |
| R09 | ✅ Закрыто | Malformed handler изолирован, rejection записывается |
| R10 | 🟡 Частично | Catalog manifest есть; TPS/map/roster отсутствуют, gameplay identity не совпадает с execution |
| R11 | 🟡 Частично | Version/result/round-trip есть; не все intents имеют DTO, batch APIs почти не проверены |
| R12 | 🟡 Частично | Constructor copy есть; public `init` позволяет заменить immutable payload через `with` |
| R13 | 🟡 Частично | Pending hash есть; colliding keys наследуют unspecified tie ambiguity |
| R14 | ✅ Закрыто | Production mutation hooks ограничены |
| R15 | 🟡 Частично | Correctness equivalence есть; equal-ratio case остаётся per-unit |
| R16 | 🟡 Частично | Static ticks/dirty tiles улучшены; moved sources и signatures всё ещё дают широкую работу |
| R17 | 🔴 Открыто | Idle-cache имеет correctness regression после unlock и не входит в hash |
| R18 | 🔴 Открыто | JSON catalog загружается/хешируется, но gameplay исполняет `Embedded` |
| R19 | 🟡 Частично | Economy/research/events/capabilities добавлены; atomic map snapshot и hard immutability отсутствуют |
| R20 | 🟡 Частично по evidence | Authoritative positions/collision используют millitiles; long-run cross-OS hash comparison из DoD не автоматизирован |
| R21 | 🟡 Частично | `SessionState`/mapper выделены; session и mapper всё ещё смешивают orchestration/state/command dispatch |
| R22 | 🟡 Частично | Harness/CI job есть; превышение budgets по умолчанию не ломает CI |
| R23 | ✅ Закрыто | Catch-up ограничен и policy документирована |
| R24 | ✅ Закрыто | Local player валидируется до session start |
| R25 | 🟡 Частично | Дублирование убрано ценой file I/O внутри Core |
| R26 | 🟡 Частично | Enemy laboratory shortcut исправлен; public Core research APIs всё ещё позволяют trusted/null-actor opt-out |
| R27 | 🟡 Частично | Hidden selection закрыт; полный tracer раскрывает скрытый противоположный endpoint |
| R28 | ✅ Закрыто по исходному scope | Non-positive/duplicate packs, allocations и target tiers проверяются |
| R29 | 🟡 Частично | Fair allocation реализован; remainder state отсутствует в hash |
| R30 | ✅ Закрыто | Production menu/hotkeys compose from match `BuildCostCatalog` (H05); Embedded helper obsolete |
| R31 | 🟡 Частично | Roster/team happy paths есть; start bounds/overlap и zero-team result не закрыты |
| R32 | 🟡 Частично | TPS прокинут в hosts/history; часть tick semantics и session identity остаётся фиксированной |
| R33 | 🟡 Частично | Minimap cache есть; catch-up invalidation, native resource и render benchmark остаются |
| R34 | 🟡 Частично | Несколько references проверяются; research unlocks/schema strictness остаются неполными |

---

## 4. Каталог актуальных проблем

Severity здесь учитывает не только дефект default-content path, но и прямые блокеры запрошенных критериев network/modding extensibility. Поэтому разрыв runtime catalogs/UI отмечен как High, хотя поставляемые embedded и JSON defaults сейчас совпадают.

### High

#### H1. Загруженные gameplay tables не являются authoritative

`ContentBootstrap` загружает `gameplay-tables.json` и передаёт каталог в match options: [`ContentBootstrap.cs:57–82`](../../src/SteelConveyorWar.Core/Hosting/ContentBootstrap.cs#L57-L82). `GameSimulation` сохраняет его, а manifest хеширует: [`SimulationContentManifest.cs:27–33`](../../src/SteelConveyorWar.Core/Content/SimulationContentManifest.cs#L27-L33).

Но runtime facade всегда обращается к singleton:

- power/stacks/footprints/collision: [`MvpDefinitions.cs:55–85`](../../src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs#L55-L85);
- resistance/recipes/stats: [`MvpDefinitions.cs:128–145`](../../src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs#L128-L145);
- factory logic читает `MvpDefinitions.ProductionRecipes`: [`FactoryBastionSystem.cs:71–100`](../../src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs#L71-L100);
- renderer также читает embedded footprint и unit classification: [`WorldRenderer.cs:183–193`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L183-L193).

Следствия:

- изменение JSON меняет manifest, но не gameplay;
- два binary с разными embedded tables и одинаковым JSON могут пройти manifest check и симулировать по-разному;
- Core и UI не могут честно считаться data-driven.

**Рекомендация:** сделать match-scoped `GameplayTablesCatalog` единственным источником rules; передать его system contexts и presentation snapshots; оставить embedded только fallback для composition.

#### H2. State hash пропускает authoritative research scheduler state

Largest-remainder scheduler хранит persistent accumulators: [`PlayerResearchState.cs:39–43`](../../src/SteelConveyorWar.Core/Research/PlayerResearchState.cs#L39-L43), [`PlayerResearchState.cs:95–97`](../../src/SteelConveyorWar.Core/Research/PlayerResearchState.cs#L95-L97). Они напрямую определяют следующий выбранный track/project: [`ResearchSystem.cs:441–476`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L441-L476), [`ResearchSystem.cs:479–515`](../../src/SteelConveyorWar.Core/Research/ResearchSystem.cs#L479-L515).

`WriteResearch` пишет progress, allocations и weights, но не оба remainder dictionaries: [`SimulationStateHasher.cs:237–280`](../../src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs#L237-L280).

Две симуляции могут иметь одинаковый hash, а на следующем lab cycle выбрать разные проекты.

**Рекомендация:** детерминированно хешировать оба accumulator map; bump `AlgorithmVersion`; тест «разный remainder → разный hash и следующий выбор».

#### H3. Command identity/order ещё не задаёт сетевой authority contract

Для одного tick сортировка использует только `(Actor, Sequence)`: [`GameSimulation.Commands.cs:126–145`](../../src/SteelConveyorWar.Core/GameSimulation.Commands.cs#L126-L145), [`GameSimulation.Commands.cs:178–190`](../../src/SteelConveyorWar.Core/GameSimulation.Commands.cs#L178-L190).

Остатки:

- `Sequence == 0` не имеет canonical tie-break;
- `List<T>.Sort` нестабилен, поэтому разные payload с одинаковым `(Actor, Sequence)` разрешаются неопределённо и могут зависеть от arrival/list layout;
- duplicate set создаётся заново каждый tick;
- sequence state локален каждому sink instance: [`DeferredCommandSink.cs:19–20`](../../src/SteelConveyorWar.Core/Commands/DeferredCommandSink.cs#L19-L20), [`DeferredCommandSink.cs:59–65`](../../src/SteelConveyorWar.Core/Commands/DeferredCommandSink.cs#L59-L65);
- sink доверяет `command.Actor`: [`DeferredCommandSink.cs:41–54`](../../src/SteelConveyorWar.Core/Commands/DeferredCommandSink.cs#L41-L54).

В текущем local/headless пути это не exploit: сетевого ingress ещё нет. Но будущий transport обязан привязать authenticated session к actor и не принимать client-claimed identity. Иначе клиент сможет назвать чужой `PlayerId`.

**Рекомендация:** server-assigned `(sessionActor, monotonicCommandId)`, запрет `Sequence <= 0` на network path, persistent dedupe window и deterministic rejection conflicting duplicates независимо от arrival order.

#### H4. Factory idle-cache может не проснуться после research unlock

Manual factory пропускается до проверки recipe/unlock, если совпали accounting epoch и input version: [`FactoryBastionSystem.cs:56–63`](../../src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs#L56-L63). Locked recipe запоминает этот cache: [`FactoryBastionSystem.cs:71–80`](../../src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs#L71-L80).

`TrySetFactoryProduction` разрешает предварительно выбрать существующий locked recipe: [`GameSimulation.cs:818–852`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L818-L852). Research completion обновляет health, но не `_armyAccountingEpoch`: [`ResearchTickSystem.cs:16–20`](../../src/SteelConveyorWar.Core/Systems/ResearchTickSystem.cs#L16-L20).

После unlock неизменившаяся фабрика может продолжать skip бесконечно. Cache fields при этом влияют на future behavior, но не входят в [`SimulationStateHasher.WriteEntity`](../../src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs#L134-L235).

**Рекомендация:** invalidation epoch должен учитывать research capability/version; derived caches либо полностью восстанавливаются из authoritative state, либо входят в hash/save surface.

#### H5. SFML build menu остаётся embedded

Production static list строится один раз из `MvpBuildCostCatalog.Embedded`: [`BuildMenuCatalog.cs:38–45`](../../src/SteelConveyorWar.Sfml/Ui/BuildMenuCatalog.cs#L38-L45). Его используют:

- hotkey `B`: [`InputCommandMapper.cs:162–165`](../../src/SteelConveyorWar.Sfml/Input/InputCommandMapper.cs#L162-L165);
- overlay picking/drawing: [`BuildBarOverlay.cs:9–33`](../../src/SteelConveyorWar.Sfml/Ui/BuildBarOverlay.cs#L9-L33), [`BuildBarOverlay.cs:66–72`](../../src/SteelConveyorWar.Sfml/Ui/BuildBarOverlay.cs#L66-L72);
- copy fallback: [`BuildBarModel.cs:96–109`](../../src/SteelConveyorWar.Sfml/Ui/BuildBarModel.cs#L96-L109).

`ComposeFrom(simulation.BuildCostCatalog)` существует, но production composition его не вызывает. Runtime-added/removed kinds расходятся с Core.

**Рекомендация:** создать match-scoped build menu model из `simulation.BuildCostCatalog`, передавать его в mapper/overlay и исключить static mutable array.

#### H6. Combat tracer раскрывает скрытый противоположный endpoint

SFML разрешает весь shot, если виден любой participant/endpoint: [`WorldRenderer.cs:155–173`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L155-L173). Затем renderer рисует полную линию `From → To`: [`WorldRenderer.cs:94–112`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L94-L112).

`PlayerView` повторяет ту же политику и возвращает обе точные координаты: [`PlayerView.cs:138–155`](../../src/SteelConveyorWar.Core/Observation/PlayerView.cs#L138-L155), [`PlayerView.cs:176–199`](../../src/SteelConveyorWar.Core/Observation/PlayerView.cs#L176-L199).

Если виден стрелок, но цель скрыта, tracer раскрывает цель; если видна цель, раскрывается скрытый стрелок. Текущий test закрепляет «виден любой конец», а не competitive-safe policy.

**Рекомендация:** передавать полный shot только когда разрешены оба endpoints; иначе clip к границе видимой области либо выдавать обезличенный impact/muzzle event без скрытой координаты.

#### H7. Authoritative content catalogs остаются cast-mutable

Public records объявляют `IReadOnlyDictionary`, но loaders возвращают реальные dictionaries:

- entity loader: [`EntityContentLoader.cs:57–70`](../../src/SteelConveyorWar.Core/Content/EntityContentLoader.cs#L57-L70), public surface: [`EntityCatalog.cs:10–14`](../../src/SteelConveyorWar.Core/Content/EntityCatalog.cs#L10-L14);
- tile loader: [`TileContentLoader.cs:40–45`](../../src/SteelConveyorWar.Core/Content/TileContentLoader.cs#L40-L45), public surface: [`TileCatalog.cs:9–13`](../../src/SteelConveyorWar.Core/Content/TileCatalog.cs#L9-L13);
- build-cost root обёрнут, но `Costs[kind]` остаётся raw dictionary: [`BuildCostContentLoader.cs:60–90`](../../src/SteelConveyorWar.Core/Content/BuildCostContentLoader.cs#L60-L90);
- gameplay root maps обёрнуты, но recipe input maps сохраняются внутри records: [`GameplayTablesLoader.cs:150–180`](../../src/SteelConveyorWar.Core/Content/GameplayTablesLoader.cs#L150-L180), [`GameplayTablesLoader.cs:311–337`](../../src/SteelConveyorWar.Core/Content/GameplayTablesLoader.cs#L311-L337).

Внешний adapter/plugin может изменить authoritative defeat rules или build costs после tick 0, вне command stream. Gameplay recipe maps тоже mutable, но из-за H1 сейчас являются изменяемой manifest metadata, а не реально исполняемыми match rules. Manifest будет пересчитан уже по изменённому объекту, а peers могут мутировать его в разный момент.

**Рекомендация:** freeze/deep-copy все catalog graphs при parse/construction; добавить recursive cast-mutation tests.

### Medium

#### M1. Deep immutability обходится через record `with`

Конструкторы копируют caller collections, но properties остаются public `init`:

- [`BastionOrder.WaypointList`](../../src/SteelConveyorWar.Core/Domain/ValueObjects.cs#L103-L110);
- [`SetTrackAllocationCommand.Allocations`](../../src/SteelConveyorWar.Core/Commands/SimulationCommands.cs#L87-L104).

`order with { WaypointList = mutableList }` и аналогичный command снова создают mutable post-enqueue payload.

**Рекомендация:** хранить конкретный `ImmutableArray`/`ImmutableDictionary` без публичного replacement setter; tests должны покрывать `with` bypass.

#### M2. Command protocol выражает не все authoritative intents

Публичный `TrySetProjectWeight` не имеет command DTO: [`GameSimulation.cs:794–805`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L794-L805). `AssignFactoryBastion` рекламируется/сериализуется, но handler всегда возвращает `false`: [`GameSimulation.cs:855–863`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L855-L863).

All-kind tests в основном проверяют kind/runtime type, а не равенство всех payload fields; batch serializer APIs почти не имеют negative/round-trip matrix.

**Рекомендация:** единый capability registry «advertised = serializable = applicable» и value-equality tests для каждого command kind.

#### M3. Schema и cross-catalog validation остаются неполными

`ContentCrossValidator` проверяет только build/recipe technology и defeat kind: [`ContentCrossValidator.cs:32–67`](../../src/SteelConveyorWar.Core/Content/ContentCrossValidator.cs#L32-L67). Параметр `tiles` фактически не используется.

Не проверяются:

- research `UnlockContentEffect` → entity/recipe/item recipe;
- gameplay recipe inputs/output domains;
- map starts bounds/overlap;
- все числовые ranges gameplay stats;
- unknown JSON properties;
- undefined numeric enum values.

Research DTO всё ещё не имеет общего `schemaVersion`, а неизвестные unlock kinds могут быть silently ignored.

**Рекомендация:** strict unmapped-member policy, schema version для каждого root DTO, полный reference graph и negative tests по каждому catalog edge.

#### M4. Benchmark job наблюдает, но не блокирует regression

При превышении бюджета process возвращает `0`, пока вручную не установлен `SCW_BENCH_HARD_GATE=1`: [`Benchmarks/Program.cs:21–38`](../../tests/SteelConveyorWar.Benchmarks/Program.cs#L21-L38). CI переменную не устанавливает: [`.github/workflows/ci.yml:122–127`](../../.github/workflows/ci.yml#L122-L127).

Budgets равны 50 ms/tick и 5 MB/tick: [`TickScenarioRunner.cs:10–12`](../../tests/SteelConveyorWar.Benchmarks/TickScenarioRunner.cs#L10-L12). Matrix содержит 40-unit army, 24-unit battle, 80 consumers, а `fow` повторяет army setup: [`TickScenarioRunner.cs:14–23`](../../tests/SteelConveyorWar.Benchmarks/TickScenarioRunner.cs#L14-L23). Initial A* выполняется до измерения: [`TickScenarioRunner.cs:29–50`](../../tests/SteelConveyorWar.Benchmarks/TickScenarioRunner.cs#L29-L50).

**Рекомендация:** calibrated hard budgets, baselines/artifact comparison и matrix 100/500/1000 units, belts, 1000 vision sources, 2000 buildings, render frame.

#### M5. Batched energy fill имеет per-unit worst case

Batch вычисляется относительно только следующего heap competitor: [`PowerSystem.cs:243–280`](../../src/SteelConveyorWar.Core/Systems/PowerSystem.cs#L243-L280). При одинаковых capacities/ratios каждый consumer после одной единицы перестаёт быть minimum, поэтому common equal-ratio case остаётся близок к `O(energy × log consumers)`.

**Рекомендация:** level-based water filling по группе одинакового minimum ratio; benchmark с большой produced energy и одинаковыми consumers.

#### M6. Spatial/FoW improvements всё ещё делают широкую работу и аллокации

Spatial index rebuild вызывается несколькими systems в одном tick: [`CommanderOrdersSystem.cs:19–22`](../../src/SteelConveyorWar.Core/Systems/CommanderOrdersSystem.cs#L19-L22), [`CommanderOrdersSystem.cs:47–50`](../../src/SteelConveyorWar.Core/Systems/CommanderOrdersSystem.cs#L47-L50), [`CommanderOrdersSystem.cs:70–73`](../../src/SteelConveyorWar.Core/Systems/CommanderOrdersSystem.cs#L70-L73), [`MovementSystem.cs:16–19`](../../src/SteelConveyorWar.Core/Systems/MovementSystem.cs#L16-L19), [`CombatSystem.cs:245–251`](../../src/SteelConveyorWar.Core/Systems/CombatSystem.cs#L245-L251). `GetEntitiesAt` продолжает создавать/sort list: [`GameWorld.cs:73–103`](../../src/SteelConveyorWar.Core/World/GameWorld.cs#L73-L103). Path reconstruction создаёт списки. Power создаёт dictionaries/heaps per player/tick. FoW экономит static ticks, но moved source снова обрабатывает область для каждого player/source, а signature rebuild сортирует collections.

Это не текущий MVP blocker, но отсутствие large-scale benchmark не позволяет считать R07/R16 закрытыми по performance DoD.

#### M7. Multi-tick catch-up теряет minimap invalidation

SFML может выполнить несколько `AdvanceTick` до draw: [`SfmlPlaySession.cs:444–467`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L444-L467). HUD читает только final tick dirty list: [`HudOverlay.cs:99–118`](../../src/SteelConveyorWar.Sfml/Ui/HudOverlay.cs#L99-L118).

Если FoW изменился на первом catch-up tick, а последний tick пуст, earlier dirty tiles потеряны. Minimap может оставаться stale до periodic full rebuild.

**Рекомендация:** session агрегирует dirty regions за все simulation ticks между renders либо Core предоставляет monotonic invalidation generation.

#### M8. SFML resource lifetime и decomposition неполны

`WorldRenderer.DrawWorld` создаёт `RectangleShape` каждый frame без `using`/`Dispose`: [`WorldRenderer.cs:24–40`](../../src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs#L24-L40). Для native SFML object это лишнее давление на finalizer/native heap.

`SfmlPlaySession.Run` по-прежнему владеет window events, camera, pacing, tick advancement, visibility buffering и render composition; `InputCommandMapper` одновременно читает live simulation, мутирует UI state и enqueue-команды.

**Рекомендация:** dispose/reuse draw primitives; разделить frame host, presentation snapshot, pure intent mapping и command gateway.

#### M9. Shared bootstrap нарушает зафиксированную Core boundary

`Core.Hosting.ContentBootstrap` владеет path resolution, `Directory`/`File` и host environment: [`ContentBootstrap.cs:3–7`](../../src/SteelConveyorWar.Core/Hosting/ContentBootstrap.cs#L3-L7), [`ContentBootstrap.cs:15–37`](../../src/SteelConveyorWar.Core/Hosting/ContentBootstrap.cs#L15-L37), [`ContentBootstrap.cs:44–95`](../../src/SteelConveyorWar.Core/Hosting/ContentBootstrap.cs#L44-L95).

Это убирает дублирование Client/Headless, но противоречит принятому правилу «Core parse; hosts I/O».

**Рекомендация:** вынести filesystem composition в отдельный host/bootstrap project либо передавать Core готовые JSON streams/text.

#### M10. Bot observation snapshot не полностью immutable и atomic

`PlayerView` создаёт `List`, `Dictionary`, `HashSet` и возвращает их как `IReadOnly*`: [`PlayerView.cs:91–126`](../../src/SteelConveyorWar.Core/Observation/PlayerView.cs#L91-L126), [`PlayerView.cs:161–173`](../../src/SteelConveyorWar.Core/Observation/PlayerView.cs#L161-L173). Consumer может downcast и изменить retained snapshot. `AllCommandKinds` хранит массив и возвращается напрямую: [`PlayerView.cs:11–14`](../../src/SteelConveyorWar.Core/Observation/PlayerView.cs#L11-L14), [`PlayerView.cs:136`](../../src/SteelConveyorWar.Core/Observation/PlayerView.cs#L136).

Terrain/visibility остаются live calls и не входят в `CaptureSnapshot`, поэтому map-aware решение не атомарно относительно одного observation tick.

**Рекомендация:** immutable collections и один self-contained observation frame либо versioned map view с тем же tick.

#### M11. Session identity, TPS и roster edge cases неполны

Manifest не включает TPS/map/roster: [`SimulationContentManifest.cs:22–33`](../../src/SteelConveyorWar.Core/Content/SimulationContentManifest.cs#L22-L33). Map starts создаются без явной bounds/overlap validation: [`GameSimulation.cs:1624–1645`](../../src/SteelConveyorWar.Core/GameSimulation.cs#L1624-L1645). Victory обрабатывает ровно одну active team, но не zero-team terminal result: [`VictorySystem.cs:30–45`](../../src/SteelConveyorWar.Core/Systems/VictorySystem.cs#L30-L45).

**Рекомендация:** отдельный versioned session manifest `(content, map, roster, teams, TPS, seed, algorithm)` и fail-fast map validation до создания world.

### Low

#### L1. Trailing `--local-player` silently игнорируется

Parser рассматривает flag только при наличии следующего аргумента, поэтому trailing option выбирает default seat вместо понятной ошибки: [`LocalPlayerBinding.cs:13–32`](../../src/SteelConveyorWar.Sfml/LocalPlayerBinding.cs#L13-L32).

#### L2. F1 после смерти local commander может вызвать `First(...)`

Selection helper предполагает наличие живого commander и использует `First`: [`SfmlInputHelpers.cs:49–59`](../../src/SteelConveyorWar.Sfml/Input/SfmlInputHelpers.cs#L49-L59). После поражения shortcut должен no-op либо выбирать безопасный fallback.

#### L3. UI modes не полностью взаимоисключающие

Build bar может оставаться открытым вместе с research/energy/bastion overlay: exclusive helpers закрывают другие overlays, но не build mode: [`SessionState.cs:224–243`](../../src/SteelConveyorWar.Sfml/Input/SessionState.cs#L224-L243). Build-bar click обрабатывается раньше overlay clicks: [`SfmlPlaySession.cs:188–207`](../../src/SteelConveyorWar.Sfml/SfmlPlaySession.cs#L188-L207). Это UX/state-machine debt, не simulation correctness.

---

## 5. Архитектурные паттерны и responsibility boundaries

### Сильные стороны

| Паттерн | Текущее состояние |
|---|---|
| Ports/adapters | Core не зависит от SFML; Headless остаётся Core-only |
| Fixed timestep | Tick boundary и capped local catch-up определены |
| Command pattern | Production hosts используют DTO/sink/queue/result |
| Determinism | Integer world math, ordered iteration, state/pending hashes |
| Observation model | Live `WorldEntity` больше не выходит через fair view |
| System extraction | Power и Combat имеют context contract и isolated tests |
| Spatial indexing | Общий deterministic bucket index обслуживает movement/threat/combat |
| Content loading | Shared fail-fast load и canonical catalog hashing |
| CI | Multi-OS build/test/smoke, coverage artifacts, benchmark artifact |

### Остаточная архитектурная проблема

Сейчас coexist три источника authoritative truth:

```text
GameCreationOptions.GameplayTables  -> manifest
GameplayTablesCatalog.Embedded      -> actual simulation/presentation
EntityKind / switches               -> capability and identity
```

До устранения этого разрыва data-driven catalog является metadata, а не runtime authority.

Целевая цепочка:

```text
Host I/O
  -> validated SessionDefinition
     -> SimulationKernel(match-scoped catalogs + roster + TPS + seed)
        -> systems through explicit context
        -> immutable ObservationFrame
        -> SFML / Bot / Replay adapters

Authenticated command ingress
  -> server-bound actor + canonical command id
  -> validation/dedupe
  -> tick queue
  -> authoritative result log
```

---

## 6. Производительность

Quick matrix выполнен и сформировал JSON artifact. Из-за soft-gate semantics этот запуск подтверждает работоспособность harness, но не доказывает соблюдение бюджета или запас производительности.

Но performance conclusion пока ограничен:

1. budgets слишком широки и не блокируют CI;
2. `fow` не является отдельным FoW stress scenario;
3. A* setup происходит до measurement;
4. factory accounting, belts, state hashing и rendering не представлены;
5. equal-ratio power остаётся per-unit;
6. minimap каждый frame всё ещё сканирует/sort entities;
7. read-only wrappers и scratch plans создают регулярные managed allocations.

Следующий обязательный matrix:

| Scenario | Scale | Gate |
|---|---:|---|
| Idle factory | 100 / 500 / 2000 buildings | p95 tick + alloc/tick |
| Belts/logistics | 100 / 1000 / 5000 segments | p95 logistics |
| Army path/collision | 100 / 500 / 1000 units | initial path + steady move |
| Battle | 100v100 / 500v500 | target/combat p95 |
| FoW | 100 / 1000 moving sources | dirty generation + repaint |
| Power | 100 / 1000 equal-ratio consumers | high-production fill |
| Hash | small / large state | state+pending hash time |
| SFML | explored map + 2000 entities | frame p95 + native/managed alloc |

---

## 7. Сетевой мультиплеер

### Foundation

- deterministic fixed tick;
- integer positions/collision;
- queued production input;
- command serialization/version/sequence/result;
- state + pending hashes;
- content fingerprint;
- Headless host;
- multi-OS CI.

### Блокеры

1. authenticated session не привязан к actor;
2. conflicting duplicate command keys не имеют deterministic tie-break;
3. research remainder и factory cache отсутствуют в hash;
4. loaded content identity не совпадает с executed content;
5. session manifest не включает map/roster/TPS;
6. payload immutability обходится через `with`;
7. нет durable replay/result ledger и save/load surface;
8. нет transport limits/rate limits/message size policy;
9. cross-OS hash печатается, но не сравнивается;
10. competitive event visibility раскрывает скрытые endpoints.

**Вывод:** Core подходит для следующего lockstep prototype, но не для untrusted network play. Оценка **4.0/10**.

---

## 8. Боты

### Готово

- Core-only запуск;
- fair/cheat observation modes;
- immutable entity DTO вместо live entity;
- own economy/research, tech signatures и event stream;
- command vocabulary;
- тот же queued sink, что у local player.

### Остатки

1. nested observation collections не hard-immutable;
2. terrain/visibility не входят в atomic snapshot;
3. event visibility раскрывает скрытый endpoint;
4. advertised command vocabulary содержит unusable legacy command и не содержит всех research intents;
5. Headless реализует fixed stub, а не injectable bot policy;
6. нет scenario goals, bot cadence/seed contract, replay ingestion и rejection feedback loop.

**Вывод:** API уже годится для bot experiments, но не завершён как стабильный fair-agent contract. Оценка **6.0/10**.

---

## 9. Приоритетный roadmap

### P0 — correctness / determinism

1. Провести `GameplayTablesCatalog` через все Core systems и SFML вместо `Embedded`.
2. Хешировать research remainders и любой cache, влияющий на future behavior.
3. Инвалидировать factory idle cache при research/capability changes.
4. Определить canonical conflicting-duplicate policy; network ingress привязывает actor к session.
5. Исправить tracer/event visibility, не раскрывая второй скрытый endpoint.
6. Создавать SFML build menu из match `BuildCostCatalog`.
7. Сделать все authoritative catalog graphs глубоко immutable.

### P1 — protocol / scale

1. Закрыть `with` immutability bypass.
2. Довести command vocabulary и full-payload round-trip tests.
3. Добавить strict schema/cross-catalog/session validation.
4. Сделать benchmark budgets hard и расширить matrix.
5. Агрегировать minimap dirty regions между render frames.
6. Сделать observation frame self-contained и immutable.

### P2 — responsibility / extensibility

1. Вынести host file I/O из Core.
2. Разделить SFML frame host, state machine, pure mapper и presentation snapshot.
3. Продолжить extraction systems из `GameSimulation`.
4. Перейти от enum ordinal identity к stable content IDs там, где нужен modding.
5. Добавить replay/save/session manifests и cross-platform hash comparison.

---

## 10. Финальный вывод

После PR #111 проект стал сильнее как deterministic MVP: production command path, integer coordinates, observation DTO, spatial/performance infrastructure и тестовая база — реальные улучшения.

Но формальное «R01–R34 complete» смешивает три разных состояния:

- инфраструктура добавлена;
- happy path покрыт;
- исходный end-to-end DoD закрыт.

Для R17/R18 и большинства partial items выполнены первые два пункта, но не третий. Текущий статус:

> Хорошо тестируемый deterministic prototype с сильным фундаментом для Headless/ботов и следующего lockstep этапа, но с High content-authority, hash, command-order и FoW gaps. Не network-ready и не полностью data-driven.

