# L03 — UI mode exclusivity including build menu

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § L3  
**Severity:** Low · **Домен:** SFML UX state machine · **Roadmap:** P2  
**Related residual:** R21  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

`PrepareOpenResearchExclusive` / Energy / Bastion close each other but **do not close build mode**. Build bar can remain open under research/energy overlays; left-click hit-test prefers build bar first.

UX confusion, not simulation correctness.

---

## Доказательства

- `SessionState.cs:224-243` — exclusive helpers omit build
- `SfmlPlaySession.cs:188-207` — build bar click priority
- `SfmlPlaySession` draw path can show build bar + overlays

---

## План фикса

1. Define mode enum/flags: `None | Build | Research | Energy | BastionCompose` (patrol may be orthogonal).
2. Opening any exclusive mode closes others including build (`IsBuildMenuOpen=false`, clear pending build).
3. Opening build closes research/energy/bastion compose.
4. Click routing follows single active mode.
5. `SessionStateTests` for mutual exclusion matrix.

---

## Definition of Done

- [ ] At most one exclusive overlay/mode active.
- [ ] Tests cover transitions.
- [ ] No simulation changes.

## Специалисты

`sfml-platform`, `test-ci`.
