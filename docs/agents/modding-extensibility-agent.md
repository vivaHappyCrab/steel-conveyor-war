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

- Runtime loads `config/game.json`, `research.json`, `tiles.json`, and `entities.json` via Core parsers + Client I/O (fail-fast on missing/invalid)
- Recipes, combat stats, build costs, and most timings still live in `MvpDefinitions.cs`
- **Behavior slice (#82):** `entities.json` `lossCondition` drives defeat/victory; tile `walkable`/`resource` and entity `buildsStructures` are still metadata/registry fields. Not fully data-driven entities.
- Until remaining balance migrates: put new recipes/costs/stats in `MvpDefinitions`, keep SFML free of balance, document shifts in implementation decisions

## Must preserve

- Core validates content instead of assuming ids exist
- UI must not become the source of gameplay balance
- Ids stay stable for future saves/replays/logs

## Verification

- Config validation tests when loader exists
- `dotnet test tests/SteelConveyorWar.Core.Tests -c Release`

## Escalation / git

- Loader architecture → engine-architect + core-simulation
- Draft PR only
