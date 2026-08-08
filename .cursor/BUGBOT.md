# Bugbot project policy — Steel Conveyor War

Focus on regressions that break MVP invariants:

1. **Core/SFML boundary** — SFML must not become authoritative gameplay; Core must not reference SFML.
2. **Determinism** — wall-clock time, unseeded random, unstable iteration order, or hidden UI state in simulation.
3. **Command API** — adapters mutating core internals instead of `Try*` / public simulation APIs.
4. **Tests** — Core tests pulling SFML; missing regression tests for logistics, construction, combat, fog, or pathfinding changes.
5. **Config honesty** — claiming `config/` is loaded when balance still lives in `MvpDefinitions.cs`.
6. **Git safety** — workflow changes that weaken required checks or allow unprotected pushes to `develop`/`main`.

Prefer concrete, actionable comments with file references. Skip style nits covered by `dotnet format` / analyzers.
