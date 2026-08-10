# Verification gaps

Known gaps that CI/agents should not pretend are covered.

| Gap | Impact | Notes |
|-----|--------|-------|
| Map `RandomSeed` drives starting terrain only (no biomes) | Procedural depth | Seeded mirrored patches covered by `MapGenerationTests`; full biomes are non-goals |
| No on-disk map/seed-map persistence | Session resume / opaque map blobs | **Documented MVP non-goal** (GDD §17; regenerate from seed+params later). See `MVP_IMPLEMENTATION_DECISIONS.md` § Map And Session Lifetime |
| Remaining recipes/combat/footprints/stacks/power still in `MvpDefinitions.cs` | Modding/data-driven claims | Research + game/tiles/entities/build-costs JSON load at runtime; recipes/combat/stats still code — not fully data-driven |
| Authoritative state fields are broadly mutable | Encapsulation / adapter safety | Prefer new public APIs |
| No checked-in golden same-seed/same-commands state hash | Multiplayer readiness | **Decision (#80): keep deferred.** Dual-run gate remains primary (`SimulationStateHasher` + `DeterminismHashTests`). Golden hex deferred while Core authoritative surface still churns (combat/energy/recipes/research/spawn/balance; `AlgorithmVersion` already at `5`). A fixed golden would mostly fail on intentional content/surface edits, forcing frequent refreshes without improving cross-run lockstep detection. Revisit when Core tick/command/hash surface stabilizes or lockstep/replay needs a fixed oracle. Refresh rules live in `MVP_IMPLEMENTATION_DECISIONS.md` § Simulation State Hash. |
| Remaining movement-step `Sqrt` / `WorldPosition` doubles | Cross-runtime lockstep | **MVP policy documented** (ADR 0001 + decisions § Authoritative Numeric Policy): single-runtime guarantee; energy sort / A* costs / distance-squared compares mitigated. Fixed-point migration still open |
| Limited Bastion order / combat loop coverage | Combat regressions | Victory damage path exists; broaden orders/combat |
| Some Core collections still expose internal mutable accessors within the assembly | Encapsulation / adapter safety | Public surfaces narrowed (`internal set`, wrapped `IReadOnly*`); remaining gap is assembly-internal `*Mutable` helpers, not castable Sfml/Client writes. Inventory mutators (`TryRemove`/`TryAdd*`) are assembly-internal; external code must use `GameSimulation` commands/queries |
| Limited Bastion order / combat loop coverage | Combat regressions | Orders AttackArea/Defend, in-range damage/cooldown, no friendly fire, and combat-path victory covered in `CombatBastionTests`; Patrol/Support/turrets still thin |
| SFML smoke depends on native libs + display | CI flakiness risk | Linux job uses Xvfb; document native failures in README. Display-free path covered by `SteelConveyorWar.Headless` smoke in `verify.ps1` / CI `build-test` |

Update this file when gaps are closed or newly discovered.
