# M10 — Atomic immutable bot observation frame

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M10  
**Severity:** Medium · **Домен:** bots / fair observation · **Roadmap:** P1  
**Related residual:** R04 (partial), R19 (partial), H06  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

1. `PlayerView` создаёт mutable `List`/`Dictionary`/`HashSet` и отдаёт как `IReadOnly*` — consumer can downcast and mutate retained snapshot.
2. `AllCommandKinds` returns live array reference.
3. `CaptureSnapshot` omits terrain/visibility — map-aware decisions not atomic vs entity/economy snapshot tick.
4. Combat events may leak hidden endpoints (H06).

---

## Доказательства

- `PlayerView.cs:11-14,76-133,136,161-173` — mutable nested collections as `IReadOnly*`; terrain `null` unless `includeTerrain`
- `IPlayerView` / bot fair default `includeTerrain: false` (R04 ADR deferred terrain for bots — atomic map decisions still open)
- `ObservationSnapshots.cs` — DTO shapes
- `BotObservationContractTests` — tick isolation, not cast-mutation / map atomicity
- Related presentation cost (SFML, not bot DoD): `SessionPresenter` rebuilds full capture each catch-up tick — see also M07

---

## Целевой дизайн

```text
PlayerObservationFrame (tick T):
  entities (ImmutableArray)
  economy/research (immutable maps)
  tech signatures
  events (H06-safe)
  availableCommands (ImmutableArray)
  fog/terrain: either
    (a) dense snapshot of known tiles, or
    (b) versioned FogBoardView frozen at T with copy-on-write
```

Prefer (b) if full terrain copy expensive: store immutable visibility grid + known terrain for Explored/Visible only.

All nested collections: `ImmutableDictionary` / `ImmutableHashSet` / `ImmutableArray`.

---

## План фикса

1. Freeze nested collections in snapshot DTOs (ctors copy to immutable).
2. Return `ImmutableArray` for command kinds from frozen static.
3. Extend `CaptureSnapshot` with fog/terrain board OR document `IPlayerView` requiring single-frame capture API `CaptureFrame()` that samples visibility/terrain under same tick lock.
4. Tests:
   - cast+mutate snapshot collections throws / no effect on subsequent reads;
   - mutating returned command kinds doesn't affect global;
   - frame terrain/visibility stable after AdvanceTick;
   - H06 event coordinate policy.
5. Headless stub uses `CaptureFrame` once per decision.

---

## Definition of Done

- [ ] Snapshots hard-immutable (cast-mutation tests).
- [ ] One API yields atomic map+entities+economy for tick T.
- [ ] R04/R19 residuals closed for immutability/atomicity.
- [ ] Coordinate with H06 for events.

## Специалисты

`core-simulation`, `multiplayer-correctness`, `test-ci`.
