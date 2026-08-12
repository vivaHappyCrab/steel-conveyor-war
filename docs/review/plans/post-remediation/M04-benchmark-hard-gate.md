# M04 — Hard performance regression gate + realistic matrix

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M4  
**Severity:** Medium · **Домен:** CI / performance · **Roadmap:** P1  
**Related residual:** R22 (partial), R07/R15/R16/R17/R33 evidence gaps  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

Quick benchmark при превышении soft budget всё равно exit 0, пока не выставлен `SCW_BENCH_HARD_GATE=1`. CI переменную не ставит. Budgets 50ms/5MB слишком широки. Matrix мала; `fow` дублирует army; A* setup до measurement.

R22 DoD «automatic regression gate» не выполнен.

---

## Доказательства

- `tests/SteelConveyorWar.Benchmarks/Program.cs:21-38`
- `.github/workflows/ci.yml:122-127`
- `TickScenarioRunner.cs:10-23,29-50`

---

## План фикса

### Phase 1 — make gate real

1. Collect baselines on CI (artifact JSON) for 1–2 weeks soft.
2. Set calibrated p95/alloc budgets (~3–5× measured p95, not 90×).
3. CI: `SCW_BENCH_HARD_GATE=1` on `benchmark-quick` (or separate required job).
4. Document override for local experimentation.

### Phase 2 — matrix

Add scenarios from review §6 table (start with subset if runtime budget limited):

| Scenario | Scale (minimum) |
|---|---|
| idle_factory | 500 buildings |
| army_move | 100 units (measure includes pathing) |
| battle | 50v50 |
| power_equal | 200 equal-ratio consumers, high production |
| fow_moving | 100 moving vision sources (dedicated) |
| hash | large state Compute cost |

5. Move path enqueue after warm-up or include intentional pathing window in measured ticks.
6. Optional: compare against committed baseline JSON with % tolerance.

### Phase 3 — SFML render bench (can be separate job)

Frame p95 + managed/native alloc with explored map.

---

## Тесты / CI evidence

- Job fails when budgets exceeded.
- PR template links to benchmark artifact.
- Local `eng/verify.ps1` may keep soft; CI hard.

---

## Definition of Done

- [ ] Default CI fails on budget exceed.
- [ ] Budgets calibrated from real measurements.
- [ ] Matrix includes non-duplicated FoW and larger army/power cases.
- [ ] R22 marked closed.

## Специалисты

`test-ci`, `core-simulation`, `sfml-platform`.
