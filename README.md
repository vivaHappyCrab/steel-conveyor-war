# Steel Conveyor War

2D PvP macro-RTS/factory game prototype inspired by Factorio-like logistics.

The current milestone provides the engineering foundation: .NET 10 solution, headless simulation core, SFML rendering adapter, client startup, tests, configs, resources, Cursor/GitHub AI workflow, and agent role documents.

## Requirements

- .NET SDK `10.0.x` (see `global.json`, `rollForward: latestFeature`)
- Windows or Linux
- SFML native libraries come from the `SFML.Net`/`CSFML` NuGet packages. If runtime launch fails, check native library loading first.

## License

- Code: [MIT](LICENSE)
- Assets under `resources/`: [All Rights Reserved](resources/LICENSE.md)

## Project structure

- `src/SteelConveyorWar.Core` — headless deterministic simulation
- `src/SteelConveyorWar.Sfml` — SFML window, rendering, input, and asset adapter
- `src/SteelConveyorWar.Client` — executable game client composition
- `tests/SteelConveyorWar.Core.Tests` — core unit tests without graphics
- `tests/SteelConveyorWar.Sfml.Tests` — SFML adapter tests
- `config/` — data-driven content scaffold (not loaded by runtime yet)
- `resources/` — textures, fonts, audio, and other presentation assets
- `docs/` — GDD, decisions, engineering workflow, agent roles
- `AGENTS.md` + `.cursor/` — Cursor project instructions, rules, agents, and skills
- `eng/verify.ps1` — local verification entrypoint

## Commands

```powershell
dotnet restore SteelConveyorWar.sln --locked-mode
dotnet build SteelConveyorWar.sln -c Release
dotnet test SteelConveyorWar.sln -c Release
pwsh eng/verify.ps1
dotnet run --project src/SteelConveyorWar.Client
dotnet run --project src/SteelConveyorWar.Client -- --smoke-test
```

## AI workflow

See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/engineering/AI_WORKFLOW.md](docs/engineering/AI_WORKFLOW.md).

Short version: issues with Definition of Ready get `ai-ready` → Cloud Agent opens a draft PR to `develop` → CI + reviews → human merges.

## Current MVP prototype

- Fixed `30` ticks-per-second deterministic core simulation
- Local symmetric 1v1 map with Fe/Cu starts and neutral coal/oil expansion resources
- Players, БМК commanders, Bastions, hubs, buildings, units, inventories, power, fog of war, and tech signatures
- MVP foundations for ghost build construction, T1-T2 resources, laboratories/research, logistics, production, Bastion templates/orders, combat, and commander-kill victory
- SFML rendering of terrain, entities, fog, and tech signature zones
- Gameplay logic independent from SFML for testing, replay, and future multiplayer sync

## Prototype controls

Developer prototype for player 1:

- Left click selects a visible object
- Select your БМК and press `B` to open/close the build menu
- Number keys choose buildings while the build menu is open
- Left click with a pending building places or queues a ghost build
- `R` / `Shift+R` rotates directed logistics entities
- Right click issues Bastion attack orders (or БМК move when selected)
- `Ctrl+Left click` collects output buffer items into the БМК when in range

## Verification status

Baseline after workflow bootstrap:

```powershell
dotnet build SteelConveyorWar.sln -c Release
dotnet test SteelConveyorWar.sln -c Release
dotnet run --project src/SteelConveyorWar.Client -c Release -- --smoke-test
```

Expected local baseline: build 0 warnings / 0 errors; **53** tests (49 Core + 4 Sfml); client smoke exits successfully.

See `docs/MVP_IMPLEMENTATION_DECISIONS.md` and `docs/engineering/VERIFICATION_GAPS.md`.
