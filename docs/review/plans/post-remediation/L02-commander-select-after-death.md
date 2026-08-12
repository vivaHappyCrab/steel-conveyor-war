# L02 — F1 / EnsureLocalCommanderSelected after commander death

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § L2  
**Severity:** Low · **Домен:** SFML robustness · **Roadmap:** P2  
**Related residual:** R24  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

`EnsureLocalCommanderSelected` uses `.First(...)` on living local commander. After local commander dies (defeat), F1/hotkeys can throw `InvalidOperationException`.

---

## Доказательства

- `SfmlInputHelpers.cs:49-59` — `.First(...)` на живом local commander
- Callers: F1 `InputCommandMapper.cs:137-143`; Q-copy `:153-156`
- Start guard `LocalPlayerBinding.EnsureSeatControllable` (`:54-60`) покрывает только session start, **не** mid-match death
- После defeat `AdvanceTick` early-return (`GameSimulation.cs:1600-1605`), но окно/input продолжают работать

---

## План фикса

1. Replace `First` with `FirstOrDefault`.
2. If none: leave selection unchanged / clear selection; return false.
3. Mapper treats false as handled no-op.
4. Test: world without living local commander → no throw.

---

## Definition of Done

- [ ] No throw after commander death on F1/ensure paths.
- [ ] Unit test covers empty commander set.

## Специалисты

`sfml-platform`, `test-ci`.
