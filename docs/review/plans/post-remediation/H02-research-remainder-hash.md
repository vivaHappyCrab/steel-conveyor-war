# H02 — Хешировать research scheduler remainders

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § H2  
**Severity:** High · **Домен:** determinism / lockstep · **Roadmap:** P0  
**Related residual:** R29 (partial), R13 (pending surface)  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

Largest-remainder scheduler хранит persistent accumulators `_trackSelectionRemainder` и `_projectSelectionRemainder`. Они определяют следующий track/project на lab cycle, но `SimulationStateHasher.WriteResearch` их не пишет.

Две симуляции могут иметь одинаковый state hash и на следующем consume выбрать разные проекты → silent desync после «совпадения» hash.

---

## Доказательства

| Участок | Что происходит |
|---|---|
| `src/SteelConveyorWar.Core/Research/PlayerResearchState.cs:39-43` | Accumulators объявлены |
| `...PlayerResearchState.cs:95-97` | Internal accessors |
| `src/SteelConveyorWar.Core/Research/ResearchSystem.cs:441-476` | Selection использует remainders |
| `...ResearchSystem.cs:479-515` | `SelectByLargestRemainder` мутирует accumulator |
| `src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs:237-280` | WriteResearch: progress/tracks/weights/modifiers — **без** remainders |
| `SimulationStateHasher.AlgorithmVersion = 8` | Потребует bump |

---

## Почему R29 «closed» неполный

R29 исправил last-entry bias fair allocation и добавил remainders. DoD детерминизма распределения закрыт для одинакового начального состояния, но desync detector не видит divergence, если remainders уже разошлись (save/load, mid-match join, truncated snapshot).

---

## Корневая причина

Remainders — derived-looking counters, но они **не восстанавливаются** однозначно из progress/weights без истории selection. Значит это authoritative hidden state.

---

## Целевой дизайн

1. `WriteResearch` пишет оба map в каноническом порядке ключей.
2. Bump `AlgorithmVersion` (9).
3. Любой future save/load/replay включает remainders.
4. Тест: разные remainders → разный hash; одинаковые → одинаковый следующий выбор.

---

## План фикса

1. В `WriteResearch` после tracks/weights:
   - count + ordered `(trackId, remainder)` для `TrackSelectionRemainder`;
   - count + ordered `(technologyId.Value, remainder)` для `ProjectSelectionRemainder`.
2. Использовать `StringComparer.Ordinal` / technology id ordinal как в остальных research maps.
3. `AlgorithmVersion++` (8 → 9); обновить ADR/docs notes.
4. Добавить `ResearchRemainderHashTests`:
   - clone state, mutate only remainder → hash differs;
   - two sims with same remainder advance one lab cycle → same selected project + same resulting remainder;
   - dual-run unchanged for default start (remainders empty/zero).
5. Если есть presentation snapshot research — remainders **не** обязаны попадать в bot observation (internal scheduler), только в authoritative hash/save.

---

## Тесты

- `DeterminismHashTests` / новый `ResearchRemainderHashTests.cs`
- Сценарий: force identical progress/weights, different remainder → hash mismatch
- Сценарий: after N pack consumptions, dual-run hash equal including remainders
- Golden/hash version note: AlgorithmVersion 9

---

## Риски / non-goals

**Риски:** ломает совместимость hash с AlgorithmVersion 8 (ожидаемо).  
**Non-goals:** менять сам largest-remainder algorithm; выставлять remainders в UI.

---

## Definition of Done

- [ ] Оба remainder dictionaries входят в state hash.
- [ ] AlgorithmVersion bumped и задокументирован.
- [ ] Есть тест «разный remainder → разный hash и/или разный следующий выбор».
- [ ] Dual-run / verify green.
- [ ] R29 отмечается закрытым по hash surface.

---

## Верификация

```powershell
dotnet test tests/SteelConveyorWar.Core.Tests -c Release --filter "FullyQualifiedName~ResearchRemainder|FullyQualifiedName~DeterminismHash"
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
```

## Специалисты

`multiplayer-correctness`, `core-simulation`, `test-ci`.
