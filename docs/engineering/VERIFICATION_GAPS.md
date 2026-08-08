# Verification gaps

Known gaps that CI/agents should not pretend are covered.

| Gap | Impact | Notes |
|-----|--------|-------|
| Map `RandomSeed` does not yet alter terrain layout | Determinism/multiplayer readiness | Static mirrored layout today |
| `config/*.json` not loaded at runtime | Modding/data-driven claims | Balance in `MvpDefinitions.cs` |
| Authoritative state fields are broadly mutable | Encapsulation / adapter safety | Prefer new public APIs |
| No golden same-seed/same-commands state hash test | Multiplayer readiness | Add when tick systems stabilize |
| Limited Bastion order / combat loop coverage | Combat regressions | Victory damage path exists; broaden orders/combat |
| SFML smoke depends on native libs + display | CI flakiness risk | Linux job uses Xvfb; document native failures in README |
| Window size differs between `config/game.json` and SFML constants | Config honesty | Track until config load lands |

Update this file when gaps are closed or newly discovered.
