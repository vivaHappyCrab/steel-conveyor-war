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

- Generation must stay deterministic and reproducible across machines (today via static mirrored layout; later via explicit seeds once generation consumes them)
- PvP starts remain symmetrical unless design docs change
- Map data stays independent from presentation assets

## Current gap

- `RandomSeed` is stored but the current static `CreateStartingTerrain` layout does not yet vary by seed — fix or document before claiming seed-driven maps
- Persisting terrain/seed-map blobs to disk is an **MVP non-goal** (see `docs/MVP_IMPLEMENTATION_DECISIONS.md` § Map And Session Lifetime). Prefer future seed+params regeneration over map files

## Verification

- Determinism/symmetry tests in Core.Tests
- `dotnet test tests/SteelConveyorWar.Core.Tests -c Release`

## Escalation / git

- Fog gameplay rules may be shared with core-simulation
- Draft PR only; no merge
