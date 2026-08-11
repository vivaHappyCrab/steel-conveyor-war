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

- `SfmlInputHelpers.cs:49-59`
- Call sites in `InputCommandMapper` (F1 / copy / build paths)

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
