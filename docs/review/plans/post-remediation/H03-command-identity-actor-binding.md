# H03 — Canonical command identity / network actor binding

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § H3  
**Severity:** High · **Домен:** lockstep / trust boundary · **Roadmap:** P0 (Core contract) + P1 (session binding stub)  
**Related residual:** R03 (partial), R01 (partial), R11 (partial), R13 (partial)  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

Tick apply сортирует по `(Actor, Sequence)`, но:

1. `Sequence == 0` — unsequenced, без canonical tie-break.
2. `List<T>.Sort` **нестабилен** → при равных ключах порядок **неопределён** (не «stable FIFO»).
3. Duplicate suppression window живёт только внутри одного tick.
4. Sequence state локален каждому `DeferredCommandSink` instance.
5. Sink доверяет `command.Actor` (client-claimed identity).

Для local/headless это ещё не exploit. Для будущего untrusted network — blocker.

---

## Доказательства

| Участок | Что происходит |
|---|---|
| `GameSimulation.Commands.cs:126-145` | Sort + per-tick dedupe |
| `GameSimulation.Commands.cs:128-131` | Комментарий про «stable OrderBy», фактически `List.Sort` (unstable) |
| `GameSimulation.Commands.cs:178-190` | `CompareCanonical` только Actor/Sequence |
| `GameSimulation.Commands.cs:33-44` | `EnqueueCommand` — **нет** actor auth |
| `DeferredCommandSink.cs:19-20,41-54,59-65` | Per-sink sequence; trusts `command.Actor` |
| `SimulationStateHasher.cs:89-102` | Pending hash тоже `(Tick,Actor,Sequence)` без tie-break payload |
| `CommandQueueTests` | Distinct sequences / fixed-order duplicate; **не** conflicting payloads + opposite arrival |

Комментарий в коде утверждает «OrderBy is a stable sort» — фактически используется `List.Sort`, который нестабилен. Документацию/ожидания нужно исправить вместе с кодом.

---

## Почему R03 «closed» неполный

R03 добавил Sequence и sort для **уникальных** non-zero keys. DoD «порядок не зависит от arrival» не выполнен для collisions / Sequence 0 / multi-sink / network identity.

---

## Целевой дизайн

### A. Deterministic apply policy (Core, сейчас)

```text
Sort key: (Actor.Value, Sequence, Kind, payloadCanonicalHash)
Conflict policy for identical (Actor, Sequence):
  - reject all but first by sort key (deterministic), OR
  - reject entire key group as malformed
Network path: Sequence must be > 0
Local legacy: sinks always assign Sequence >= 1
```

Рекомендация: **reject duplicates with same (Actor, Sequence) after first by full canonical payload order**, record `CommandRejection`, never depend on unstable sort.

Дополнительно: включить `Kind` + stable payload fingerprint в compare, чтобы равные Sequence с разным payload давали определённый winner/reject.

### B. Session-bound actor (host contract, stub OK)

```text
IPlayerCommandSink.Enqueue(command)
  -> SessionCommandIngress(authenticatedPlayerId)
       overwrites/rejects mismatched command.Actor
```

Даже без network transport: ввести `BoundPlayerCommandSink(PlayerId boundActor)` wrapper, используемый SFML/Headless. Core `DeferredCommandSink` может остаться low-level.

### C. Cross-tick dedupe (optional P1)

Sliding window of recently applied `(Actor, Sequence)` for N ticks / ledger for replay. Не обязательно для закрытия H03 Core portion, но нужно для replay integrity (связка R13).

---

## План фикса

1. Исправить комментарий/docs: `List.Sort` нестабилен.
2. Запретить `Sequence <= 0` на production sink path (уже начинается с 1 — enforce в `EnqueueCommand` для non-test, или reject at apply with rejection reason).
3. Canonical compare: `(Actor, Sequence, Kind, StablePayloadKey)`.
4. Conflict policy + tests:
   - same key, different payload, opposite arrival orders → identical apply outcome;
   - duplicate identical payload → applied once;
   - Sequence 0 rejected or isolated test-only path.
5. Добавить `BoundPlayerCommandSink` / `SessionActorCommandSink`:
   - SFML local player binding;
   - Headless stub AI player binding;
   - mismatch → reject before enqueue.
6. Pending hash использует тот же canonical ordering/payload key.
7. Документировать network requirement: transport must not trust client Actor.

PR slice:

- PR1: Core sort/dedupe/tests (closes deterministic portion).
- PR2: Bound actor sink + host wiring.
- PR3 (optional): cross-tick ledger.

---

## Тесты

- `CommandQueueTests.ConflictingSameKey_OppositeArrival_SameResult`
- `CommandQueueTests.SequenceZero_RejectedOnProductionPath`
- `DeferredCommandSinkTests.AlwaysAssignsPositiveSequence`
- `BoundActorSinkTests.RejectsMismatchedActor`
- Pending hash equality independent of enqueue order for conflicting keys

---

## Риски / non-goals

**Риски:** изменение compare может сдвинуть apply order для уже неоднозначных кейсов (хорошо — сейчас undefined).  
**Non-goals:** полный network transport, crypto auth, rate limiting (см. review §7 blockers 7–8).

---

## Definition of Done

- [ ] Equal-key resolution deterministic и покрыт reverse-arrival тестом.
- [ ] Production path не использует Sequence 0.
- [ ] Hosts enqueue через actor-bound sink.
- [ ] Документация не утверждает stable `List.Sort`.
- [ ] R03/R01 residual по identity отмечены закрытыми для Core contract.

---

## Верификация

```powershell
dotnet test tests/SteelConveyorWar.Core.Tests -c Release --filter "FullyQualifiedName~CommandQueue|FullyQualifiedName~DeferredCommand|FullyQualifiedName~BoundActor|FullyQualifiedName~Ownership"
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
```

## Специалисты

`multiplayer-correctness`, `core-simulation`, `sfml-platform`, `test-ci`.
