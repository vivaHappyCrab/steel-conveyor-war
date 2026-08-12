# Четвёртое ревью кода — Steel Conveyor War

**Дата:** 2026-08-12  
**Проверенный commit:** `c31ba95` (`develop`, merge PR #114)  
**Связанный issue:** [#61 — Полный аудит архитектуры, производительности и расширяемости](https://github.com/vivaHappyCrab/steel-conveyor-war/issues/61)  
**Предыдущие ревью:**  
- [`CODE_REVIEW_REPEAT_2026-08-10.md`](CODE_REVIEW_REPEAT_2026-08-10.md)  
- [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) (третье)  
**Контекст:** проверка после merge планов post-remediation H01–H07 / M01–M11 / L01–L03 (PR #114)  
**Метод:** те же критерии, что в третьем ревью; независимая сверка DoD по планам `docs/review/plans/post-remediation/`; spot-check ключевых call sites; полная `eng/verify.ps1`

---

## 1. Итог

PR #114 закрывает каталог проблем третьего ревью end-to-end, а не только инфраструктурой:

- match `GameplayTables` исполняются в Core systems и SFML (не только хешируются);
- research remainders входят в state hash (`AlgorithmVersion = 9`);
- canonical conflict policy + `BoundPlayerCommandSink` на Client/Headless;
- factory idle-cache просыпается после research unlock (`CapabilityEpoch`);
- build menu compose из match `BuildCostCatalog`;
- tracer/events — Policy B без точного hidden endpoint;
- catalogs deep-freeze + cast-mutation tests;
- Medium/Low набор (protocol, schema, bench hard gate, Hosting I/O, observation frame, session manifest, SFML/UX polish) реализован.

Запись STATUS «все post-remediation H/M/L закрыты» **подтверждается кодом**. Старый блок «R01–R34 = 34/34» из волны PR #111 по-прежнему смешивает инфраструктуру с DoD; после #114 большинство partial/open из третьего ревью действительно закрыты. Честные остатки: **R01** (trusted `Try*` с `actor = null`) и **R34** (tiles не участвуют в cross-graph).

### Severity summary

| Severity | Третье ревью | Сейчас |
|---|---:|---:|
| Critical | 0 | **0** |
| High | 7 | **0** |
| Medium | 11 | **3** (остаточные) |
| Low | 3 | **6** (новые/пониженные polish) |

Post-remediation plan set: **21/21 CLOSED** (H01–H07, M01–M11, L01–L03).

### Оценка

| Критерий | После #111 (3-е) | Сейчас (#114) | Комментарий |
|---|---:|---:|---|
| Архитектурные паттерны | 7.0 | **8.0** | Authority pipeline, bound sink, Hosting boundary, session manifest |
| Производительность | 6.0 | **7.0** | Hard CI gate + matrix; level water-fill; ≤2 spatial rebuilds; alloc polish остаётся |
| Расширяемость для сети | 4.0 | **6.5** | Identity/hash/content/session blockers третьего ревью сняты; нет transport/auth/replay |
| Расширяемость для ботов | 6.0 | **7.5** | `CaptureFrame` + fog board + H06-safe events + vocab filter |
| Разделение ответственности | 6.0 | **7.5** | Hosting I/O; SimulationPump / PresentationComposer; `GameSimulation` всё ещё широк |
| **Итого** | **5.8** | **7.4** | Deterministic MVP пригоден к lockstep prototype; не untrusted-network product |

Отдельная оценка data-driven/content extensibility: **7.0/10** (было 4.0). Tables/build costs authoritative; timing/kind predicates и часть SFML classification всё ещё code-owned через `MvpDefinitions`.

---

## 2. Верификация

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
```

Результат на `c31ba95`:

- restore/format: pass;
- Release build: pass, **0 warnings / 0 errors**;
- Core tests: **437/437**;
- Hosting tests: **4/4**;
- SFML tests: **76/76**;
- Headless smoke: pass (90 ticks, 6 commands);
- Client smoke: pass.

Ограничения доказательств (как раньше):

- coverage threshold не enforced;
- hard bench gate включён в CI (`SCW_BENCH_HARD_GATE=1`), локальный default soft;
- нет автоматического cross-OS state-hash compare;
- нет SFML frame-time / image-equivalence bench;
- статический аудит ≠ network fuzz / soak / profiler.

---

## 3. Статус post-remediation H / M / L

| ID | Статус | Доказательство (кратко) |
|---|---|---|
| H01 | ✅ | Systems/SFML → `GameplayTables`; `GameplayTablesAuthorityTests` |
| H02 | ✅ | Remainders в `WriteResearch`; `AlgorithmVersion = 9` |
| H03 | ✅ | Canonical compare + dup reject; `BoundPlayerCommandSink` |
| H04 | ✅ | `CapabilityEpoch` + no sticky skip; unlock wake test |
| H05 | ✅ | Session `ComposeFrom(simulation.BuildCostCatalog)` |
| H06 | ✅ | Shared `CombatShotVisibility` Policy B |
| H07 | ✅ | `ContentFreeze` + nested cast-mutation tests |
| M01 | ✅ | Get-only immutable payloads; `ImmutablePayloadTests` |
| M02 | ✅ | `SetProjectWeightCommand`; vocab filter; batch protocol tests |
| M03 | ✅ | Expanded `ContentCrossValidator` + strict JSON; tiles unused by design |
| M04 | ✅ | CI hard gate + expanded matrix |
| M05 | ✅ | Level water-fill for equal `(buffer,capacity)` |
| M06 | ✅ | ≤2 shared spatial rebuilds/tick; combat shares index |
| M07 | ✅ | Frame fog dirty union in `SimulationPump` |
| M08 | ✅ | Resource dispose + pump/composer split; mapper purity partial |
| M09 | ✅ | `SteelConveyorWar.Hosting` owns file I/O |
| M10 | ✅ | `CaptureFrame` / `FogBoardView` / frozen nests |
| M11 | ✅ | `SimulationSessionManifest`; map bounds/overlap; `Draw` |
| L01 | ✅ | Trailing `--local-player` errors |
| L02 | ✅ | Dead commander select → no-op |
| L03 | ✅ | Exclusive UI modes include build |

---

## 4. Статус R01–R34 после #114

Переоценка только пунктов, которые третье ревью держало 🟡/🔴 (остальные ✅ без регрессии).

| ID | 3-е ревью | Сейчас | Остаток |
|---|---|---|---|
| R01 | 🟡 | 🟡 | Hosts bind actor; public `Try*` всё ещё `actor = null` opt-out |
| R03 | 🟡 | ✅ | Canonical `(Actor, Sequence, Kind, payload)` + dup reject |
| R04 | 🟡 | ✅ | Frozen observation / `CaptureFrame` |
| R05 | 🟡 | ✅ | Deep-freeze catalogs |
| R07 | 🟡 | ✅ | ≤2 rebuilds/tick + shared combat index (alloc polish Low) |
| R10 | 🟡 | ✅ | Session manifest covers TPS/map/roster/seed |
| R11 | 🟡 | ✅ | Vocab + project weight + batch tests |
| R12 | 🟡 | ✅ | `with` collection bypass закрыт |
| R13 | 🟡 | ✅ | Pending hash + conflict policy согласованы |
| R15 | 🟡 | ✅ | Level water-fill |
| R16 | 🟡 | ✅ | Moved-source dirty disks |
| R17 | 🔴 | ✅ | CapabilityEpoch invalidation |
| R18 | 🔴 | ✅ | Match tables authoritative |
| R19 | 🟡 | ✅ | Atomic fog+entity frame |
| R21 | 🟡 | ✅ min DoD | Pump/composer; mapper purity / `Run` size residual |
| R22 | 🟡 | ✅ | Hard CI gate |
| R25 | 🟡 | ✅ | Hosting I/O |
| R26 | 🟡 | ✅ hosts | Enemy lab closed earlier; `Try*` null-actor residual = R01 |
| R27 | 🟡 | ✅ | Policy B tracers |
| R29 | 🟡 | ✅ | Remainders hashed |
| R30 | ✅ | ✅ | Runtime build menu |
| R31 | 🟡 | ✅ | Bounds/overlap + Draw |
| R32 | 🟡 | ✅ | TPS in session identity |
| R33 | 🟡 | ✅ | Catch-up dirty union |
| R34 | 🟡 | 🟡 | Unlocks/schema stricter; **tiles** still unused in cross-graph |

**Строгий счёт после #114:** полностью закрыто по исходному DoD **~32/34**; частично **R01, R34** (и узкие residual notes у R07/R21 как Low polish, не reopen).

---

## 5. Каталог актуальных проблем

### High

Нет подтверждённых High findings.

### Medium

#### M-A. Trusted immediate `Try*` APIs допускают `actor = null`

Production hosts используют `BoundPlayerCommandSink`, но публичные `GameSimulation.Try*` сохраняют optional `actor = null` (например `GameSimulation.cs:359`, `:426`, `:524`, `:756`, `:1283+`). Сетевой/бот host, вызывающий immediate API напрямую, обходит seat binding.

**Риск:** footgun для будущего untrusted ingress.  
**Связь:** R01.

#### M-B. Нет transport / replay / durable ledger

Session manifest, pending hash и bound sink готовы к lockstep prototype, но отсутствуют: wire transport, rate/size limits, authenticated session beyond local bind, save/load, durable command ledger, automated cross-OS hash compare.

**Риск:** блокирует product multiplayer, не ломает local MVP.

#### M-C. `GameSimulation` остаётся широким orchestration facade

Systems выделены, tick pipeline яснее (`≤2` spatial rebuilds), но command/state/query поверхность всё ещё сосредоточена в одном типе. SFML частично читает `MvpDefinitions` для classification (не tables authority).

**Риск:** стоимость изменений и accidental coupling; не correctness defect.

### Low

1. **`MvpDefinitions` gameplay accessors** всё ещё делегируют в Embedded и не `[Obsolete]` — риск silent regress если новый код пойдёт в facade.
2. **`WorldEntity` ctor** seed MaxHealth из Embedded; owned entities синхронизируются позже.
3. **Tiles catalog** не участвует в cross-validator graph (`ContentCrossValidator.cs:48-50`).
4. **`GetEntitiesAt`** всё ещё аллоцирует/сортирует; hot placement уже на `AnyAliveAt`.
5. **SFML `SfmlPlaySession.Run` / mapper purity** — M08 residual.
6. **Obsolete `AssignFactoryBastion` enum** retained for wire numbers; filtered from vocabulary.
7. **Partial combat events** scrub coords but keep entity ids (acceptable; document).
8. Plan `.md` DoD checkboxes для части M* устарели (unchecked) при закрытом STATUS — doc drift.

---

## 6. Сеть

### Foundation (готово)

- deterministic fixed tick + millitile coords;
- queued production input + sequence/version/result;
- canonical conflict policy + bound actor sink;
- state + pending hashes including research remainders;
- content + **session** manifests (TPS/map/roster/seed);
- Hosting bootstrap + Headless host;
- multi-OS CI + hard bench gate.

### Блокеры (обновлено)

1. ~~authenticated session actor~~ → local bind есть; **untrusted transport auth отсутствует**;
2. ~~conflicting duplicate keys~~ → **закрыто**;
3. ~~research remainder / factory cache hash gaps~~ → remainders hashed; idle cache derived+invalidated;
4. ~~loaded ≠ executed content~~ → **закрыто** для gameplay tables;
5. ~~session manifest map/roster/TPS~~ → **закрыто**;
6. ~~payload `with` bypass~~ → **закрыто**;
7. нет durable replay/result ledger и save/load;
8. нет transport limits/rate limits;
9. cross-OS hash печатается, но не сравнивается автоматически;
10. ~~hidden tracer endpoints~~ → **закрыто** (Policy B);
11. **новое:** immediate `Try*` null-actor footgun (M-A).

**Вывод:** Core готов к **trusted lockstep prototype**. Оценка сети **6.5/10** (было 4.0). Не ready для untrusted internet play.

---

## 7. Боты

### Готово

- Core-only / Headless;
- fair/cheat modes;
- frozen observation frame + fog board;
- H06-safe combat events;
- filtered command vocabulary + project weights;
- тот же bound queued sink, что у local player.

### Остатки

1. Headless — fixed stub, не injectable bot policy;
2. нет scenario goals / cadence-seed contract / rejection feedback loop;
3. entity ids на partial events;
4. immediate `Try*` bypass если bot обходит sink.

**Вывод:** fair-agent contract заметно сильнее. Оценка **7.5/10** (было 6.0).

---

## 8. Приоритетный roadmap (после #114)

### P0

Нет correctness/determinism blockers уровня третьего ревью.

### P1 — network / trust hardening

1. Убрать или запечатать `actor = null` на production `Try*` (require actor / internal-only).
2. Transport prototype: tick envelopes, size/rate limits, seat auth.
3. Automated cross-OS hash compare в CI.
4. Save/load или durable command ledger для mid-match join.

### P2 — polish / extensibility

1. `[Obsolete]` на Embedded gameplay accessors `MvpDefinitions`; запретить новые call sites.
2. Подключить tiles к cross-catalog или явно исключить из match identity docs.
3. Non-alloc / pooled `GetEntitiesAt` consumers; дальше снижать spatial work.
4. Продолжить extraction из `GameSimulation`; pure SFML mapper.
5. Stable content IDs для modding beyond enums.
6. Injectable Headless bot policy + scenario harness.

---

## 9. Финальный вывод

После PR #114 проект перешёл из состояния «инфраструктура remediation без end-to-end DoD» в состояние **согласованного deterministic MVP**:

> Content authority, hash surface, command identity, FoW observation, catalog immutability и host boundary соответствуют заявленным планам. Остаются trust/API hygiene (R01), transport/replay и крупные maintainability/perf polish items — не High correctness gaps.

Рекомендация: принять STATUS post-remediation close-out как подтверждённый; следующий продуктовый фокус — **P1 network trust** и закрытие R01, а не повторная волна H-findings.
