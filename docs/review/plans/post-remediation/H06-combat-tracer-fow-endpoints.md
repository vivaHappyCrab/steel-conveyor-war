# H06 — Combat tracer / observation без раскрытия скрытого endpoint

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § H6  
**Severity:** High · **Домен:** FoW / fair observation / competitive integrity · **Roadmap:** P0  
**Related residual:** R27 (partial), R19 (events)  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

Shot считается видимым, если виден **любой** participant или endpoint tile. Затем полный `From → To` рисуется/отдаётся боту. Видимый стрелок раскрывает скрытую цель (и наоборот).

R27 закрыл global unfiltered tracers и hidden selection, но закрепил unsafe OR-policy в тестах.

---

## Доказательства

| Участок | Что происходит |
|---|---|
| `WorldRenderer.cs:155-173` | `IsShotVisibleToLocalPlayer` — OR endpoints |
| `WorldRenderer.cs:94-112` | `DrawCombatShots` рисует полную линию |
| `SfmlPlaySession.cs:453-460` | Buffers shots passing OR gate |
| `PlayerView.cs:138-155,176-199` | Same OR policy; returns both coordinates |
| `CombatShotVisibilityTests.cs:41-47` | Codifies OR + «owned attacker always visible» (incl. fog target) as expected |
| Bot fair events | `ObservedCombatEvent` carries both positions — mirrors SFML leak surface |

---

## Целевой дизайн (выбрать и зафиксировать в GDD/decisions)

### Policy A — Both endpoints required (строгая)

Полный tracer только если attacker **и** target observable (или оба tiles Visible). Иначе shot не показывается.

Плюсы: просто, no leak. Минусы: «выстрел в туман» не виден даже у своего юнита стреляющего в FoW (можно исключение: own attacker always shows muzzle-only).

### Policy B — Clip to visibility (предпочтительная UX)

Если виден только один конец:

- own/visible attacker + hidden target → показать muzzle segment / line до края visible region, **без** точного hidden To;
- visible target + hidden attacker → impact marker only, без точного From.

Bot events: `ObservedCombatEvent` получает nullable/optional hidden endpoint или separate event kinds (`MuzzleFlash`, `Impact`).

### Policy C — Team-aware

Own shots always show full line; enemy shots require both ends. Weaker vs wallhacks using own splash visibility — still prefer B for enemies.

**Рекомендация:** Policy B для presentation + bot events; document in `MVP_IMPLEMENTATION_DECISIONS.md`.

---

## План фикса

1. Зафиксировать policy в docs (decisions + GDD FoW note).
2. Заменить OR gate на visibility classification:
   - `ShotReveal.Full | MuzzleOnly | ImpactOnly | Hidden`.
3. Core `PlayerView.GetEventsThisTick` emits events without leaking hidden coordinates.
4. SFML maps reveal mode to draw primitives (full line / half / marker).
5. Rewrite `CombatShotVisibilityTests` + add bot observation tests.
6. Ensure lingering shots re-check visibility each frame or store reveal mode at capture time carefully (don't later upgrade Hidden→Full without new visibility).

---

## Тесты

- Visible attacker, hidden target → no exact target coord in observation/render path
- Visible target, hidden attacker → no exact attacker coord
- Both visible → full event
- Own unit firing into FoW → allowed under chosen policy (document)
- Selection still clears when entity leaves vision (R27 regression)

---

## Риски / non-goals

**Риски:** UX change vs current tracers; players may perceive «missing shots».  
**Non-goals:** ballistics simulation; networked lag compensation.

---

## Definition of Done

- [ ] Hidden endpoint coordinates never leave Core observation / SFML draw for Fair mode.
- [ ] Tests assert non-leak explicitly.
- [ ] Policy documented.
- [ ] R27 residual closed.

---

## Верификация

```powershell
dotnet test tests/SteelConveyorWar.Core.Tests -c Release --filter "PlayerView|BotObservation|CombatShot"
dotnet test tests/SteelConveyorWar.Sfml.Tests -c Release --filter CombatShot
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
```

## Специалисты

`sfml-platform`, `multiplayer-correctness`, `game-design-consistency`, `test-ci`.
