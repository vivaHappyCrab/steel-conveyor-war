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

- Generation uses explicit seeds and is reproducible across machines
- PvP starts remain symmetrical unless design docs change
- Map data stays independent from presentation assets

## Current gap

- `RandomSeed` is stored but the current static `CreateStartingTerrain` layout does not yet vary by seed — fix or document before claiming seed-driven maps

## Verification

- Determinism/symmetry tests in Core.Tests
- `dotnet test tests/SteelConveyorWar.Core.Tests -c Release`

## Escalation / git

- Fog gameplay rules may be shared with core-simulation
- Draft PR only; no merge
