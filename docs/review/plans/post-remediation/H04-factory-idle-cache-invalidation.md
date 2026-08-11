# H04 — Factory idle-cache invalidation after research unlock

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § H4  
**Severity:** High · **Домен:** gameplay correctness / determinism · **Roadmap:** P0  
**Related residual:** R17 (open)  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

Manual factory может навсегда остаться в idle-skip после unlock рецепта:

1. `TrySetFactoryProduction` позволяет выбрать recipe, даже если он ещё locked.
2. Idle tick видит locked recipe → `RememberIdleFactorySkip` записывает epoch + input version.
3. Последующие ticks: epoch/input совпали → `continue` **до** unlock check.
4. Research completion синкает max health, но **не** bump'ает `_armyAccountingEpoch`.
5. Cache fields влияют на future behavior, но не входят в state hash.

---

## Доказательства

| Участок | Что происходит |
|---|---|
| `FactoryBastionSystem.cs:56-63` | Idle skip before unlock/capacity checks |
| `FactoryBastionSystem.cs:71-80` | Locked recipe → RememberIdleFactorySkip |
| `FactoryBastionSystem.cs:109-113` | RememberIdleFactorySkip writes epoch/version |
| `GameSimulation.cs:818-852` | Manual target set without unlock gate |
| `GameSimulation.cs:45,255` | `_armyAccountingEpoch` / bump |
| `ResearchTickSystem.cs:16-20` | ProcessResearch + SyncAllResolvedMaxHealth only |
| `WorldEntity.cs:65-74` | IdleFactorySupplyEpoch / IdleFactoryInputVersion |
| `SimulationStateHasher.WriteEntity` | Cache fields omitted |

---

## Почему R17 «closed» неполный

R17 добавил scratch buffers + idle-skip epoch для perf. Correctness invariant «skip only when outcome unchanged» нарушен: research unlock меняет outcome без epoch bump. Также derived cache стал behavior-affecting without hash.

---

## Корневая причина

Invalidation set слишком узкий: только army accounting + input buffer mutation. Missing: research capability/version, template capacity modifiers, maybe power/energy policy changes.

---

## Целевой дизайн (выбрать один)

### Вариант A — Research version epoch (предпочтительный)

```text
PlayerResearchState.ContentEpoch or Simulation.ResearchCapabilityEpoch
  incremented on any unlock/modifier/capability change
Factory idle skip key = (armyEpoch, inputVersion, researchEpoch[owner])
```

### Вариант B — Don't skip when waiting on unlock

If manual target exists and recipe locked → do not RememberIdleFactorySkip; re-check every tick (cheap compared to wrong freeze).

### Вариант C — Gate SetFactoryProduction

Reject locked recipes at set-time. Still need invalidation if tech completes while waiting for capacity/inputs.

**Рекомендация:** A + C вместе (defense in depth). Derived caches remain non-authoritative: after load/hash compare, caches rebuild from world (or bump epoch on load). Do **not** put caches in hash if they are pure derived — but then must guarantee rebuild. Safer short-term: invalidate correctly so behavior matches recomputation.

---

## План фикса

1. Добавить `BumpResearchCapabilityEpoch()` вызываемый из research completion / unlock / modifier apply paths.
2. В idle skip сравнивать также owner research epoch (или global).
3. `TrySetFactoryProduction`: reject if recipe not unlocked for owner (align with factory start gates).
4. Unit test reproducing freeze:
   - set manual locked recipe;
   - advance ticks (skip armed);
   - force-complete required tech;
   - assert production starts / work ticks become > 0 within 1 tick.
5. Dual-run / hash: after fix, behavior deterministic; caches either rebuilt or invalidated — document that caches are non-authoritative.
6. Optional: clear idle cache fields on research epoch bump for clarity.

---

## Тесты

- `FactoryBastionAccountingTests.ManualLockedRecipe_UnlocksAndStarts`
- `FactoryBastionAccountingTests.SetFactoryProduction_RejectsLockedRecipe`
- Regression: autofill still re-resolves every tick
- Regression: idle skip still skips when truly unchanged (perf)

---

## Риски / non-goals

**Риски:** чуть больше work per research completion (epoch bump + factories re-evaluate once) — acceptable.  
**Non-goals:** full factory accounting rewrite; putting cache fields into hash unless chosen as authoritative.

---

## Definition of Done

- [ ] Reproduced freeze has a failing test before fix and passing after.
- [ ] Unlock / capability changes invalidate idle skip.
- [ ] Locked recipe cannot be sticky-skipped forever.
- [ ] R17 correctness DoD closed; STATUS updated.
- [ ] verify green.

---

## Верификация

```powershell
dotnet test tests/SteelConveyorWar.Core.Tests -c Release --filter FactoryBastion
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
```

## Специалисты

`core-simulation`, `combat-bastion` / logistics-build as needed, `multiplayer-correctness`, `test-ci`.
