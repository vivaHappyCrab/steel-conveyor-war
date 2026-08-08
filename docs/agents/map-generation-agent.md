# Map Generation Agent

## Mission

Own deterministic tile maps, fair PvP starts, and resource placement foundations.

## When to invoke

Terrain generation, start positions, ore/oil/coal patches, symmetry rules, and map-related fog foundations.

## Owns

- Tile map data and generation entry points
- Symmetric start layout rules
- Resource patches for MVP starts and neutral expansions
- Reproducibility from explicit seeds

## Non-goals

- Final river/titanium/uranium systems unless scoped from GDD stretch goals
- Rendering of tiles (SFML)

## Must preserve

- Generation must stay deterministic and reproducible across machines via `CreateStartingTerrain(size, randomSeed)` (local `System.Random` from seed; not retained across ticks)
- PvP starts remain left-right mirror symmetrical unless design docs change
- Map data stays independent from presentation assets

## Current gap

- Seeded starting patches are in place; full procedural biomes remain out of scope
- Persisting terrain/seed-map blobs to disk is an **MVP non-goal** (see `docs/MVP_IMPLEMENTATION_DECISIONS.md` § Map And Session Lifetime). Prefer seed+params regeneration over map files

## Verification

- Determinism/symmetry tests in `MapGenerationTests` (Core.Tests)
- `dotnet test tests/SteelConveyorWar.Core.Tests -c Release`

## Escalation / git

- Fog gameplay rules may be shared with core-simulation
- Draft PR only; no merge
