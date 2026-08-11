# L01 — Trailing `--local-player` must error

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § L1  
**Severity:** Low · **Домен:** CLI UX · **Roadmap:** P2  
**Related residual:** R24  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

`LocalPlayerBinding.Resolve` iterates `i < args.Count - 1`, so a trailing `--local-player` without value is silently ignored and default seat is used.

---

## Доказательства

- `Sfml/LocalPlayerBinding.cs:13-32`

---

## План фикса

1. Scan all args; if flag present at last index or next token is another flag → throw `ArgumentException` with clear message.
2. Keep existing invalid/non-positive value errors.
3. Test: `["--local-player"]` throws; `["--local-player","2"]` OK; `["--smoke-test"]` default.

---

## Definition of Done

- [ ] Trailing flag errors.
- [ ] Unit test covers case.
- [ ] Smoke still passes with valid args.

## Специалисты

`sfml-platform`, `test-ci`.
