# M07 — Агрегация minimap dirty across multi-tick catch-up

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M7  
**Severity:** Medium · **Домен:** SFML presentation correctness · **Roadmap:** P1  
**Related residual:** R33 (partial), R23  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

`FixedStepPacer` может продвинуть несколько simulation ticks за один frame. HUD читает только `GetFogDirtyTiles` **последнего** tick. Если FoW изменился на раннем catch-up tick, а финальный tick пуст, dirty теряется → stale minimap до periodic full rebuild.

---

## Доказательства

- `SfmlPlaySession.cs:444-467` — multi-tick advance loop
- `HudOverlay.cs:99-118` — reads final dirty only
- `FogOfWarSystem` — dirty list cleared/replaced per tick
- `MinimapDirtyTracker` — plans from provided dirty set

---

## Целевой дизайн

**Option A (session accumulate):**

```text
HashSet/List frameFogDirty
foreach tick:
  AdvanceTick()
  frameFogDirty.AddRange(GetFogDirtyTiles(local))
DrawMinimap(frameFogDirty)
clear frameFogDirty
```

**Option B (Core generation):**

Monotonic `FogInvalidationGeneration` + retained dirty union until consumer acks. Heavier API.

**Option C (simple backstop):**

Force full minimap rebuild when `ticksThisFrame > 1` (correct, slightly heavier; OK with R23 cap ≤8).

**Recommendation:** Option A as primary; C acceptable interim. Entity markers partially self-heal via previous/current snapshots — primary gap is **terrain/FoW patches**, not markers. 180-frame full rebuild (`MinimapDirtyTracker` interval) is drift backstop, not the fix.

Dirty overwrite details: idle skip clears (`FogOfWarSystem.cs:55-63`); repaint clears then rebuilds (`PlayerState.cs:78-85`, `:91-93`, `:107-120`).

---

## План фикса

1. In `SfmlPlaySession` tick loop, union dirty tiles into frame-scoped set (pooled).
2. Pass union to `HudOverlay.DrawMinimap` (new param) instead of live last-tick query **or** have overlay accept optional override.
3. Test: simulate 2 ticks where dirty only on first; assert Plan sees those tiles / texture patched.
4. Ensure full rebuild path unchanged.
5. Document interaction with R23 catch-up cap.

---

## Тесты

- `MinimapDirtyTrackerTests` / new `SfmlPlaySession` unit-level helper test with fake dirty sequences
- Multi-tick: dirty on tick N, empty on N+1 → still patched before draw

---

## Definition of Done

- [x] No FoW dirty from intermediate catch-up ticks is lost before draw.
- [x] Test covers multi-tick case.
- [x] R33 residual for catch-up invalidation closed.

## Специалисты

`sfml-platform`, `test-ci`.
