# Verification gaps

Known gaps that CI/agents should not pretend are covered.

| Gap | Impact | Notes |
|-----|--------|-------|
| Map `RandomSeed` does not yet alter terrain layout | Determinism/multiplayer readiness | Static mirrored layout today; seed stored but unused |
| No on-disk map/seed-map persistence | Session resume / opaque map blobs | **Documented MVP non-goal** (GDD §17; regenerate from seed+params later). See `MVP_IMPLEMENTATION_DECISIONS.md` § Map And Session Lifetime |
| Remaining balance still in `MvpDefinitions.cs` | Modding/data-driven claims | Research + game/tiles/entities JSON load at runtime; recipes/combat/build costs still code |
| Authoritative state fields are broadly mutable | Encapsulation / adapter safety | Prefer new public APIs |
| No golden same-seed/same-commands state hash test | Multiplayer readiness | **Closed for dual-run gate:** `SimulationStateHasher` + `DeterminismHashTests`. Optional checked-in golden still deferred while systems churn; refresh policy in `MVP_IMPLEMENTATION_DECISIONS.md` |
| Limited Bastion order / combat loop coverage | Combat regressions | Victory damage path exists; broaden orders/combat |
| Some Core collections still expose internal mutable accessors within the assembly | Encapsulation / adapter safety | Public surfaces narrowed (`internal set`, wrapped `IReadOnly*`); remaining gap is assembly-internal `*Mutable` helpers and public `Inventory.Try*` mutators, not castable Sfml/Client writes |
| Limited Bastion order / combat loop coverage | Combat regressions | Orders AttackArea/Defend, in-range damage/cooldown, no friendly fire, and combat-path victory covered in `CombatBastionTests`; Patrol/Support/turrets still thin |
| SFML smoke depends on native libs + display | CI flakiness risk | Linux job uses Xvfb; document native failures in README |

Update this file when gaps are closed or newly discovered.
