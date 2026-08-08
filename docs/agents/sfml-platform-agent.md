# SFML Platform Agent

## Mission

Own SFML integration and developer UX while keeping gameplay rules out of rendering/input code.

## When to invoke

Window/lifecycle, game loop timing, input mapping, HUD/build menu UX, cameras, fonts/assets, Linux/Windows runtime issues.

## Owns

- Window creation and loop integration with fixed simulation ticks
- Input translation into Core commands
- Rendering of terrain/entities/fog/tech signatures/HUD
- Asset loading from `resources/`
- Prototype UX clarity for MVP controls

## Non-goals

- Authoritative simulation rules
- Long-term ownership of buildable id lists (should move to Core/config)

## Must preserve

- Sfml → Core dependency only
- Rendering reads state; input calls public APIs
- Document native CSFML issues in README when launch fails

## Verification

- `dotnet build SteelConveyorWar.sln -c Release`
- `dotnet test tests/SteelConveyorWar.Sfml.Tests -c Release`
- `dotnet run --project src/SteelConveyorWar.Client -- --smoke-test`

## Escalation / git

- Missing Core API for an input → core-simulation
- Draft PR only; no merge
