# ADR 0001: Authoritative numeric policy (doubles / Sqrt)

## Status

Accepted

## Context

Authoritative Core uses `WorldPosition` (`double`) and `Math.Sqrt` in movement, pathfinding, collision, and historically energy fill-ratio sorting. Cross-platform / cross-CPU floating-point divergence is a lockstep desync risk even when `SimulationStateHasher` records IEEE bit patterns — matching hashes only prove agreement **after** divergent math has already produced the same bits on one machine.

Issue #68 (review H5 / N4) requires a documented policy and a concrete mitigation path before multiplayer lockstep work.

## Decision

### MVP policy (current)

1. **Single-runtime guarantee for remaining authoritative FP.** Until fixed-point (or equivalent) migration completes, lockstep / dual-client claims are valid only when peers share the **same .NET runtime family and OS/CPU ABI** (same published build targeting the same RID class). Cross-OS or mixed-JIT lockstep is **out of MVP**.
2. **Gameplay decisions prefer integer / squared comparisons** wherever order or threshold checks do not require a true Euclidean length:
   - Energy emptiest-first sort uses integer cross-multiply ratios (no `double` division).
   - Ground A* step / heuristic costs use integer octile weights (`10` / `14`), not `Math.Sqrt(2)`.
   - Collision and range threshold checks use **distance-squared** against `radius²` (no `Sqrt` on the compare path).
3. **Deferred migration target:** fixed-point or integer world coordinates for authoritative movement / collision / continuous range. Tile-space gameplay that already uses integer Euclidean (`dx*dx+dy*dy`) stays integer.
4. **Presentation-only floats** may remain unconstrained (SFML draw, camera, belt item lerp, HUD bars, combat tracer endpoints, energy history graphs). They must not feed back into Core decisions.

### Remaining authoritative FP (explicit)

- `WorldPosition` storage and **movement step normalization** still use `double` + `Math.Sqrt` (`DistanceTo` / step along the unit vector). Covered by the single-runtime guarantee until migration.
- `CollisionSize.Radius` and related constant multiplies remain `double` under the same guarantee.
- Hasher continues to fingerprint world doubles via `DoubleToInt64Bits`.

## Consequences

Positive:

- Clear lockstep boundary for MVP hosts and bots.
- Removes unchecked FP from energy ordering and A* cost/heuristic ranking.
- Collision / interact / build radius gates no longer call `Sqrt` for decisions.

Negative / follow-up:

- Full fixed-point `WorldPosition` migration is still required before heterogeneous-runtime lockstep.
- Integer A* (`10`/`14`) can change path tie-breaks vs prior `Sqrt(2)` costs (same-seed dual-run still matches).
- Movement step `Sqrt` remains a desync hazard across divergent FP environments.

## Alternatives considered

| Option | Why not (for this ADR) |
|--------|-------------------------|
| Immediate full fixed-point world coords | High blast radius across movement/collision/SFML adapters; deferred as incremental follow-up |
| Soft-float / software IEEE everywhere | Heavy; unnecessary if single-runtime is accepted for MVP |
| Leave energy/path on doubles with docs only | Fails AC mitigation expectation; cheap integer fixes available now |
