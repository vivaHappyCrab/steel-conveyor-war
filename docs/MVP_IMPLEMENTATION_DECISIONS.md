# MVP Implementation Decisions

This document records architecture and game-design decisions made while implementing `docs/MVP_GDD.md`.

## Architecture

- Gameplay rules live in `SteelConveyorWar.Core`; SFML remains a rendering and input adapter.
- Composition roots: `SteelConveyorWar.Client` opens an SFML window; `SteelConveyorWar.Headless` is a Core-only host that loads `config/`, calls `CreateNewGame`, applies a stub command/AI step, then `AdvanceTick` (usable from CI without a display). Full bot AI and network transport remain follow-ups.
- The core simulation advances only through fixed ticks and explicit public APIs.
- Authoritative Core state is encapsulated for adapters/tests: mutable entity/player fields use `{ get; internal set; }`, inventory mutators (`TryRemove` / `TryAdd*` / `Add` / `Clear`) are `internal`, and research collections are public `IReadOnly*` with assembly-internal mutable storage. External production code mutates via public `GameSimulation` command APIs (`Try*` convenience) or the tick-stamped queue (`EnqueueCommand` / `DeferredCommandSink`).
- Tick systems are extracted from the former god-object into focused collaborators (`PowerSystem`, `CombatSystem`, `MovementSystem`, `FogOfWarSystem`, `FactoryBastionSystem`, `VictorySystem`, …) that talk through `ISimulationSystemContext` / partial `GameSimulation`.
- Hot-path queries share `SpatialQueryIndex` (combat target/splash, movement collision, defend threat) and a pooled `PathfindingWorkspace` for A*.
- **Trust boundary — test/debug helpers:** god-mode and fixture helpers on `GameSimulation` are `internal` (`AddPlayerItems`, `DamageEntity`, `AddItemToEntity`, `TryForceCompleteResearch`, `TrySpawnEntityForTests`, `TryTeleportEntityForTests`, `TrySetEntityHealthForTests`, `ClearEntityInventoryForTests`, `ClearEntityInputBufferForTests`, `TrySetEnergyBufferForTests`, `ApplyResearchModifierForTests`, `ComputeCombatDamageForTests`, `CanOccupyWorldPositionForTests`). `InternalsVisibleTo` is granted to `SteelConveyorWar.Core.Tests`, `SteelConveyorWar.Sfml.Tests` (FoW/selection tests), and `SteelConveyorWar.Benchmarks` only. Do **not** friend Sfml/Client runtime assemblies (or future net hosts/bots) without an explicit security review — sharing the Core assembly must not expose cheats on the public surface.
- Remaining public mutation surface is intentional gameplay commands. Adapters must not mutate entity/player fields or inventories directly.
- Construction build costs / ticks / tech gates load from `config/build-costs.json` into `BuildCostCatalog`. Gameplay tables (recipes, combat stats, footprints, stack sizes, power, resistances) load from `config/gameplay-tables.json` into `GameplayTablesCatalog` (R18). Timing constants (mine/conveyor/inserter ticks, commander radii, army caps) and `EntityKind` / unit-factory set helpers remain code-owned in `MvpDefinitions.cs` — do not treat the game as fully data-driven yet. Static `MvpDefinitions` accessors delegate to `GameplayTablesCatalog.Embedded` for Embedded↔disk parity.
- `GameSimulation.CreateNewGame(seed)` creates a deterministic local match from `MapSettings.Default1v1` / embedded catalogs and research profile `mvp-b`. Starting seats (commander/bastion/hub tiles + presentation color) come from the map roster (R31), not hardcoded `PlayerId(1)/(2)` geometry.
- `GameSimulation.CreateNewGame(GameCreationOptions)` accepts research/tile/entity/build-cost/gameplay-tables/map catalogs for tests and composition roots. Hosts load via `SteelConveyorWar.Hosting.ContentBootstrap` (shared Client/Headless I/O, R25/M09) with Core parsers + `ContentCrossValidator` fail-fast (R34).

## Command Trust Boundary

- Core is the authority for entity ownership on gameplay mutators. SFML/UI ownership filters are UX only and must not be the sole gate.
- Entity-targeted commands require an explicit `PlayerId actorPlayerId` and reject intents when `entity.OwnerId != actor` (null owner fails closed): `TryIssueMoveCommand`, `TryStopCommander`, `TryIssueBastionOrder`, `TrySetBastionTemplate`, `TrySetFactoryProduction`, `TrySetAssemblerRecipe`, and `TryRotateEntity`.
- There is **no intentional shared control** in MVP: allies do not share command authority over each other's entities.
- Commander-keyed interact/build/demolish APIs authorize via the supplied commander entity **and** the command `Actor` (R01): forged foreign actor/commander pairs fail closed. Research selection rejects enemy laboratories (R26).
- Malformed / rejected queued commands are logged into `LastTickCommandRejections` and do not abort the tick (R09). Command wire JSON is protocol-versioned (`SimulationCommandSerializer.ProtocolVersion`, R11).
- Test-only helpers (`TryTeleportEntityForTests`, `TryForceCompleteResearch`, `DamageEntity`, etc.) remain god-mode and are not part of the player trust boundary.
- **Production SFML path:** `SfmlPlaySession` uses `DeferredCommandSink` → `EnqueueCommand` (input-delay ticks), not immediate `Try*` for player intents (R02). `SessionState` + `InputCommandMapper` own UI modes and intent→command mapping (R21). Legacy public `Try*` remain for tests and rare host shortcuts; lockstep/replay/fair bots must stay on the queue.

## Config Loading

- **Ownership:** Core owns parse/validate of simulation JSON content (`ResearchContentLoader`, `GameSettingsLoader`, `TileContentLoader`, `EntityContentLoader`, `BuildCostContentLoader`, `GameplayTablesLoader`). `SteelConveyorWar.Hosting.ContentBootstrap` owns shared path resolution and file I/O for Client/Headless (R25/M09), then passes parsed catalogs into `GameCreationOptions`. SFML never parses gameplay JSON; it only receives display options from Client.
- **Authoritative at runtime (Client/Headless fail-fast):** `config/game.json`, `config/research.json`, `config/tiles.json`, `config/entities.json`, `config/build-costs.json` (path via `game.json` → `buildCosts.content`), `config/gameplay-tables.json` (path via `game.json` → `gameplayTables.content`). Missing or invalid files abort startup.
- **Still code-owned:** most timing/radius constants in `MvpDefinitions.cs` (mine/conveyor/inserter ticks, commander radii, army caps). Unit/factory kind sets and combat category helpers remain code. Prefer `GameSimulation.GameplayTables` / `BuildCostCatalog` over static `MvpDefinitions` accessors when a match catalog is available; static accessors remain Embedded parity fallbacks.
- **Partial data-driven behavior (issue #82 slice):** `entities.json` `lossCondition: "Defeat"` drives victory/defeat evaluation via `EntityCatalog.GetDefeatLossKinds()` → `GameSimulation.CheckVictory`. Empty entity catalog (unit-test default) keeps the MVP commander-survival fallback. Tile JSON (`walkable` / `resource`) and entity `buildsStructures` remain registry/metadata fields and do **not** yet replace enum-driven placement, movement, or production. Do **not** claim fully data-driven entities/tiles.
- Embedded `MvpResearchCatalog`, `MvpBuildCostCatalog`, and `GameplayTablesCatalog.Embedded` remain parity fallbacks for unit tests and `GameCreationOptions.Default` only (not for Client/Headless disk startup).
- **Host-only window block:** `game.json` may include a presentation `window` `{ width, height, title }` section. Client `HostDisplayOptionsLoader` parses it into `SfmlDisplayOptions`; Core `GameSettings` / `GameSettingsLoader` intentionally ignore it so sim content stays SFML-free. Side-panel layout scales from window width.
- Local seat binding is host-owned: `SfmlDisplayOptions.LocalPlayerId` (default P1) drives selection/FoW/input/HUD. Client resolves `--local-player <id>` via `LocalPlayerBinding` and passes it through `HostDisplayOptionsLoader.Parse`. Not a Core/config concern yet; not networked multiplayer.
- `simulation.ticksPerSecond` is loaded into `GameSettings.TicksPerSecond` and (R32) threaded into `GameCreationOptions.TicksPerSecond`, which the Client and Headless composition roots now pass on match creation. From there it becomes the per-match instance `GameSimulation.TicksPerSecond`, the single source for all Core duration-in-ticks conversions (build-timing fallback, `EnergyStatsHistory` window/ring capacity) — so changing TPS retimes the match consistently instead of only re-pacing the host loop. The same value still drives Client/SFML fixed-delta pacing via `SfmlDisplayOptions.TicksPerSecond` (through `HostDisplayOptionsLoader.Parse`). Missing/zero falls back to `GameSettings.Default.TicksPerSecond` (30); explicitly negative values fail validation. `GameSimulation.DefaultTicksPerSecond` (30) is the fallback constant used when a match/test does not specify a rate. Headless currently advances by `--ticks` count (not wall-clock TPS). **M11:** TPS + map roster + seed enter `SimulationSessionManifest` for peer handshake (`EnsureMatch`); content-only `SimulationContentManifest` remains the catalog fingerprint. Systems that still use raw tick constants (e.g. repair intervals) are intentionally **tick-based**, not wall-clock — changing TPS scales real-time duration of those intervals via the host pacing loop.
- **Session terminal (M11):** `GameStatus.Draw` when every team is eliminated (mutual wipe); `PlayerWon` when exactly one team remains. Invalid map starts (OOB / footprint overlap) fail at `CreateNewGame` via `ContentCrossValidator`.

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

- Mines and wells produce items into local building inventories on a work cycle driven via `WorkTicksRemaining`/`WorkTicksTotal` each tick (progress bars): iron/copper mines use `OreMineWorkTicks` (=30), coal mines `CoalMineWorkTicks` (=45), oil wells `MineWorkTicks` (=15). Each active work tick drains `PowerDemand` from the building buffer (empty buffer pauses progress); on cycle complete the building adds 1 ore. Full output leaves the building idle (ticks cleared). Labs use the same per-tick drain while running a research cycle.
- Buildings now expose separate input and output buffers. Recipes consume from input buffers and put completed products into output buffers.
- Input and output buffers are limited by per-item stack size definitions in gameplay tables (`ItemStackSizes` / `GameplayTablesCatalog`).
- Inserters move items between adjacent output/input buffers and conveyor slots. An inserter hand can hold only one item with amount `1`.
- When multiple empty-handed inserters pull from the same source entity in one tick, only one may extract (deterministic fair share by tick + source id among candidates ordered by inserter id).
- Conveyor tiles hold at most two item slots and move items in their direction only after `MvpDefinitions.ConveyorMoveTicks`.
- Inserters transfer held items only after `MvpDefinitions.InserterTransferTicks`.
- Conveyor and inserter direction is core state and can be rotated through a simulation API. SFML only renders the arrows and translates hotkeys.
- Conveyor item rendering interpolates draw position from `ProgressTicks / moveTicks` along belt `Direction`; two slots are placed along the belt axis (~25%/75%). Core movement remains discrete hops.
- Oil is represented as `CrudeOil` items refined into `Fuel`. Full fluid pressure, pipe networks and reservoirs are deliberately deferred.
- Energy is tracked as produced versus demanded per player. Each powered consumer has an `EnergyBuffer` with capacity `PowerDemand × 100`. The grid fills buffers emptiest-first each tick (`PowerSystem`): a min-heap ordered by `EnergyBuffer/Capacity` ascending (exact rational compare via cross-multiply / `EnergyFillRatioComparer`, not floating division) then entity id. Distribution uses **batched water-filling** (R15): each heap pop grants every consecutive unit that would still prefer the same entity under the comparer — same final buffers as per-unit dequeue/enqueue, fewer heap ops when production is large. Buildings drain `PowerDemand` from their buffer only while actively producing; empty buffer pauses work progress (soft craft-time inflate removed). Per-player presentation-only `EnergyStatsHistory` ring (sized from match TPS, keyed by absolute `Tick`, not hashed) feeds the SFML energy overlay (**P**): windows 10s/30s/1m/5m/10m; consumption series are **actual** buffer drains; `Query` emits **float** averages over **fixed absolute** buckets (1s / 5s / 10s) so completed graph points never rewrite as the live window slides and sub-1 averages stay visible on the polyline. Both graphs share one Y max and draw axis labels (Y energy/tick, X window time) with equal plot height and an inset frame around the plot region.
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
- Research content validation rejects non-positive / duplicate science packs, negative default allocations, and unknown `TargetTierId` (R28). Lab work attributes pack consumption to the project that spent it and distributes progress with largest-remainder / deterministic track order so weighted allocations are honest over time (R29) — not last-track remainder bias.
- Embedded `MvpResearchCatalog` remains the parity fallback; `eng/ExportResearchCatalog` regenerates `config/research.json` from it. Technologies carry player-facing `DisplayName` / `Description` (Russian via `ResearchDisplayNames`); SFML research overlay lays out gate candidates as a bus graph (mandatory row → shared bus → next tier; optionals hang off the bus).

## Bastions And Combat

- Bastions own desired unit templates (sum capped; research can raise capacity). Factories do **not** store bastion assignment. Autofill picks a missing unit kind across **all** owned bastion templates; spawned units assign to the lowest bastion Id that still needs that kind (no overshoot fallback). Live supply (assigned living units + attributed in-flight factory production) is exposed via `GetBastionUnitSupply`. Factory/bastion accounting uses reusable sorted scratch lists rebuilt each pass, with live mid-tick inserts on spawn and an idle-skip epoch for manual factories when inputs/army epoch are unchanged (R17). SFML shows a center composition overlay for tier-unlocked unit kinds with live/max and +/- wired to bastion template commands.
- Produced units inherit the Bastion's current order (Scout filter applies). Manual factory recipes stay selected after spawn but **do not start** a new craft while player-wide kind supply ≥ summed templates for that kind or total army supply ≥ template capacity (idle wait); autofill clears and re-picks deficits. `TrySetBastionTemplate` enforces the capacity against the **player-wide** sum of all owned bastion templates. Unit spawn searches rings 1–6 for a passable, non-overlapping tile; if none is free the craft stays in-flight (`WorkTicksRemaining = 1`) until a tile opens (no stack on the factory tile).
- Active defense garrisons units inside the Bastion; threats in Bastion vision trigger a sortie (spatial neighborhood query, R07). Bastion death kills assigned units.
- Combat uses deterministic Euclidean range, cooldown, and **formula C** damage: `max(1, AttackDamage - Armor) * Resistance(projectile, targetCategory)` with basis-point integer math (`CombatDamage` / gameplay-tables resistances). HP is clamped to ≥ 0. Each landed shot appends a presentation-only `CombatShotEvent` (not hashed) for SFML tracers.
- Mobile units collide with buildings (circle vs footprint) always. Unit↔unit circle–circle applies only while **stopped**; moving units ignore other units' collision radii (buildings unchanged). Distance checks use distance-squared (ADR 0001). Draw silhouettes scale from `GetCollisionSize`.
- Intermediate craft uses `IronGear` and `Composite` (1 iron + 1 copper plate → 1; work ticks 40 / 60). `CopperWire` / circuit-via-wire are removed. Scout production costs 4×Composite; SciencePackT2 consumes Composite.
- `EntityStats` includes Armor, `ProjectileKind` (`GroundToGround` | `Ballistic` | `AirToGround`), and `SplashRadius` (0 = single target). MG turrets/bots/БМК are G2G; cannon/rocket/medium tank are Ballistic; AA turret/bot are AirToGround. Splash applies in the same `ProcessCombat` pass to enemies near the primary target (ordered by entity id).
- Walls/SteelWalls block **GroundToGround** damage to allied **ground** units (БМК + `UnitKinds` except Scout) when a Bresenham LoS tile between attacker and target holds a Wall/SteelWall owned by a player with the same `TeamId` as the target. Ballistic and AirToGround ignore walls. Buildings and walls as targets still take full formula-C damage.
- Research modifiers (`ResearchStatIds.AttackDamage` / `Armor` / `AttackCooldownTicks` / `MaxHealth`, plus existing `VisionRadius`) flow through `ResolveStat` / `AddModifierEffect`. `ProcessCombat` and FoW/HP sync use resolved values (MaxHealth delta adjusts current HP when the cap changes).
- Victory is evaluated from entity-catalog `lossCondition: "Defeat"` kinds when an entity catalog is loaded (repo `entities.json` marks the commander). Empty catalogs (most unit tests) keep the MVP commander-survival fallback. When more than one `TeamId` is present, victory is declared when **exactly one team** still has living loss-condition entities (`WinnerTeamId`; `WinnerId` is the lowest surviving player id on that team for 1v1 compatibility) — not when a single player dies while allies remain (R31). Friendly fire is disabled for MVP (same `TeamId`). Dead loss-condition entities (including commanders) are purged by `World.RemoveDead` after `CheckVictory` in the same tick (and on post-game `AdvanceTick` if death came from out-of-tick APIs); FoW/render already ignore `!IsAlive`, so retention is not required for corpse UI.
- Baseline combat tables live in `docs/MVP_GDD.md` §12 and `config/gameplay-tables.json` (`entityStats` / `resistances`; Embedded parity via `GameplayTablesCatalog.Embedded`).

## Fog Of War And Tech Signatures

- Each player has a tile visibility mask: unknown, explored, visible.
- Owned entities reveal circular Euclidean-radius vision (integer `dx*dx+dy*dy <= r*r`).
- **Shared vision (MVP):** entities owned by any player with the same `TeamId` contribute to every ally's fog mask each tick.
- Bastion vision radius is elevated relative to other buildings.
- Combat attack range uses the same Euclidean tile check for determinism.
- FoW/tech-signature updates skip full work when vision/signature sources are unchanged; otherwise decay+repaint uses cached vision disks and reusable signature buffers. Presentation-only dirty tiles are exposed via `GameSimulation.GetFogDirtyTiles` / `PlayerState.FogDirtyTiles` (R16) for the minimap cache — not hashed.
- Tech signatures are aggregated into map zones from **non-allied** entities and expose intensity without exact building identity.
- SFML draws a FoW minimap flush top-right of the window into a **cached `RenderTexture`**: full rebuild on size/player/map change (and periodically); otherwise patch fog-dirty tiles / changed entity footprints via `MinimapDirtyTracker`, or blit the cache when nothing changed (R33). **M07:** when `FixedStepPacer` advances multiple ticks in one frame (R23 cap ≤8), the session unions `GetFogDirtyTiles` across every catch-up tick and passes that frame set into `HudOverlay.DrawMinimap` so intermediate FoW dirty is not lost when the final tick is empty. Live entity markers (blue=own / red=enemy / magenta(255,0,255)=ally) use the same Visible gate as the main playfield so Explored does not leak current enemy/ally positions; unknown tiles stay hidden. A viewport rectangle shows the current camera; LMB recenters the camera; RMB issues the same world orders as playfield RMB on the mapped tile. Selection and combat tracers are FoW-gated (R27/H06). **Tracer Policy B:** full From→To only when both endpoints are observable; owned/visible attacker into fog → fixed-length muzzle stub (+X, independent of hidden `To`); visible impact + hidden attacker → fixed-length impact stub (−X, independent of hidden `From`); fair `ObservedCombatEvent` carries `RevealMode` and sanitized endpoints — never exact fog coordinates.
- **Observation API (bots / future net clients):** `GameSimulation.CreatePlayerView(playerId, mode)` returns `IPlayerView` with **immutable tick-stamped snapshots** (R04/R19/M10) — not live mutable `WorldEntity` handles on the fair path. Nested collections are deep-frozen (cast-mutation throws). Prefer `CaptureFrame()` for atomic map+entities+economy decisions: it includes a tick-frozen `FogBoardView` (dense visibility + known terrain for Explored/Visible) alongside entities, own economy/research, tech signatures, command vocabulary, and H06-safe combat events.
  - **Fair** (default): FoW-limited read model — `TryGetTerrain` hides `Unknown` tiles; visible entities/economy/research/combat events are copied snapshots. Prefer this for product AI.
  - **Cheat:** unfiltered live `World` entities/terrain for tests, tools, and explicit god-mode bots. Reading `simulation.World` directly remains possible and is also cheat vision — do not treat it as fair observation.

## Map And Session Lifetime

- MVP match map size is **192×112** tiles (4× the original 48×28 prototype footprint).
- **Static alliances + starts:** `config/maps/default.json` (path via `game.json` → `map.content`) defines the match roster: `id` / `name` / `teamId` plus optional `startCommander` / `startBastion` / `startHub` / `color` (R31). Same `teamId` = allied for the whole match. Default 1v1 is Blue `teamId` 1 vs Red `teamId` 2 with explicit mid-map seats on the 192×112 grid. Seats without start geometry (e.g. roster-only allies in tests) skip spawn; seats 1/2 still resolve classic mirrored defaults when starts are omitted. Dynamic mid-match diplomacy is out of MVP.
- `GameSimulation.CreateNewGame` builds mirrored starting terrain in memory via `CreateStartingTerrain(size, options.RandomSeed)` and stores `RandomSeed` on the simulation. Player `TeamId` and presentation `Color` come from `MapSettings`. `CreateStartingEntities` iterates the roster and places БМК/Bastion/Hub from resolved start tiles; solar remains algorithmic (adjacent grass / mirrored fairness).
- Terrain generation uses a local `System.Random` seeded with `RandomSeed` to jitter patch centers/radii on the left half, then mirrors resource tiles to the right for PvP fairness. Grass remains the default fill. The RNG is not kept for later ticks.
- Starting Fe/Cu patches sit near each base; coal/oil patches sit farther toward the half-map center. Ore fill uses **Chebyshev** distance so each patch AABB is at least **4×4** (radius ≥ 2 → 5×5).
- Each spawned seat starts with БМК, Bastion, Hub, and **one SolarPanel** on grass at Chebyshev distance 1 from the bastion footprint (no resource overlap).
- SFML camera pans with **arrow keys**, **MMB drag**, and **edge-scroll** on the playfield (not WASD). **F1** selects the local БМК and centers the camera. **T** toggles the research overlay; **P** toggles the energy statistics overlay. With a bastion selected, **A/S/D/F** issue Attack/Scout/Defend/Patrol, **E** toggles the composition template overlay, and **1–0** switch among owned bastions. Fixed-step catch-up is capped (R23). Invalid `--local-player` fails closed (R24). World draw is clipped to the playfield; the top bar shows energy + BMK inventory (energy text turns red when `PowerDemand > PowerProduced`); the side panel wraps HUD text including combat stubs (projectile/vision/damage/fire rate/splash/armor). Commander fill color comes from roster `PlayerState.Color`. Buildings (except belts/inserters) and units use geometric pictograms; unit silhouettes are circle (БМК) / square (ground) / triangle (Scout). Build menu composition and affordability use the match `BuildCostCatalog` (H05/R30).
- Terrain and entity layout currently live only for the process lifetime of one match. Exiting the client discards the in-memory grid; the next session regenerates from the same seed + generation parameters.
- `RandomSeed` is accepted from `GameCreationOptions` / `config/game.json` (`simulation.defaultRandomSeed`) and consumed by starting terrain generation (reproducibility covered by `MapGenerationTests`).
- Desired post-MVP reproducibility prefers **seed + generation parameters → regenerate** over opaque map blobs (better for lockstep / fairness than shipping terrain files).
- **MVP non-goal:** no on-disk terrain/seed-map blob format, and no save/load path for world terrain. Map JSON is **roster/alliance metadata only**, not a terrain dump. Aligns with GDD §17 (saves are out of MVP). Full procedural biomes remain out of scope.

## Open Follow-Ups

- Optionally drop `EntityKind` as the primary content key (string `EntityTypeId` only) once catalogs stabilize — R18 stage kept the enum as a legacy mirror.
- Optionally tighten `SteelConveyorWar.Benchmarks` budgets further from CI artifact trends (R22/M04 hard gate is on in CI; local stays soft unless `SCW_BENCH_HARD_GATE=1`).
- Tune energy demand/production balance and optional consumer priority tiers beyond emptiest-first once production loops are playtested.
- Expand SFML research controls from prototype paging/hotkeys to a dedicated full tree panel; optional further `HudOverlay` subpanel split.
- Replace simplified oil item movement with a dedicated fluid network if T2 playtests show it is needed.
- Network transport / matchmaker / rollback netcode remain out of scope (command queue + content manifest + millitiles are Core prerequisites).

## Tick-Stamped Command Queue

- Gameplay intents are `ISimulationCommand` DTOs (`SteelConveyorWar.Core.Commands`) with `Kind`, actor `PlayerId`, `Tick`, and canonical `Sequence` (R03). Payloads that carry collections are deep-frozen (R12).
- Hosts call `GameSimulation.EnqueueCommand` / `EnqueueForNextTick` (or `DeferredCommandSink` / `IPlayerCommandSink`); `AdvanceTick` increments `Tick`, then applies pending commands for the current tick in canonical actor/sequence order, then runs systems.
- Covered intents: move/stop, ghost/queue build & demolish, rotate, research start/cancel/select & track allocation, factory production/bastion assign, bastion template/order, assembler recipe, hub/output deposit/withdraw (typed and untyped), collect output.
- Legacy public `Try*` methods still mutate **immediately** for tests and rare host shortcuts. **SFML production input uses the queue.** Lockstep, replay, and fair bot logs must use the queue. `ApplyCommand` dispatches a DTO through the same handlers.
- Serialization: versioned envelope via `SimulationCommandSerializer` (`protocolVersion`, kind, actor, tick, sequence, payload). Enums as camelCase strings; `TechnologyId` as its string value; bastion waypoints as ordered `{x,y}` arrays; track allocations written sorted by track id. Round-trip covered by `CommandProtocolTests` / property tests (R11/R22).
- Determinism gate: same seed + same queued commands → same `ComputeStateHash` (`DeterminismHashTests`, `CommandQueueTests`).

## Authoritative Numeric Policy

- **Source of truth:** [`docs/adr/0001-authoritative-numeric-policy.md`](adr/0001-authoritative-numeric-policy.md).
- **Lockstep claim:** authoritative continuous positions are integer **millitiles** (`WorldPosition`/`CollisionSize.RadiusMilli`, 1 tile = 1000). Cross-runtime dual-run hash equality is in scope for position/collision math at `AlgorithmVersion` 8+.
- **Mitigations in Core:** energy emptiest-first uses integer cross-multiply ratios; ground A* uses integer octile costs (`10`/`14`); collision / interact / build radius gates use distance-squared; movement step uses deterministic `WorldUnits.IntegerSqrt` (no IEEE `Sqrt`).
- **Presentation-only floats (not hashed / not gameplay):** SFML camera and millitile→pixel conversion (`ToTileSpaceX/Y`), belt item draw lerp, HUD energy/craft bars, combat tracer endpoints, `EnergyStatsHistory` overlay series, unit silhouette scaling from collision radius for draw.

## Simulation State Hash

- `SimulationStateHasher.AlgorithmVersion` (currently **`9`**) fingerprints authoritative Core state: seed, tick, status, **content manifest** (research + build costs + entities + tiles + gameplay tables, R10/`SimulationContentManifest`), next entity id, terrain, ordered players (teamId/inventory/visibility/research — including **largest-remainder track/project accumulators** (H02) — /power), ordered entities (buffers, energy buffer, sticky smelt recipe, work totals, paths, combat/build fields, **queued commander orders** R08, bastion order waypoints).
- Authoritative world positions hash as raw `int64` millitiles (not IEEE doubles). Unordered collections are sorted before hashing.
- **Pending queue fingerprint:** peers compare `(ComputeStateHash, ComputePendingCommandsHash)` as the full session pair (R13). Applied commands affect state hash; the pending buffer is hashed separately so divergent queues are observable before apply.
- Primary quality gate: dual independent runs with the same seed/commands must match (`DeterminismHashTests`). A checked-in golden hex is optional; when adding/updating one, bump `AlgorithmVersion` if the surface changed, re-run the fixture, and commit the new constant intentionally.
- **Issue #80 decision:** do **not** check in a golden hex yet. Dual-run covers accidental non-determinism; a golden would mostly regress on intentional edits. Add a CI-asserted golden later once multiplayer lockstep needs a fixed oracle.
- Out of surface: SFML/UI, wall-clock, presentation-only floats listed under Authoritative Numeric Policy, FoW dirty tile lists, and **presentation side-channels in Core** (below).
- Starting terrain generation already consumes `RandomSeed` (see Map And Session Lifetime).

## Performance Gate

- `tests/SteelConveyorWar.Benchmarks` (BenchmarkDotNet) exercises idle_factory / army_move / battle / power_equal / fow_moving / hash scenarios (R22/M04).
- CI job `benchmark-quick` runs `--quick` with **hard** budgets (`SCW_BENCH_HARD_GATE=1`) and uploads JSON artifacts. Local/`eng/verify.ps1` stay soft unless that env var is set (override for experimentation).
- Calibrated budgets: p95 ≤ 5 ms/tick, alloc ≤ 2.5 MB/tick (~3–5× measured Release headroom on the expanded matrix; hash covers `ComputeStateHash` scratch).

## Presentation state in Core

MVP keeps a few presentation-only side-channels inside Core so SFML can draw tracers / overlays without owning sim-derived FX. They are intentional, not a license to grow an SFML dependency in Core.

| Field / API | Location | Consumer | Hashed? |
|-------------|----------|----------|---------|
| `SimulationPresentationSink` / `CombatShotsThisTick` | `GameSimulation.Presentation` (alias `CombatShotsThisTick`) | SFML combat tracers | **No** |
| `EnergyStats` (`EnergyStatsHistory`) | `PlayerState` | SFML energy overlay (**P**) | **No** |
| `TechSignatures` | `PlayerState` (via `GetTechSignatureHotspots`) | SFML fog tech-signature overlay | **No** |
| `FogDirtyTiles` / `GetFogDirtyTiles` | `PlayerState` / `GameSimulation` | SFML minimap dirty cache (R16/R33) | **No** |

**Hash exclusion policy**

- Authoritative lockstep / dual-run identity uses only `SimulationStateHasher` surfaces. Presentation fields must never be written into the hasher.
- Adding a new Core field that only serves UI/FX: document it in this table, mark it presentation-only in XML docs, omit it from the hasher, and extend `DeterminismHashTests.PresentationSideChannels_DoNotAffectStateHash` (or an equivalent comment gate on `SimulationStateHasher`).
- Do **not** remove combat tracers or the energy overlay as part of clarifying this boundary; isolation/docs first. Moving FX fully out of Core is a later refactor if multipath/replay needs a cleaner event bus.
