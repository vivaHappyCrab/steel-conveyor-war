# M06 — Снизить spatial rebuilds и hot-path allocations

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M6  
**Severity:** Medium · **Домен:** performance · **Roadmap:** P1/P2  
**Related residual:** R07 (partial), R16 (partial)  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

За idle tick spatial index rebuild вызывается многократно (commander build/demolish/move, movement, combat). `GetEntitiesAt` аллоцирует/сортирует. Path reconstruction аллоцирует списки. Power создаёт dictionaries/heaps каждый tick. FoW moved sources всё ещё широко перекрашивают; signature rebuilds тяжёлые.

R07/R16 добавили index/dirty foundations, но performance DoD (large-scale evidence) открыт.

---

## Доказательства

- `CommanderOrdersSystem.cs:19-22,47-50,70-73` — rebuild ×3
- `MovementSystem.cs:16-19` — rebuild
- `CombatSystem.cs:245-251` — rebuild
- `GameWorld.cs:73-103` — `GetEntitiesAt` alloc/sort
- FoW / Power systems — per-tick structures

---

## План фикса

1. **Single rebuild per tick phase:** `AdvanceTick` builds/refreshes `_spatialQueryIndex` once after commands (or once before movement+combat cluster); systems receive shared index.
2. Commander orders share that index instead of rebuilding each subprocess.
3. `GetEntitiesAt`: return pooled buffer / span API for hot paths; keep allocating API for rare callers.
4. Pathfinding: ensure `PathfindingWorkspace` reuses path/candidate lists end-to-end (audit remaining `new List`).
5. Power: reuse per-player consumer lists/heaps across ticks (clear/reuse).
6. FoW: event-driven dirty for moved sources only; avoid full player repaint when possible; replace quadratic signature sorts with incremental structures where feasible.
7. Gate with M04 large army/FoW/power scenarios.

Do not claim closure without measured improvement.

---

## Тесты

- Dual-run determinism after rebuild consolidation
- SpatialQueryIndexTests regression
- Benchmark before/after artifacts attached to PR

---

## Definition of Done

- [ ] ≤1 full spatial rebuild per tick (or documented phase- rationalized ≤2).
- [ ] Hot `GetEntitiesAt` path allocation-free or pooled.
- [ ] Bench evidence on 100+ units / FoW scenario.
- [ ] R07/R16 performance residuals updated.

## Специалисты

`core-simulation`, `test-ci`, `multiplayer-correctness` (determinism gate).
