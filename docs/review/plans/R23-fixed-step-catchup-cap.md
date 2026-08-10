# R23 — Ограничить catch-up в fixed-step цикле

**Severity:** Low · **Домен:** SFML loop / robustness · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
`while (accumulator >= fixedDelta)` не ограничивает число тиков за кадр. После длинной паузы (свернутое окно, точка останова) возможна spiral-of-death: симуляция пытается «догнать» огромное число тиков за один кадр. Нужна документированная catch-up/drop политика, особенно перед network host.

## Где в коде
- `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs:872-925` — цикл `while (accumulator >= fixedDelta)` без max ticks/frame.

## План фикса
1. Ввести `MaxTicksPerFrame` (например 5–10). После достижения — прекратить catch-up в этом кадре.
2. Политика для остатка: либо «drop» лишнего accumulator (clamp), либо задокументированное отставание. Зафиксировать выбор в комментарии/доке.
3. Для будущего network host — согласовать с lockstep-темпом (drop недопустим в lockstep; там нужен stall/resync).

## Тесты
- Тест: искусственно большой `deltaTime` не приводит к многотысячному числу тиков за кадр (ограничено `MaxTicksPerFrame`).

## Definition of Done
- Число тиков за кадр ограничено; политика остатка задокументирована.

## Связанные замечания
R32 (TPS/pacing), R2 (network path).
