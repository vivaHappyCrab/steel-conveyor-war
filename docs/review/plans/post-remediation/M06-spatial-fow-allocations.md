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

Shared `GameSimulation._spatialQueryIndex` rebuilds в одном tick:

| Call | Файл |
|---|---|
| Commander build/demolish/move | `CommanderOrdersSystem.cs:21`, `:49`, `:72` |
| Factory spawn tile / bastion AI | `FactoryBastionSystem.cs:451`, `:511` |
| Movement | `MovementSystem.cs:18` |
| Placement helper | `GameSimulation.cs:922` |

Плюс **отдельный** combat index: `CombatSystem.cs:250` (свой `SpatialQueryIndex`).

Дополнительно:

- `GameWorld.cs:73-103` — `GetEntitiesAt` alloc/sort (placement `:1731`, `:1928`; hot path уже имеет `AnyAliveAt`)
- `MovementSystem.cs:268-279` — `ReconstructPath` всё ещё `new List` несмотря на workspace
- `MovementSystem.cs:239-261` — candidate rings: `new List` + LINQ OrderBy
- FoW: static skip OK (`FogOfWarSystem.cs:55-65`); при движении — full decay/paint (`:68-118`); signatures rebuild+sort (`:239-368`)

---

## План фикса

1. **Single rebuild per tick phase:** `AdvanceTick` refreshes shared index once after commands (or once before movement+combat cluster); systems receive shared index. Убрать redundant Rebuild в commander/factory/movement.
2. Combat: либо шарить тот же index, либо документировать second pass как обязательный (сейчас отдельный).
3. Placement: prefer `AnyAliveAt` / non-alloc enumerator; keep sorted `GetEntitiesAt` for rare/API needs.
4. Pathfinding: pooled path buffer; drop LINQ в candidate selection; workspace end-to-end.
5. Power: reuse per-player consumer lists/heaps across ticks (clear/reuse) — связка с M05.
6. FoW: invalidate only moved/created/destroyed sources; paint dirty tiles only; keep hash-equivalent order.
7. Gate with M04 large army/FoW/power scenarios.

Do not claim closure without measured improvement.

---

## Тесты

- Dual-run determinism after rebuild consolidation
- SpatialQueryIndexTests regression
- Benchmark before/after artifacts attached to PR

---

## Definition of Done

- [x] ≤1 full spatial rebuild per tick (or documented phase- rationalized ≤2).
- [x] Hot `GetEntitiesAt` path allocation-free or pooled.
- [x] Bench evidence on 100+ units / FoW scenario.
- [x] R07/R16 performance residuals updated.

## Специалисты

`core-simulation`, `test-ci`, `multiplayer-correctness` (determinism gate).
