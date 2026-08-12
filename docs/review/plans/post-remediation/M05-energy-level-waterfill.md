# M05 — Level-based energy water-fill для equal-ratio case

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M5  
**Severity:** Medium · **Домен:** performance · **Roadmap:** P1/P2  
**Related residual:** R15 (partial)  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

Batched fill сравнивает target только с **следующим** heap competitor. При одинаковых capacity/buffer после +1 unit target перестаёт быть minimum → common equal-ratio case остаётся ~`O(energy × log C)`.

Correctness equivalence к per-unit algorithm закрыта; performance DoD — нет.

---

## Доказательства

- `PowerSystem.cs:243-280` — batched loop vs **одного** next heap competitor
- `PowerSystem.cs:289-327` — `CountConsecutivePreferredUnits`
- Equal empty buffers / equal caps → `batch == 1`: при `bufA=bufB=0`, `capA=capB`, `idA < idB` → `maxPreferredT = 0` → `units = 1` (строки 303–327)
- Per tick также: новый `PriorityQueue` + scan (`:155-177`); per-player kind dictionaries (`:133-145`)
- `EnergyBatchedWaterfillTests` — equivalence (включая equal-ratio), **не** complexity/bench
- Quick bench `power` (80 assemblers) слишком мал / не equal-ratio stress

---

## План фикса

1. Group consumers by identical `(buffer, capacity)` ratio class (or exact buffer/capacity key for equal start).
2. Level water-fill: distribute `min(remaining, groupCount * deltaToNextLevel)` across group in O(group) / O(log levels).
3. Preserve deterministic id tie-break within group (ascending Id).
4. Keep equivalence tests vs per-unit oracle for random and equal cases.
5. Add benchmark scenario `power_equal` with high produced energy (M04).
6. Document complexity target: independent of produced energy for equal-ratio plateaus.

---

## Тесты

- Equivalence: equal capacities, large energy
- Equivalence: mixed capacities (existing)
- Deterministic order of partial fills by Id
- Benchmark evidence under hard gate

---

## Definition of Done

- [x] Equal-ratio high-energy case not per-unit in practice (bench proof).
- [x] Equivalence tests still green.
- [x] R15 performance residual closed.

## Специалисты

`core-simulation`, `test-ci`.
