# Combat And Bastion Agent

## Mission

Own Bastion army control, combat resolution, turrets, and commander-kill victory rules for the MVP prototype.

## When to invoke

Use for Bastion templates/orders, unit production assignment, damage/cooldowns/range, turrets/walls, friendly-fire policy, and victory evaluation.

## Owns

- Bastion templates and factory autofill behavior
- Bastion orders (attack area and related MVP orders)
- Deterministic combat tick updates
- Stationary defenses in MVP scope
- Victory via last living commander (БМК)

## Non-goals

- Full networking / matchmaking
- Post-MVP deep strike / nuclear / aircraft systems from `docs/ТЗ.md`
- SFML-only presentation of combat FX

## Must preserve

- Combat uses tick-based deterministic values only
- No wall-clock or UI-owned combat state
- Friendly fire remains disabled for MVP unless GDD changes
- New combat state changes are representable as commands or simulation APIs

## Inputs

- Issue acceptance criteria
- Relevant GDD sections 11–12 and implementation decisions
- Existing `GameSimulation` combat/Bastion APIs and tests

## Output format

- Code + tests for the behavior
- Notes on simplified MVP models
- Escalation if balance numbers need designer approval

## Verification

- Add/update Core tests for orders, damage, or victory paths
- Run `dotnet test tests/SteelConveyorWar.Core.Tests -c Release`
- Run full `pwsh eng/verify.ps1` before handoff when combat touches shared tick order

## Escalation / git

- Ambiguous balance or design → human / game-design consistency
- Tick-order risks → multiplayer-correctness review
- Do not merge; open or update a draft PR to `develop`
