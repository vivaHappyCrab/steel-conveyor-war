# M11 — Session manifest, map validation, zero-team terminal

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M11  
**Severity:** Medium · **Домен:** multiplayer session / roster · **Roadmap:** P1  
**Related residual:** R10 (partial), R31 (partial), R32 (partial)  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

1. Content manifest omits TPS, map id/roster/teams/starts — peers can match content hash with different session settings.
2. Map starts parsed/spawned without bounds/overlap validation.
3. Victory handles exactly one active team; zero active teams leaves `InProgress`.

---

## Доказательства

- `SimulationContentManifest.cs:22-33` — только catalogs (research/profile/build/entity/tile/gameplay); **нет** TPS/map/roster/seed/algorithm
- TPS на match (`GameCreationOptions` / `TicksPerSecond`) есть (R32), но в manifest не входит (R10 deviation)
- `MapSettingsLoader.cs:98-106` — coords без bounds; triple completeness only `:63-70`
- `GameSimulation.cs:1624-1645` — `ResolveStartPositions` без overlap/footprint checks; `AddCompletedEntity` `:1742-1747` добавляет вслепую
- `VictorySystem.cs:30-45` — ровно одна active team → win
- `GameStatus` = только `InProgress | PlayerWon` (`Enums.cs:89-93`) → `activeTeams.Count == 0` оставляет **вечный** `InProgress`
- Tests: `TeamVictoryTests` / `MapStartPositionsTests` — happy paths; нет OOB/overlap/zero-team

---

## Целевой дизайн

```text
SessionManifestHash = H(
  AlgorithmVersion,
  ContentManifest,
  MapId + roster(players/teams/starts/colors),
  TicksPerSecond,
  RandomSeed,
  ResearchProfileId
)
EnsureSessionMatch(local, remote) fail-fast before tick 0
```

Map validation at load:

- starts inside world;
- commander/bastion/hub footprints non-overlapping per player and across players as required by design;
- each controllable seat has commander start.

Victory:

- `activeTeams.Count == 0` → `GameStatus.Draw` / `NoWinner` (pick existing enum or add); deterministic.

TPS:

- include in session manifest;
- audit remaining literal 30-tick assumptions (repair intervals etc.) — either scale by TPS or document fixed-tick semantics vs real-time.

---

## План фикса

1. Add `SimulationSessionManifest.Compute(GameSimulation)` + `EnsureMatch`.
2. Fold into handshake docs; optionally into state hash header.
3. Validate starts in `MapSettingsLoader` or `GameSimulation` ctor (fail-fast).
4. Zero-team terminal + tests (mutual elimination).
5. TPS: manifest + decide policy for systems using raw tick constants (document if intentionally tick-based not realtime).
6. Tests: mismatched TPS fails EnsureMatch; overlapping starts rejected; draw status.

---

## Definition of Done

- [ ] Session identity includes map/roster/TPS/seed.
- [ ] Invalid starts fail load.
- [ ] Zero-team match terminates.
- [ ] R10/R31/R32 residuals updated.

## Специалисты

`multiplayer-correctness`, `map-generation`, `core-simulation`, `test-ci`.
