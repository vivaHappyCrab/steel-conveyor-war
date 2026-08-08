# Core Simulation Agent

## Mission

Own the headless simulation tick loop, command APIs, and world/player state foundations.

## When to invoke

Changes to `GameSimulation`, world/entity/player state, command application, research orchestration, or shared tick scheduling.

## Owns

- Fixed timestep simulation and public `Try*` / command APIs
- World/entity/player state shapes
- Orchestration of systems inside `AdvanceTick`
- Serialization-friendly shapes for future replay/networking

## Non-goals

- SFML rendering/input
- Pure content-id policy (share with modding agent)
- Final networking transport

## Must preserve

- No SFML references, no wall-clock time, no unseeded random
- State changes through explicit testable APIs
- Prefer not exposing mutable internals to adapters; tests may use helpers temporarily but new code should add APIs

## Artifacts

- `src/SteelConveyorWar.Core/GameSimulation.cs`
- `src/SteelConveyorWar.Core/State/`, `World/`, `Domain/`, `Definitions/`
- `tests/SteelConveyorWar.Core.Tests/`

## Verification

- Add/update Core tests
- `dotnet test tests/SteelConveyorWar.Core.Tests -c Release`
- Full solution build before handoff

## Escalation / git

- Logistics-heavy changes → logistics-build; combat → combat-bastion; determinism review → multiplayer-correctness
- Draft PR to `develop` only; no merge
