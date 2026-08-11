# ADR 0001: Authoritative numeric policy (millitiles)

## Status

Accepted (supersedes double/`Sqrt` MVP caveat for world positions)

## Context

Authoritative Core previously used `WorldPosition` (`double`) and `Math.Sqrt` in movement step normalization. Cross-platform floating-point divergence is a lockstep desync risk. Issue #68 / review R20 requires fixed-point (or equivalent) migration before heterogeneous lockstep.

## Decision

### Current policy

1. **Authoritative continuous positions use integer millitiles.** `WorldPosition` stores `long` millitiles where **1 tile = 1000 millitiles**. Tile centers are `tile*1000+500`. Mobile step is **125** millitiles/tick (was 0.125 tile).
2. **Collision radii** are millitiles (`CollisionSize.RadiusMilli`).
3. **Distances** prefer squared compares; movement normalization uses deterministic integer sqrt (`WorldUnits.IntegerSqrt`), not IEEE `Math.Sqrt`.
4. **Hasher** fingerprints raw `int64` millitile coordinates (`AlgorithmVersion` 8+).
5. **Presentation-only floats** remain unconstrained at the SFML boundary (`ToTileSpaceX/Y`, camera, draw). They must not feed back into Core decisions.
6. Tile-space gameplay that already uses integer Euclidean (`dx*dx+dy*dy` on tiles) stays integer.

### Heterogeneous lockstep

Peers may share lockstep across OS/CPU ABIs for position/collision math under this millitile policy, provided they share the same `AlgorithmVersion`, content manifest, and command protocol.

## Consequences

Positive:

- Removes authoritative FP from world positions and movement step length.
- Enables cross-runtime dual-run hash equality for movement scenarios.

Negative / follow-up:

- Integer division in step normalization can quantize paths slightly vs prior double steps (dual-run still matches).
- Content collision radii in `gameplay-tables.json` are authored in millitiles.

## Alternatives considered

| Option | Why not |
|--------|---------|
| Q32.32 fixed-point | Heavier; millitiles match authored 0.125 step and radii cleanly |
| Soft-float everywhere | Unnecessary once millitiles cover authoritative continuous space |
| Keep doubles with docs only | Fails R20 DoD for heterogeneous lockstep |
