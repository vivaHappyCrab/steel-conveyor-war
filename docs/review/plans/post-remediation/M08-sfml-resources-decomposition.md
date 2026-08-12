# M08 — SFML resource lifetime + session decomposition

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M8  
**Severity:** Medium · **Домен:** SFML maintainability / native resources · **Roadmap:** P1/P2  
**Related residual:** R21 (partial), R33  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

1. `WorldRenderer.DrawWorld` создаёт `RectangleShape` каждый frame без `Dispose`/`using` (native SFML object).
2. `SfmlPlaySession.Run` всё ещё owns window events, camera, pacing, tick, visibility buffering, render composition.
3. `InputCommandMapper` одновременно читает live simulation, мутирует UI state и enqueue-ит команды — не pure intent mapper.

R21 extracted SessionState/mapper, but session god-method remains.

---

## Доказательства

- `WorldRenderer.cs:24-40` — undisposed `RectangleShape`
- `SfmlPlaySession.cs:17-110,431-649` — broad Run loop
- `InputCommandMapper.cs:15-18,52-97` — live sim + state + commands

---

## План фикса

### P0-small (ship first)

1. Wrap tile `RectangleShape` in `using` or reuse a cached shape instance per draw.
2. Audit other SFML draw helpers for missing dispose.

### P1 decomposition

3. Split `SfmlPlaySession` into:
   - `FrameHost` (window/events/clock);
   - `SimulationPump` (pacer + AdvanceTick + shot buffer);
   - `PresentationComposer` (views/overlays);
   - keep `SessionState` as pure UI state.
4. Refactor `InputCommandMapper` toward:
   - input snapshot in;
   - `IReadOnlyList<Intent>` / commands out;
   - session mutations via explicit commands to `SessionState`;
   - avoid deep live world queries where observation snapshot suffices (coordinate with M10/H06).
5. Unit tests without window already exist for mapper/state — extend as modules shrink.

---

## Тесты

- No new native leak under smoke (manual / long smoke optional)
- Existing SFML tests green
- Mapper tests cover intent emission without needing full session

---

## Definition of Done

- [x] Frame tile shape disposed/reused.
- [x] Session responsibilities split enough that Run is orchestration-only (<~200 lines ideal).
- [x] R21 residual updated honestly (full pure mapper may remain partial).

## Специалисты

`sfml-platform`, `engine-architect`, `test-ci`.
