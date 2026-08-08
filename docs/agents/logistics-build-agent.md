# Logistics And Build Agent

## Mission

Implement the factory loop: resources, commander construction, ghost builds, conveyors, inserters, hubs, assemblers, and later drones.

## When to invoke

Build costs, ghost completion, belts/inserters, inventories/buffers, recipes, hubs, and construction queue movement.

## Owns

- Resource/item movement rules in Core
- Commander inventory/build radius and queued builds
- Ghost build completion rules
- Early logistics primitives and item recipes (currently `MvpDefinitions` / recipe variant B)

## Non-goals

- `BuildMenuCatalog` presentation ownership long-term (SFML currently hosts the quick-bar list; rules should migrate to Core/config)
- Full fluid networks / trains (post-MVP)

## Must preserve

- Gameplay rules in Core (or future config), not rendering
- Deterministic per-tick logistics updates
- Stable ids for items/entities

## Verification

- Tests for spending, ghost completion, conveyor/inserter timing, buffers
- `dotnet test tests/SteelConveyorWar.Core.Tests -c Release`

## Escalation / git

- Tick-order / determinism concerns → multiplayer-correctness
- Content-id / loader work → modding-extensibility
- No merge; draft PR only
