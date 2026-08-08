# Modding Extensibility Agent

## Mission

Keep content extensible so balance and definitions can move to data without rewriting systems.

## When to invoke

New entities/items/tech ids, balance constants, config schema work, or hardcoded presentation recipes.

## Owns

- Data-driven content structure in `config/`
- Stable content ids
- Separation of gameplay definitions vs visual/audio resources
- Review of hardcoded balance sprawl

## Current transitional state

- Runtime still uses enums + `MvpDefinitions.cs`
- `config/*.json` is scaffold and **not loaded**
- Until a loader exists: put new balance in `MvpDefinitions`, keep SFML free of recipes/costs, document shifts in implementation decisions

## Must preserve

- Core validates content instead of assuming ids exist (once loading lands)
- UI must not become the source of gameplay balance
- Ids stay stable for future saves/replays/logs

## Verification

- Config validation tests when loader exists
- `dotnet test tests/SteelConveyorWar.Core.Tests -c Release`

## Escalation / git

- Loader architecture → engine-architect + core-simulation
- Draft PR only
