# Verification gaps

Known gaps that CI/agents should not pretend are covered.

| Gap | Impact | Notes |
|-----|--------|-------|
| Map `RandomSeed` does not yet alter terrain layout | Determinism/multiplayer readiness | Static mirrored layout today; seed stored but unused |
| No on-disk map/seed-map persistence | Session resume / opaque map blobs | **Documented MVP non-goal** (GDD §17; regenerate from seed+params later). See `MVP_IMPLEMENTATION_DECISIONS.md` § Map And Session Lifetime |
| `config/*.json` not loaded at runtime | Modding/data-driven claims | Balance in `MvpDefinitions.cs` |
| Some Core collections still expose internal mutable accessors within the assembly | Encapsulation / adapter safety | Public surfaces narrowed (`internal set`, wrapped `IReadOnly*`); remaining gap is assembly-internal `*Mutable` helpers and public `Inventory.Try*` mutators, not castable Sfml/Client writes |
| No golden same-seed/same-commands state hash test | Multiplayer readiness | Add when tick systems stabilize |
| Limited Bastion order / combat loop coverage | Combat regressions | Victory damage path exists; broaden orders/combat |
| SFML smoke depends on native libs + display | CI flakiness risk | Linux job uses Xvfb; document native failures in README |
| Window size differs between `config/game.json` and SFML constants | Config honesty | Track until config load lands |

Update this file when gaps are closed or newly discovered.
