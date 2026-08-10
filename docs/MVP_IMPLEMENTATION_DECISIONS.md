# MVP Implementation Decisions

This document records architecture and game-design decisions made while implementing `docs/MVP_GDD.md`.

## Architecture

- Gameplay rules live in `SteelConveyorWar.Core`; SFML remains a rendering and input adapter.
- The core simulation advances only through fixed ticks and explicit public APIs.
- Authoritative Core state is encapsulated for adapters/tests: mutable entity/player fields use `{ get; internal set; }`, inventories expose public `Try*` with `internal` `Add`/`Clear`, and research collections are public `IReadOnly*` with assembly-internal mutable storage. External code mutates via `GameSimulation` APIs (including test helpers such as `TryForceCompleteResearch`).
- MVP recipes/combat/build costs remain compact enums + `MvpDefinitions.cs` in Core. Research technologies and match bootstrap settings load from `config/*.json`. Broader migration of remaining balance to `config/` remains a follow-up.
- `GameSimulation.CreateNewGame(seed)` creates a deterministic local 1v1 match with mirrored starts and the default research profile (`mvp-b`).
- `GameSimulation.CreateNewGame(GameCreationOptions)` accepts an explicit research catalog/profile plus optional tile/entity catalogs for tests and composition roots.

## Config Loading

- **Ownership:** Core owns parse/validate of JSON content (`ResearchContentLoader`, `GameSettingsLoader`, `TileContentLoader`, `EntityContentLoader`). Client owns path resolution and file I/O, then passes parsed catalogs into `GameCreationOptions`. SFML never parses gameplay JSON; it only receives display options from Client.
- **Authoritative at runtime (Client fail-fast):** `config/game.json`, `config/research.json`, `config/tiles.json`, `config/entities.json`. Missing or invalid files abort startup.
- **Still code-owned:** build costs, recipes, combat stats, footprints, stack sizes, and most timing constants in `MvpDefinitions.cs`. Tile/entity JSON catalogs are ID registries for content ids — they do not yet replace enum-driven simulation behavior.
- Embedded `MvpResearchCatalog` remains the parity fallback for unit tests and `GameCreationOptions.Default` only (not for Client disk startup).
- Window width/height/title come from `game.json` into `SfmlDisplayOptions`; side-panel layout scales from window width.
- `simulation.ticksPerSecond` is parsed for future hosts but the Client/SFML loop still uses `GameSimulation.TicksPerSecond` (const 30) until wired.

## Scope Strategy

- The first code pass implements the full MVP surface as a deterministic simulation model, not final production-quality UI.
- Systems that are expensive to simulate in detail are represented by simplified deterministic rules first: oil, construction drones, ammo chains and tech signatures.
- T3-T4, nuclear weapons, aircraft, trains, full pipes, transport drones, matchmaker, replays and analytics remain outside the implemented MVP.

## Construction

- БМК is the only builder-facing entity. Commander build APIs create or queue ghost builds.
- Ghost-build costs are paid by `TryPlaceGhostBuildFromCommander`: commander inventory first, then any **remaining** items from owned hubs within `CommanderInteractRadius` (Euclidean distance from commander world position to the hub footprint — same radius/check as Ctrl withdraw/deposit). Eligible hubs are spent in ascending entity-id order. Payment is all-or-nothing: if commander + in-radius hubs cannot cover the full cost, nothing is removed.
- Construction-drone / construction-ticks research only shortens ghost build duration; drones do **not** pull materials from hubs.
- T1 construction completes after a fixed number of ticks. T2 construction drones are represented by the same ghost-build model and can be expanded later without changing placement commands.
- Player inventory remains broader faction state for later logistics/network rules; hubs are world logistics buffers and can fund BMK placement when in interact range.
- If a build target is outside the БМК build radius, the core stores a queued build order and moves the БМК toward the target until placement becomes legal (payment still runs at placement time via the same commander/hub rule).
- Building footprint is part of core placement rules: mines, laboratories, hubs and resource extractors are `2x2`; Bastions and military factories are `3x3`; other entities default to `1x1`.

## Economy And Logistics

- Mines and wells produce items into local building inventories on a work cycle driven via `WorkTicksRemaining`/`WorkTicksTotal` each tick (progress bars): iron/copper mines use `OreMineWorkTicks` (=30), coal mines `CoalMineWorkTicks` (=45), oil wells `MineWorkTicks` (=15). On cycle complete the building drains energy and adds 1 ore; full output leaves the building idle (ticks cleared).
- Buildings now expose separate input and output buffers. Recipes consume from input buffers and put completed products into output buffers.
- Input and output buffers are limited by per-item stack size definitions in `MvpDefinitions.ItemStackSizes`.
- Inserters move items between adjacent output/input buffers and conveyor slots. An inserter hand can hold only one item with amount `1`.
- When multiple empty-handed inserters pull from the same source entity in one tick, only one may extract (deterministic fair share by tick + source id among candidates ordered by inserter id).
- Conveyor tiles hold at most two item slots and move items in their direction only after `MvpDefinitions.ConveyorMoveTicks`.
- Inserters transfer held items only after `MvpDefinitions.InserterTransferTicks`.
- Conveyor and inserter direction is core state and can be rotated through a simulation API. SFML only renders the arrows and translates hotkeys.
- Conveyor item rendering interpolates draw position from `ProgressTicks / moveTicks` along belt `Direction`; two slots are placed along the belt axis (~25%/75%). Core movement remains discrete hops.
- Oil is represented as `CrudeOil` items refined into `Fuel`. Full fluid pressure, pipe networks and reservoirs are deliberately deferred.
- Energy is tracked as produced versus demanded per player. Each powered consumer has an `EnergyBuffer` with capacity `PowerDemand × 100`. The grid fills buffers emptiest-first each tick: sort by `EnergyBuffer/Capacity` ascending then entity id, and distribute `PowerProduced` one energy unit at a time. Buildings drain `PowerDemand` from their buffer only while actively producing; empty buffer pauses work progress (soft craft-time inflate removed). Per-player presentation-only `EnergyStatsHistory` ring (10 min @ 30 TPS tick storage keyed by absolute `Tick`, not hashed) feeds the SFML energy overlay (**P**): windows 10s/30s/1m/5m/10m; consumption series are **actual** buffer drains; `Query` emits averages over **fixed absolute** buckets (1s / 5s / 10s) so completed graph points never rewrite as the live window slides. Both graphs share one Y max and draw axis labels (Y energy/tick, X window time) with equal plot height.
- Assemblers and factories default to no recipe until selected (or bastion autofill). Smelters use a sticky auto-recipe from input; unused empty smelters refuse Ctrl+deposit. Base smelt ticks: iron/copper plate 40, steel 60. Iron gear / composite craft ticks: 40 / 60.
- Player-facing inventory UI applies only to Commander and Hub (`MvpDefinitions.HasPlayerInventory`).

## Commander Interaction

- Right click with the БМК selected issues a deterministic move command (outside build mode).
- In build mode (`B`), RMB hold ≥ 1s on an owned demolishable building calls `TryQueueCommanderDemolish` (instant in build radius, else queued move+demolish). Release early or retarget cancels. RMB on empty/invalid tiles is a no-op (does not move). Refund is `floor(BuildCosts/2)` plus full buffers/inventory/conveyor/held/pending output; deposit uses commander per-item stack cap, then hubs in interact radius, discard remainder. Bastion/Commander/units cannot be demolished.
- `S` with the БМК selected clears move / queued build / queued demolish (`TryStopCommander`); paid ghosts keep constructing.
- `Ctrl+Left click` with the БМК selected withdraws from hub inventory or collects the clicked entity output buffer when the target is within `CommanderInteractRadius` and owned by the same player.
- `Ctrl+Right click` with the БМК selected deposits commander inventory into hub storage or a building input buffer within `CommanderInteractRadius` and same ownership (instead of issuing a move). Production buildings accept only current-recipe inputs; no recipe → deposit fails and falls through to move. Hub remains unfiltered.
- Sidebar: RMB on an Input storage line (or hub inventory) deposits all of that item type from the БМК; LMB on an Output line (or hub inventory) withdraws all of that type into the БМК (same interact radius). Energy and craft progress bars are drawn on the selected building panel.
- Hover/`R` rotate and selected rotate only apply to directed buildings owned by the local player.
- Long-range queued collection is intentionally not implemented yet; only queued construction and queued demolish use automatic movement.

## Research

- Laboratories consume science packs from their own input buffers on a per-lab `LabCycleTicks` (=30) work cycle (`WorkTicks*` progress). On cycle complete the lab drains energy and consumes packs for active projects.
- Research is data-driven through `ResearchCatalog` / `config/research.json` with composable profiles (`mvp-a`, `mvp-b`, `mvp-c`, `hybrid-a-c`).
- Default match profile is `mvp-b` from `config/game.json`; `GameSimulation.CreateNewGame(GameCreationOptions)` can override the profile for tests and future hosts.
- Profiles combine orthogonal rules: tier gates (fixed set or 1-of-N qualifications), tracks (serial or weighted-parallel), exclusive doctrine groups, optional expansion pools, and typed effects.
- There is no runtime `switch(variant)` — gameplay systems query capabilities and modifiers, not profile ids.
- Progress is stored per technology and is not reset when switching projects.
- Completing a gate automatically unlocks the next tier (or records the T3 milestone without opening T3 content).
- Effects use integer basis-point modifiers (`AddModifier`) plus `UnlockContent` / `GrantCapability` / `CompleteMilestone`.
- Embedded `MvpResearchCatalog` remains the parity fallback; `eng/ExportResearchCatalog` regenerates `config/research.json` from it. Technologies carry player-facing `DisplayName` / `Description` (Russian via `ResearchDisplayNames`); SFML research overlay lays out gate candidates as a bus graph (mandatory row → shared bus → next tier; optionals hang off the bus).

## Bastions And Combat

- Bastions own desired unit templates (sum capped; research can raise capacity). Factories do **not** store bastion assignment. Autofill picks a missing unit kind across **all** owned bastion templates; spawned units assign to the lowest bastion Id that still needs that kind (no overshoot fallback). Live supply (assigned living units + attributed in-flight factory production) is exposed via `GetBastionUnitSupply`. SFML shows a center composition overlay for tier-unlocked unit kinds with live/max and +/- wired to `TrySetBastionTemplate`.
- Produced units inherit the Bastion's current order (Scout filter applies). Manual factory recipes stay selected after spawn but **do not start** a new craft while player-wide kind supply ≥ summed templates for that kind or total army supply ≥ template capacity (idle wait); autofill clears and re-picks deficits. `TrySetBastionTemplate` enforces the capacity against the **player-wide** sum of all owned bastion templates. Unit spawn searches rings 1–6 for a passable, non-overlapping tile; if none is free the craft stays in-flight (`WorkTicksRemaining = 1`) until a tile opens (no stack on the factory tile).
- Active defense garrisons units inside the Bastion; threats in Bastion vision trigger a sortie. Bastion death kills assigned units.
- Combat uses deterministic Euclidean range, cooldown, and **formula C** damage: `max(1, AttackDamage - Armor) * Resistance(projectile, targetCategory)` with basis-point integer math (`CombatDamage` / `MvpDefinitions.GetResistanceBasisPoints`). HP is clamped to ≥ 0. Each landed shot appends a presentation-only `CombatShotEvent` (not hashed) for SFML tracers.
- Mobile units collide with buildings (circle vs footprint) always. Unit↔unit circle–circle applies only while **stopped**; moving units ignore other units' collision radii (buildings unchanged). Draw silhouettes scale from `GetCollisionSize`.
- Intermediate craft uses `IronGear` and `Composite` (1 iron + 1 copper plate → 1; work ticks 40 / 60). `CopperWire` / circuit-via-wire are removed. Scout production costs 4×Composite; SciencePackT2 consumes Composite.
- `EntityStats` includes Armor, `ProjectileKind` (`GroundToGround` | `Ballistic` | `AirToGround`), and `SplashRadius` (0 = single target). MG turrets/bots/БМК are G2G; cannon/rocket/medium tank are Ballistic; AA turret/bot are AirToGround. Splash applies in the same `ProcessCombat` pass to enemies near the primary target (ordered by entity id).
- Walls/SteelWalls block **GroundToGround** damage to allied **ground** units (БМК + `UnitKinds` except Scout) when a Bresenham LoS tile between attacker and target holds a Wall/SteelWall owned by a player with the same `TeamId` as the target. Ballistic and AirToGround ignore walls. Buildings and walls as targets still take full formula-C damage.
- Research modifiers (`ResearchStatIds.AttackDamage` / `Armor` / `AttackCooldownTicks` / `MaxHealth`, plus existing `VisionRadius`) flow through `ResolveStat` / `AddModifierEffect`. `ProcessCombat` and FoW/HP sync use resolved values (MaxHealth delta adjusts current HP when the cap changes).
- Friendly fire is disabled for MVP (same `TeamId`). Victory is evaluated by commander survival: the last player with a living БМК wins.
- Baseline combat tables live in `docs/MVP_GDD.md` §12 and `MvpDefinitions.GetStats`.

## Fog Of War And Tech Signatures

- Each player has a tile visibility mask: unknown, explored, visible.
- Owned entities reveal circular Euclidean-radius vision (integer `dx*dx+dy*dy <= r*r`).
- **Shared vision (MVP):** entities owned by any player with the same `TeamId` contribute to every ally's fog mask each tick.
- Bastion vision radius is elevated relative to other buildings.
- Combat attack range uses the same Euclidean tile check for determinism.
- Tech signatures are aggregated into map zones from **non-allied** entities and expose intensity without exact building identity.
- SFML draws a FoW minimap flush top-right of the window: explored/visible terrain + resource patches; live entity markers (blue=own / red=enemy / magenta(255,0,255)=ally) use the same Visible gate as the main playfield so Explored does not leak current enemy/ally positions; unknown tiles stay hidden. A viewport rectangle shows the current camera; LMB recenters the camera; RMB issues the same world orders as playfield RMB on the mapped tile.

## Map And Session Lifetime

- MVP match map size is **192×112** tiles (4× the original 48×28 prototype footprint).
- **Static alliances:** `config/maps/default.json` (path via `game.json` → `map.content`) defines the match roster (`id` / `name` / `teamId`). Same `teamId` = allied for the whole match. Default 1v1 is Blue `teamId` 1 vs Red `teamId` 2. Dynamic mid-match diplomacy is out of MVP.
- `GameSimulation.CreateNewGame` builds mirrored starting terrain in memory via `CreateStartingTerrain(size, options.RandomSeed)` and stores `RandomSeed` on the simulation. Player `TeamId` comes from `MapSettings` / `GameCreationOptions.Map`.
- Terrain generation uses a local `System.Random` seeded with `RandomSeed` to jitter patch centers/radii on the left half, then mirrors resource tiles to the right for PvP fairness. Grass remains the default fill. The RNG is not kept for later ticks.
- Starting Fe/Cu patches sit near each base; coal/oil patches sit farther toward the half-map center. Ore fill uses **Chebyshev** distance so each patch AABB is at least **4×4** (radius ≥ 2 → 5×5).
- Each side starts with БМК, Bastion, Hub, and **one SolarPanel** on grass at Chebyshev distance 1 from the bastion footprint (no resource overlap).
- SFML camera pans with **arrow keys**, **MMB drag**, and **edge-scroll** on the playfield (not WASD). **F1** selects the local БМК and centers the camera. **T** toggles the research overlay; **P** toggles the energy statistics overlay. With a bastion selected, **A/S/D/F** issue Attack/Scout/Defend/Patrol, **E** toggles the composition template overlay, and **1–0** switch among owned bastions. World draw is clipped to the playfield; the top bar shows energy + BMK inventory (energy text turns red when `PowerDemand > PowerProduced`); the side panel wraps HUD text including combat stubs (projectile/vision/damage/fire rate/splash/armor). Buildings (except belts/inserters) and units use geometric pictograms; unit silhouettes are circle (БМК) / square (ground) / triangle (Scout).
- Terrain and entity layout currently live only for the process lifetime of one match. Exiting the client discards the in-memory grid; the next session regenerates from the same seed + generation parameters.
- `RandomSeed` is accepted from `GameCreationOptions` / `config/game.json` (`simulation.defaultRandomSeed`) and consumed by starting terrain generation (reproducibility covered by `MapGenerationTests`).
- Desired post-MVP reproducibility prefers **seed + generation parameters → regenerate** over opaque map blobs (better for lockstep / fairness than shipping terrain files).
- **MVP non-goal:** no on-disk terrain/seed-map blob format, and no save/load path for world terrain. Map JSON is **roster/alliance metadata only**, not a terrain dump. Aligns with GDD §17 (saves are out of MVP). Full procedural biomes remain out of scope.

## Open Follow-Ups

- Expand remaining non-research balance definitions from code to `config/` once the shape stabilizes.
- Tune energy demand/production balance and optional consumer priority tiers beyond emptiest-first once production loops are playtested.
- Expand SFML research controls from prototype paging/hotkeys to a dedicated full tree panel.
- Replace simplified oil item movement with a dedicated fluid network if T2 playtests show it is needed.
- Add tick-stamped command queue / state hash for multiplayer research lockstep.

## Simulation State Hash

- `SimulationStateHasher.AlgorithmVersion` (currently `5`) fingerprints authoritative Core state: seed, tick, status, research catalog hash/profile, next entity id, terrain, ordered players (teamId/inventory/visibility/research/power), ordered entities (buffers, energy buffer, sticky smelt recipe, work totals, paths, combat/build fields, bastion order waypoints).
- Doubles use IEEE bit patterns (`DoubleToInt64Bits`). Unordered collections are sorted before hashing.
- Primary quality gate: dual independent runs with the same seed/commands must match (`DeterminismHashTests`). A checked-in golden hex is optional; when adding/updating one, bump `AlgorithmVersion` if the surface changed, re-run the fixture, and commit the new constant intentionally.
- **Issue #80 decision:** do **not** check in a golden hex yet. MVP Core still churns fields the hasher fingerprints (combat, energy buffers, recipes, research profile/catalog, factory spawn caps, balance timing). Dual-run already covers accidental non-determinism; a golden would mostly regress on intentional edits and inflate noise. Add a CI-asserted golden later once the hash surface stabilizes or multiplayer lockstep needs a fixed oracle.
- Out of surface: SFML/UI, wall-clock, tick-stamped command logs.
- Teach map generation to consume `RandomSeed` before any claim of seed-driven layouts (no MVP map-disk format).
